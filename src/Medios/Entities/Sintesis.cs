using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

[Table("sintesis")]
[Index("Fecha", "Tipo", Name = "idx_sintesis_fecha_tipo")]
[Index("Estado", Name = "idx_sintesis_estado")]
public class Sintesis
{
    [Key]
    public int Id { get; set; }

    // Matutina | Vespertina | Ampliacion Matutina | Ampliacion Vespertina | Especial | Zonal
    [StringLength(30)]
    public string Tipo { get; set; } = "Vespertina";

    [Column(TypeName = "date")]
    public DateOnly Fecha { get; set; }

    // Borrador | Generada | Distribuida
    [StringLength(20)]
    public string Estado { get; set; } = "Borrador";

    [StringLength(500)]
    public string? PDFPath { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime FechaCreacion { get; set; } = DateTime.Now;

    [Column(TypeName = "datetime")]
    public DateTime? FechaGeneracion { get; set; }

    [StringLength(100)]
    public string OperadorGenera { get; set; } = "";

    // Canales de información final (síntesis consolidada)
    public bool CanalTodosRoles { get; set; }

    // Visibilidad in-app para todas las delegaciones (independiente del envío por mail)
    public bool CanalTodasDelegaciones { get; set; }

    [StringLength(1000)]
    public string? CanalMails { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FechaInformada { get; set; }

    public int? DelegacionId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion? Delegacion { get; set; }

    // Texto de la síntesis capturado por contingencia (Google Form → Drive). Null en
    // las síntesis generadas normalmente desde las notas del sistema.
    [Column(TypeName = "text")]
    public string? TextoImportado { get; set; }

    public virtual ICollection<SintesisNota> NotasIncluidas { get; set; } = new List<SintesisNota>();

    // Delegaciones específicas elegidas para visibilidad in-app (vacío si CanalTodasDelegaciones)
    public virtual ICollection<SintesisDelegacionDestino> DelegacionesDestino { get; set; } = new List<SintesisDelegacionDestino>();
}

// Delegaciones a las que se informa una síntesis consolidada (visibilidad in-app, no email)
[Table("sintesis_delegacion_destino")]
[Index("SintesisId", "DelegacionId", Name = "idx_sdd_unico", IsUnique = true)]
public class SintesisDelegacionDestino
{
    [Key]
    public int Id { get; set; }

    public int SintesisId { get; set; }

    [ForeignKey("SintesisId")]
    public virtual Sintesis Sintesis { get; set; } = null!;

    public int DelegacionId { get; set; }

    [ForeignKey("DelegacionId")]
    public virtual Delegacion Delegacion { get; set; } = null!;
}

[Table("sintesis_notas")]
[Index("SintesisId", Name = "idx_sn_sintesis")]
[Index("NotaPrensaId", Name = "idx_sn_nota")]
[Index("NotaVersionId", Name = "idx_sn_version")]
public class SintesisNota
{
    [Key]
    public int Id { get; set; }

    public int SintesisId { get; set; }

    [ForeignKey("SintesisId")]
    public virtual Sintesis Sintesis { get; set; } = null!;

    public int NotaPrensaId { get; set; }

    [ForeignKey("NotaPrensaId")]
    public virtual NotaPrensa Nota { get; set; } = null!;

    // Snapshot de la versión al momento de armar la síntesis
    public int NotaVersionId { get; set; }

    [ForeignKey("NotaVersionId")]
    public virtual NotaVersion NotaVersion { get; set; } = null!;

    // Orden dentro de la síntesis (para reordenamiento manual)
    public int Orden { get; set; }
}
