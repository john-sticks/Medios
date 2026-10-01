using System.Net;
using System.Net.Mail;

namespace Medios.Services
{
    // Envío de correos vía SMTP. La configuración se toma de la tabla `configuracion`
    // (claves smtp_*), editable desde Administración → Configuración Mail; si una clave
    // no está en la DB se usa el valor de appsettings sección "Smtp" como respaldo.
    public class EmailService
    {
        private readonly IConfiguration _config;
        private readonly ConfiguracionService _cfgDb;
        private readonly ILogger<EmailService> _logger;

        public EmailService(IConfiguration config, ConfiguracionService cfgDb, ILogger<EmailService> logger)
        {
            _config = config;
            _cfgDb = cfgDb;
            _logger = logger;
        }

        // Lee una clave: primero de la DB (smtp_*), si está vacía cae a appsettings Smtp:*
        private async Task<string?> LeerAsync(string claveDb, string claveAppsettings)
        {
            var v = await _cfgDb.GetAsync(claveDb);
            if (!string.IsNullOrWhiteSpace(v)) return v;
            return _config[claveAppsettings];
        }

        public async Task<bool> EstaConfiguradoAsync()
        {
            var host = await LeerAsync("smtp_host", "Smtp:Host");
            return !string.IsNullOrWhiteSpace(host);
        }

        // Envía un mail con adjunto opcional. Retorna (ok, error).
        public async Task<(bool Ok, string? Error)> EnviarAsync(
            IEnumerable<string> destinatarios, string asunto, string cuerpoHtml,
            string? adjuntoPath = null, string? adjuntoNombre = null)
        {
            var host = await LeerAsync("smtp_host", "Smtp:Host");
            if (string.IsNullOrWhiteSpace(host))
                return (false, "El servidor de correo (SMTP) no está configurado.");

            var listaDest = destinatarios
                .Where(d => !string.IsNullOrWhiteSpace(d))
                .Select(d => d.Trim())
                .Distinct()
                .ToList();
            if (listaDest.Count == 0)
                return (false, "No se indicaron destinatarios.");

            try
            {
                var portStr   = await LeerAsync("smtp_port", "Smtp:Port");
                var user      = await LeerAsync("smtp_user", "Smtp:User");
                var pass      = await LeerAsync("smtp_pass", "Smtp:Pass");
                var from      = await LeerAsync("smtp_from", "Smtp:From");
                var fromName  = await LeerAsync("smtp_fromname", "Smtp:FromName") ?? "División Medios O.S.INT";
                var sslStr    = await LeerAsync("smtp_enablessl", "Smtp:EnableSsl");

                var port = int.TryParse(portStr, out var p) ? p : 587;
                from = string.IsNullOrWhiteSpace(from) ? (user ?? "no-reply@medios.local") : from;
                var enableSsl = !bool.TryParse(sslStr, out var ssl) || ssl;

                using var msg = new MailMessage
                {
                    From = new MailAddress(from, fromName),
                    Subject = asunto,
                    Body = cuerpoHtml,
                    IsBodyHtml = true
                };
                foreach (var d in listaDest)
                    msg.To.Add(d);

                Attachment? attach = null;
                if (!string.IsNullOrWhiteSpace(adjuntoPath) && File.Exists(adjuntoPath))
                {
                    attach = new Attachment(adjuntoPath);
                    if (!string.IsNullOrWhiteSpace(adjuntoNombre))
                        attach.Name = adjuntoNombre;
                    msg.Attachments.Add(attach);
                }

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = enableSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };
                if (!string.IsNullOrWhiteSpace(user))
                    client.Credentials = new NetworkCredential(user, pass);

                await client.SendMailAsync(msg);
                attach?.Dispose();
                return (true, null);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enviando correo SMTP");
                return (false, $"Error al enviar el correo: {ex.Message}");
            }
        }

        // Parsea "a@x.com; b@y.com" → lista
        public static List<string> ParseMails(string? raw) =>
            string.IsNullOrWhiteSpace(raw)
                ? new()
                : raw.Split(new[] { ';', ',', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                     .Select(s => s.Trim())
                     .Where(s => s.Contains('@'))
                     .Distinct()
                     .ToList();
    }
}
