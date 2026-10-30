using System.Globalization;
using System.Text;
using Medios.Entities;

namespace Medios.Services;

public static class SintesisPdfModel
{
    public const float TamanoTitulo = 14;
    public static readonly IReadOnlyList<string> OrdenCategorias = new[]
    {
        "INSTITUCIONALES", "VISITA DE FUNCIONARIOS", "HECHOS DELICTUALES",
        "PROCEDIMIENTOS POLICIALES", "PROCEDIMIENTOS DE OTRAS FUERZAS", "VIOLENCIA DE GÉNERO",
        "DELITOS CONTRA LA INTEGRIDAD SEXUAL", "BÚSQUEDA/HALLAZGO DE PERSONAS",
        "HALLAZGO DE CADÁVER/RESTOS ÓSEOS", "AVERIGUACIÓN CAUSALES DE MUERTE", "DENUNCIAS",
        "MOVILIZACIONES – PROTESTAS", "SINIESTROS - CATÁSTROFES", "INTIMIDACIÓN PÚBLICA",
        "RECLAMOS DE SEGURIDAD", "ÁMBITO RURAL", "ÁMBITO JUDICIAL", "ÁMBITO PENITENCIARIO",
        "ÁMBITO DEPORTIVO", "ÁMBITO ESCOLAR", "FUGAS – MOTINES Y OTROS",
        "DECLARACIONES DE SEGURIDAD", "ACCIDENTE VIAL", "NOTAS DE OPINIÓN",
        "NOTAS DE INTERÉS", "REPERCUSIONES PERIODÍSTICAS"
    };

    private static string Normalizar(string texto)
    {
        var normalizado = new StringBuilder();
        foreach (var caracter in texto.Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(caracter) != UnicodeCategory.NonSpacingMark
                && char.IsLetterOrDigit(caracter))
                normalizado.Append(char.ToUpperInvariant(caracter));
        return normalizado.ToString();
    }

    private static int OrdenCategoria(string titulo)
    {
        var clave = Normalizar(titulo);
        if (clave.Length == 0) return -1;
        for (var i = 0; i < OrdenCategorias.Count; i++)
            if (Normalizar(OrdenCategorias[i]) == clave) return i;
        return OrdenCategorias.Count;
    }

    private static string Categoria(NotaVersion nota)
    {
        if (nota.EsRepercusion) return "REPERCUSIONES PERIODÍSTICAS";
        var nombre = nota.Categoria?.Nombre ?? "Sin categoría";
        // El ámbito ya tiene su encabezado con el partido elegido.
        if (Normalizar(nombre) == "AMBITOPARTIDO") return "";
        var orden = OrdenCategoria(nombre);
        return orden < OrdenCategorias.Count ? OrdenCategorias[orden] : nombre.ToUpperInvariant();
    }

    private static (int Orden, string Titulo, bool SoloLocalidad) Grupo(NotaVersion nota)
    {
        var ambito = CategoriaNotaPolicy.AmbitoDeNota(nota);
        if (ambito == "Nacional") return (0, "ÁMBITO NACIONAL", false);
        if (ambito == "Provincial") return (1, "ÁMBITO PROVINCIAL", false);
        var categoria = Categoria(nota);
        if (categoria.Length == 0 || nota.Categoria == null)
            return (2, "ÁMBITO " + (nota.Partido?.Nombre ?? "Partido").Trim().ToUpperInvariant(), true);
        return (3 + OrdenCategoria(categoria), categoria, false);
    }

    public static List<AmbitoPdf> Agrupar(IEnumerable<SintesisNota> incluidas) =>
        incluidas.OrderBy(sn => sn.Orden)
            .GroupBy(sn => Grupo(sn.NotaVersion))
            .OrderBy(g => g.Key.Orden)
            .ThenBy(g => g.Key.Titulo, StringComparer.OrdinalIgnoreCase)
            .Select((grupo, index) => new AmbitoPdf($"grupo-{index}", grupo.Key.Titulo,
                new List<CategoriaPdf> { new($"grupo-{index}-notas", null,
                    grupo.Select(sn => sn.NotaVersion).ToList()) }, grupo.Key.SoloLocalidad))
            .ToList();

    public static string LocalidadDentroPartido(NotaVersion nota)
    {
        var localidad = nota.Localidad?.Nombre ?? "";
        var partido = nota.Partido?.Nombre ?? "";
        var sufijo = $"({partido})";
        if (localidad.EndsWith(sufijo, StringComparison.OrdinalIgnoreCase))
            localidad = localidad[..^sufijo.Length].Trim();
        return string.Equals(localidad, partido, StringComparison.OrdinalIgnoreCase)
            ? "" : localidad.ToUpperInvariant();
    }

    public static string CuerpoConFuente(NotaVersion nota)
    {
        var cuerpo = string.IsNullOrWhiteSpace(nota.Sintesis) ? nota.Texto : nota.Sintesis;
        var medios = (nota.Fuente + "," + nota.OtrosMedios)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(m => m.ToUpperInvariant().TrimEnd('.')).Where(m => m.Length > 0);
        var fuente = string.Join(", ", medios);
        return cuerpo.TrimEnd() + (fuente.Length > 0 ? $" ({fuente})" : "");
    }

    public static int LongitudPartidoEnTitulo(NotaVersion nota)
    {
        var titulo = nota.Titulo.Trim().TrimEnd('.');
        var dosPuntos = titulo.IndexOf(':');
        var partido = nota.Partido?.Nombre.Trim();
        if (dosPuntos <= 0 || string.IsNullOrEmpty(partido)) return 0;
        var prefijo = titulo[..dosPuntos].TrimEnd();
        var comparacion = System.Globalization.CultureInfo.GetCultureInfo("es-AR").CompareInfo;
        return comparacion.Compare(prefijo, partido,
            System.Globalization.CompareOptions.IgnoreCase | System.Globalization.CompareOptions.IgnoreNonSpace) == 0
            ? prefijo.Length : 0;
    }

    public static string? LinkVisible(NotaVersion nota) => string.IsNullOrWhiteSpace(nota.Link) ? null : nota.Link.Trim();
}

public sealed record AmbitoPdf(string SectionId, string Titulo, List<CategoriaPdf> Categorias, bool SoloLocalidad = false);
public sealed record CategoriaPdf(string SectionId, string? Titulo, List<NotaVersion> Notas);
