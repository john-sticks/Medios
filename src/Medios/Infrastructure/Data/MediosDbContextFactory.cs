using Medios.Services.Respaldos;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;
using Medios.Entities;

namespace Medios.Infrastructure.Data
{
    public interface IMediosDbContextFactory
    {
        MediosDbContext Create();
    }

    public class MediosDbContextFactory : IMediosDbContextFactory
    {
        private readonly IConfiguration _configuration;
        private readonly RespaldoStore? _store;
        private readonly DatosTestContext? _datos;
        private readonly ILogger<MediosDbContextFactory> _logger;

        public MediosDbContextFactory(IConfiguration configuration, ILogger<MediosDbContextFactory> logger, RespaldoStore? store = null, DatosTestContext? datos = null)
        {
            _configuration = configuration;
            _logger = logger; _store = store; _datos = datos;
        }

        public virtual MediosDbContext Create() => Crear(_store?.Habilitado == true && _datos?.Copia == true);
        public virtual MediosDbContext CreateCopy() => _store?.Habilitado == true ? Crear(true) : throw new InvalidOperationException("Copia habilitada solo en test.");
        private MediosDbContext Crear(bool copia)
        {
            var connStr = (copia ? _configuration["TestBackup:CloneConnectionString"] : _configuration.GetConnectionString("MySql"))
                ?? throw new InvalidOperationException("Connection string 'MySql' no configurada.");

            var builder = new MySqlConnectionStringBuilder(connStr)
            {
                Database = copia ? RespaldoStore.BaseCopia : "medios",
                ConnectionTimeout = 10,
                DefaultCommandTimeout = 30,
                Pooling = true,
                MinimumPoolSize = 0,
                MaximumPoolSize = 10,
                ConnectionReset = false
            };

            var options = new DbContextOptionsBuilder<MediosDbContext>()
                .UseMySql(builder.ConnectionString, ServerVersion.AutoDetect(builder.ConnectionString),
                    o => o.EnableRetryOnFailure(maxRetryCount: 2))
                .LogTo(msg => _logger.LogDebug(msg), Microsoft.Extensions.Logging.LogLevel.Warning)
                .Options;

            return new MediosDbContext(options);
        }
    }
}
