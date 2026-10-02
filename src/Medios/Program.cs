using Medios.Infrastructure.Data;
using Medios.Infrastructure.Middleware;
using Medios.Security;
using Medios.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using System.Net;
using System.Text;
using QuestPDF.Infrastructure;

namespace Medios
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Habilita encodings legacy (windows-1252, iso-8859-1, etc.) para decodificar
            // páginas scrapeadas que no declaran UTF-8 (ver ScraperService.DecodificarHtml).
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true)
                .AddEnvironmentVariables();

            // MVC
            builder.Services.AddControllersWithViews(options =>
                {
                    options.Filters.Add(new ResponseCacheAttribute
                    {
                        NoStore = true,
                        Location = ResponseCacheLocation.None
                    });
                })
                .AddJsonOptions(options =>
                {
                    options.JsonSerializerOptions.PropertyNamingPolicy =
                        System.Text.Json.JsonNamingPolicy.CamelCase;
                });

            builder.Services.AddHttpContextAccessor();

            // Auth
            var authMode = builder.Configuration["Auth:Mode"];

            Console.WriteLine($"ENV: {builder.Environment.EnvironmentName}");
            Console.WriteLine($"AUTH MODE: {authMode}");

            if (authMode == "Cerberus")
            {
                var baseUrl = builder.Configuration["Cerberus:BaseUrl"];
                var apiKey = builder.Configuration["Cerberus:ServiceApiKey"];
                if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiKey))
                    throw new Exception("Cerberus mal configurado: faltan BaseUrl o ServiceApiKey");
            }

            switch (authMode)
            {
                case "Mock":
                    builder.Services.AddScoped<IAuthService, MockAuthService>();
                    break;
                case "Cassandra":
                    builder.Services.AddHttpClient<IAuthService, CassandraAuthService>()
                        .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
                        {
                            UseCookies = true,
                            CookieContainer = new CookieContainer()
                        });
                    break;
                case "Cerberus":
                default:
                    builder.Services.AddHttpClient<IAuthService, CerberusAuthService>();
                    break;
            }

            builder.Services.AddScoped<AuthCookieService>();

            builder.Services.AddAuthentication("Cookies")
                .AddCookie("Cookies", options =>
                {
                    options.LoginPath = "/Login";
                    options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
                });

            builder.Services.AddAuthorization(options =>
            {
                options.FallbackPolicy = new Microsoft.AspNetCore.Authorization.AuthorizationPolicyBuilder()
                    .RequireAuthenticatedUser()
                    .Build();
            });

            // Session
            builder.Services.AddSession(options =>
            {
                options.IdleTimeout = TimeSpan.FromMinutes(30);
                options.Cookie.HttpOnly = true;
                options.Cookie.IsEssential = true;
            });

            // DB
            builder.Services.AddScoped<IMediosDbContextFactory, MediosDbContextFactory>();

            // Services
            builder.Services.AddScoped<MenuXmlService>();
            builder.Services.AddScoped<AuditoriaService>();
            builder.Services.AddScoped<AutorizacionService>();
            builder.Services.AddScoped<SesionService>();
            builder.Services.AddScoped<NotaService>();
            builder.Services.AddScoped<SintesisService>();
            builder.Services.AddScoped<DelegacionService>();
            builder.Services.AddScoped<ConfiguracionService>();
            builder.Services.AddScoped<EmailService>();
            builder.Services.AddScoped<TrazaService>();
            builder.Services.AddScoped<ScrapingReglaService>();
            builder.Services.AddScoped<SincronizacionService>();
            builder.Services.AddScoped<CapturaDriveService>();
            builder.Services.AddHttpClient<IAService>();
            builder.Services.AddHttpClient<ScraperService>();
            builder.Services.AddHttpClient<ImagenService>();

            // QuestPDF community license (proyectos internos sin fines de lucro)
            QuestPDF.Settings.License = LicenseType.Community;

            var app = builder.Build();

            var forwardedHeadersOptions = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            };
            // Trust forwarded headers only when the deployment explicitly identifies its proxy.
            var knownProxy = builder.Configuration["ReverseProxy:KnownProxy"];
            if (IPAddress.TryParse(knownProxy, out var proxyIp))
            {
                forwardedHeadersOptions.KnownProxies.Add(proxyIp);
                forwardedHeadersOptions.KnownProxies.Add(proxyIp.MapToIPv6());
            }
            app.UseForwardedHeaders(forwardedHeadersOptions);

            app.UseHttpsRedirection();
            app.UseStaticFiles();
            app.UseRouting();

            app.UseMiddleware<ErrorMiddleware>();
            app.UseSession();
            app.UseAuthentication();
            app.UseAuthorization();
            app.UseMiddleware<MenuAuthorizationMiddleware>();
            app.UseMiddleware<AutorizacionMiddleware>();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Login}/{action=Index}/{id?}");

            await app.RunAsync();
        }
    }
}
