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
        private readonly ILogger<MediosDbContextFactory> _logger;

        public MediosDbContextFactory(IConfiguration configuration, ILogger<MediosDbContextFactory> logger)
        {
            _configuration = configuration;
            _logger = logger;
        }

        public MediosDbContext Create()
        {
            var connStr = _configuration.GetConnectionString("MySql")
                ?? throw new InvalidOperationException("Connection string 'MySql' no configurada.");

            var builder = new MySqlConnectionStringBuilder(connStr)
            {
                Database = "medios",
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
