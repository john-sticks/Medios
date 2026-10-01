using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Medios.Entities;

// Regla de scraping específica por dominio de portal. Si no hay regla para un
// dominio, el scraper usa la heurística genérica.
[Table("scraping_reglas")]
[Index("Dominio", Name = "uq_dominio", IsUnique = true)]
public class ScrapingRegla
{
    [Key]
    public int Id { get; set; }

    // Dominio sin www (ej. "quedigital.com.ar")
    [StringLength(150)]
    public string Dominio { get; set; } = "";

    // XPath del título (opcional; si está vacío usa la heurística //h1)
    [StringLength(300)]
    public string? XPathTitulo { get; set; }

    // XPath del contenedor del cuerpo (opcional; ej. "//div[contains(@class,'entry-content')]")
    [StringLength(300)]
    public string? XPathCuerpo { get; set; }

    // Clases CSS a eliminar antes de extraer (separadas por coma): notas relacionadas, tags, etc.
    [StringLength(500)]
    public string? ClasesExcluir { get; set; }

    // Si está activo y el portal expone articleBody en JSON-LD, se usa esa fuente (más limpia).
    public bool PreferirJsonLd { get; set; } = true;

    public bool Activo { get; set; } = true;

    [StringLength(200)]
    public string? Descripcion { get; set; }

    [Column(TypeName = "datetime")]
    public DateTime? FechaActualizacion { get; set; }
}
