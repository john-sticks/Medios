using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("notas_prensa_versiones")]
[Index("NotaId", Name = "idx_nv_nota")]
[Index("NotaId", "EsActual", Name = "idx_nv_nota_actual")]
[Index("EstadoRevision", Name = "idx_nv_estado")]
public class NotaVersion
{
    [Key]
    public int Id { get; set; }

    public int NotaId { get; set; }

    [ForeignKey("NotaId")]
    public virtual NotaPrensa Nota { get; set; } = null!;

    public int Version { get; set; }

    public int? ParentVersionId { get; set; }

    [ForeignKey("ParentVersionId")]
    public virtual NotaVersion? ParentVersion { get; set; }

    public int? CategoriaId { get; set; }

    [ForeignKey("CategoriaId")]
    public virtual CategoriaNoticia? Categoria { get; set; }

    // Nacional | Provincial | Partido (null = legado)
    [StringLength(20)]
    public string? AmbitoNota { get; set; }

    public int? PartidoId { get; set; }

    [ForeignKey("PartidoId")]
    public virtual Partido? Partido { get; set; }

    public int? LocalidadId { get; set; }

    [ForeignKey("LocalidadId")]
    public virtual Localidad? Localidad { get; set; }

    [StringLength(500)]
    public string Titulo { get; set; } = "";

    [Column(TypeName = "text")]
    public string Texto { get; set; } = "";

    [Column(TypeName = "text")]
    public string? Sintesis { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FechaNoticia { get; set; }

    [StringLength(200)]
    public string? Fuente { get; set; }

    // Otros medios donde repercutió la misma noticia (repercusión periodística), ej.
    // "LANACION,INFOBAE" — se combina con Fuente al mostrarla entre paréntesis.
    [StringLength(300)]
    public string? OtrosMedios { get; set; }

    [StringLength(2000)]
    public string? Link { get; set; }

    // Imagen representativa (ruta local reducida) y video (URL externa) de la nota
    [StringLength(500)]
    public string? ImagenUrl { get; set; }

    [StringLength(2000)]
    public string? VideoUrl { get; set; }

    // Imágenes adicionales (rutas locales reducidas) como JSON array. La representativa
    // sigue siendo ImagenUrl; estas son las que el usuario agrega manualmente.
    [Column(TypeName = "text")]
    public string? ImagenesUrls { get; set; }

    // Lista de imágenes adicionales deserializadas (no mapeada a columna)
    [NotMapped]
    public IReadOnlyList<string> ImagenesExtra =>
        string.IsNullOrWhiteSpace(ImagenesUrls)
            ? Array.Empty<string>()
            : (System.Text.Json.JsonSerializer.Deserialize<List<string>>(ImagenesUrls) ?? new List<string>());

    public bool EsRepercusion { get; set; }

    // Pendiente | Aprobada | Descartada
    [StringLength(20)]
    public string EstadoRevision { get; set; } = "Pendiente";

    [StringLength(500)]
    public string? MotivoDescarte { get; set; }

    [StringLength(100)]
    public string? OperadorRevision { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaVersion { get; set; } = DateTime.Now;

    [StringLength(100)]
    public string Usuario { get; set; } = "";

    public bool EsActual { get; set; } = true;

    // Creacion | Ampliacion | Aprobacion | Descarte | Modificacion
    [StringLength(30)]
    public string TipoEvento { get; set; } = "Creacion";

    // Breve descripción de qué se agregó/cambió (obligatorio en Ampliacion)
    [StringLength(500)]
    public string? ResumenCambio { get; set; }

    // Geolocalización del hecho
    [StringLength(500)]
    public string? Direccion { get; set; }

    public double? Latitud { get; set; }

    public double? Longitud { get; set; }

    // Carátula y modalidad Quirón (opcional)
    public int? CaratulaQuironId { get; set; }

    [ForeignKey("CaratulaQuironId")]
    public virtual CaratulaQuiron? CaratulaQuiron { get; set; }

    public int? ModalidadQuironId { get; set; }

    [ForeignKey("ModalidadQuironId")]
    public virtual ModalidadQuiron? ModalidadQuiron { get; set; }
}
