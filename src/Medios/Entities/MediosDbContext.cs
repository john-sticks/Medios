using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

public class MediosDbContext : DbContext
{
    public MediosDbContext(DbContextOptions<MediosDbContext> options) : base(options) { }

    // Infraestructura (compartida con Zeus)
    public DbSet<Auditoria> Auditorias { get; set; }
    public DbSet<AutorizacionUsuario> AutorizacionesUsuario { get; set; }
    public DbSet<AreaResponsabilidad> AreasResponsabilidad { get; set; }
    public DbSet<Partido> Partidos { get; set; }

    // Configuración
    public DbSet<Configuracion> Configuraciones { get; set; }

    // Trazabilidad del ciclo de vida de notas y síntesis
    public DbSet<TrazaEventoEntity> TrazaEventos { get; set; }

    // Reglas de scraping por portal
    public DbSet<ScrapingRegla> ScrapingReglas { get; set; }

    // Catálogos
    public DbSet<CategoriaNoticia> CategoriasNoticia { get; set; }
    public DbSet<Localidad> Localidades { get; set; }
    public DbSet<Delegacion> Delegaciones { get; set; }
    public DbSet<DelegacionPrometheus> DelegacionesPrometheus { get; set; }
    public DbSet<PortalPrensa> PortalesPrensas { get; set; }

    // Quirón
    public DbSet<CaratulaQuiron> CaratulasQuiron { get; set; }
    public DbSet<ModalidadQuiron> ModalidadesQuiron { get; set; }

    // Dominio principal
    public DbSet<SesionPrensa> SesionesPrensas { get; set; }
    public DbSet<NotaPrensa> NotasPrensa { get; set; }
    public DbSet<NotaVersion> NotasVersion { get; set; }
    public DbSet<Sintesis> Sintesis { get; set; }
    public DbSet<SintesisNota> SintesisNotas { get; set; }
    public DbSet<SintesisDelegacionDestino> SintesisDelegacionDestinos { get; set; }
    public DbSet<NotaRelacion> NotasRelaciones { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // NotaPrensa.SesionPrensaId -> SesionPrensa (nullable: notas "libres" no tienen sesión)
        modelBuilder.Entity<NotaPrensa>()
            .HasOne(n => n.Sesion)
            .WithMany(s => s.Notas)
            .HasForeignKey(n => n.SesionPrensaId)
            .OnDelete(DeleteBehavior.SetNull);

        // NotaPrensa.DelegacionId -> Delegacion (nullable: notas con sesión lo derivan de la sesión)
        modelBuilder.Entity<NotaPrensa>()
            .HasOne(n => n.Delegacion)
            .WithMany()
            .HasForeignKey(n => n.DelegacionId)
            .OnDelete(DeleteBehavior.SetNull);

        // NotaPrensa.VersionActualId -> NotaVersion (circular con NotaVersion.NotaId)
        // Usar Restrict para no generar cascade en ciclo
        modelBuilder.Entity<NotaPrensa>()
            .HasOne(n => n.VersionActual)
            .WithMany()
            .HasForeignKey(n => n.VersionActualId)
            .OnDelete(DeleteBehavior.Restrict);

        // NotaPrensa.NotaOrigenId -> NotaPrensa (self-reference: copia en borrador → nota original)
        modelBuilder.Entity<NotaPrensa>()
            .HasOne(n => n.NotaOrigen)
            .WithMany()
            .HasForeignKey(n => n.NotaOrigenId)
            .OnDelete(DeleteBehavior.SetNull);

        // NotaVersion.NotaId -> NotaPrensa (lado "owned by", cascade delete OK)
        modelBuilder.Entity<NotaVersion>()
            .HasOne(v => v.Nota)
            .WithMany(n => n.Versiones)
            .HasForeignKey(v => v.NotaId)
            .OnDelete(DeleteBehavior.Cascade);

        // NotaVersion.ParentVersionId -> NotaVersion (self-reference)
        modelBuilder.Entity<NotaVersion>()
            .HasOne(v => v.ParentVersion)
            .WithMany()
            .HasForeignKey(v => v.ParentVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // SintesisNota.NotaVersionId -> NotaVersion (snapshot; Restrict para no perder historial)
        modelBuilder.Entity<SintesisNota>()
            .HasOne(sn => sn.NotaVersion)
            .WithMany()
            .HasForeignKey(sn => sn.NotaVersionId)
            .OnDelete(DeleteBehavior.Restrict);

        // SintesisDelegacionDestino: cascade con la síntesis, restrict con la delegación
        modelBuilder.Entity<SintesisDelegacionDestino>()
            .HasOne(d => d.Sintesis)
            .WithMany(s => s.DelegacionesDestino)
            .HasForeignKey(d => d.SintesisId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<SintesisDelegacionDestino>()
            .HasOne(d => d.Delegacion)
            .WithMany()
            .HasForeignKey(d => d.DelegacionId)
            .OnDelete(DeleteBehavior.Restrict);

        // NotaRelacion: bidireccional, ambas FKs con cascade
        modelBuilder.Entity<NotaRelacion>()
            .HasOne(r => r.Nota)
            .WithMany(n => n.Relaciones)
            .HasForeignKey(r => r.NotaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotaRelacion>()
            .HasOne(r => r.NotaRelacionada)
            .WithMany()
            .HasForeignKey(r => r.NotaRelacionadaId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<NotaRelacion>()
            .HasIndex(r => new { r.NotaId, r.NotaRelacionadaId })
            .IsUnique();

        // Partido.DelegacionId -> Delegacion (nullable: vínculo directo, reemplaza el matching por Prometheus)
        modelBuilder.Entity<Partido>()
            .HasOne(p => p.Delegacion)
            .WithMany(d => d.Partidos)
            .HasForeignKey(p => p.DelegacionId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
