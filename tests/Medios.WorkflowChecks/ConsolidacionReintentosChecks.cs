using Medios.Entities;
using Medios.Infrastructure.Data;
using Medios.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;

static class ConsolidacionReintentosChecks
{
    public static async Task<int> RunAsync()
    {
        var factory = new RetryFactory();
        using (var db = factory.Create())
        {
            db.Delegaciones.Add(new Delegacion { Id = 1, Nombre = "Prueba" });
            db.SesionesPrensas.Add(new SesionPrensa { Id = 1, DelegacionId = 1,
                Fecha = new DateOnly(2026, 10, 29), Turno = "Vespertina", Estado = "Finalizada" });
            await db.SaveChangesAsync();
        }
        factory.Probe.Armed = true;
        var service = new SintesisService(factory, null!, null!);
        var id = await service.CrearAsync(new DateOnly(2026, 10, 29), "Especial", "QA", [],
            consolidada: true, sesionesFuente: [1]);
        factory.Probe.Armed = false;
        using var checkDb = factory.Create();
        if (id <= 0 || await checkDb.Sintesis.CountAsync() != 1)
            throw new InvalidOperationException("El reintento debe crear una única síntesis");
        var session = await checkDb.SesionesPrensas.FindAsync(1);
        if (session!.Estado != "Consolidada" || session.SintesisConsolidadaId != id)
            throw new InvalidOperationException("El reintento debe completar el vínculo con la sesión");
        if (factory.Probe.Attempts.Count != 2)
            throw new InvalidOperationException("Cada intento debe usar un contexto nuevo");
        var obsolete = await service.CrearAsync(new DateOnly(2026, 10, 29), "Especial", "QA", [],
            consolidada: true, sesionesFuente: [1]);
        if (obsolete != -3 || await checkDb.Sintesis.CountAsync() != 1)
            throw new InvalidOperationException("Una sesión ya consolidada no debe volver a consolidarse");
        return 4;
    }

    private sealed class TransientFailure : Exception { }
    private sealed class RetryStrategy(ExecutionStrategyDependencies dependencies)
        : ExecutionStrategy(dependencies, 1, TimeSpan.Zero)
    {
        protected override bool ShouldRetryOn(Exception exception) => exception is TransientFailure;
    }
    private sealed class StrategyFactory(ExecutionStrategyDependencies dependencies) : IExecutionStrategyFactory
    {
        public IExecutionStrategy Create() => new RetryStrategy(dependencies);
    }
    private sealed class RetryProbe : SaveChangesInterceptor
    {
        public bool Armed;
        public HashSet<Guid> Attempts { get; } = [];
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Armed)
            {
                if (ExecutionStrategy.Current == null)
                    throw new InvalidOperationException("La consolidación debe ejecutarse dentro de la estrategia de reintentos");
                Attempts.Add(eventData.Context!.ContextId.InstanceId);
                if (Attempts.Count == 1) throw new TransientFailure();
            }
            return ValueTask.FromResult(result);
        }
    }
    private sealed class RetryFactory : IMediosDbContextFactory
    {
        private readonly string name = Guid.NewGuid().ToString();
        public RetryProbe Probe { get; } = new();
        public MediosDbContext Create() => new(new DbContextOptionsBuilder<MediosDbContext>()
            .UseInMemoryDatabase(name).ReplaceService<IExecutionStrategyFactory, StrategyFactory>()
            .AddInterceptors(Probe).Options);
    }
}
