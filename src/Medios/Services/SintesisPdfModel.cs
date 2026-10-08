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

    private static int OrdenAmbito(string ambito) => ambito switch
    {
        "Nacional" => 0,
        "Provincial" => 1,
        _ => 2
    };

    public static List<AmbitoPdf> Agrupar(IEnumerable<SintesisNota> incluidas)
    {
        return incluidas.OrderBy(sn => sn.Orden)
            .GroupBy(sn => new
            {
                Ambito = sn.NotaVersion.AmbitoNota is "Nacional" or "Provincial"
                    ? sn.NotaVersion.AmbitoNota! : "Partido",
                Partido = sn.NotaVersion.AmbitoNota is "Nacional" or "Provincial"
                    ? "" : (sn.NotaVersion.Partido?.Nombre ?? "Partido").Trim().ToUpperInvariant()
            })
            .OrderBy(g => OrdenAmbito(g.Key.Ambito))
            .ThenBy(g => g.Key.Partido, StringComparer.OrdinalIgnoreCase)
            .Select((grupo, ambitoIndex) =>
            {
                var id = $"amb-{ambitoIndex}";
                var categorias = grupo.GroupBy(sn => grupo.Key.Ambito == "Partido" ? Categoria(sn.NotaVersion) : "")
                    .OrderBy(g => OrdenCategoria(g.Key))
                    .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
                    .Select((categoria, index) => new CategoriaPdf($"{id}-cat-{index}",
                        string.IsNullOrEmpty(categoria.Key) ? null : categoria.Key,
                        categoria.Select(sn => sn.NotaVersion).ToList()))
                    .ToList();
                var titulo = grupo.Key.Ambito == "Partido" ? grupo.Key.Partido : grupo.Key.Ambito.ToUpperInvariant();
                return new AmbitoPdf(id, $"ÁMBITO {titulo}", categorias);
            }).ToList();
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

    public static string? LinkVisible(NotaVersion nota) => string.IsNullOrWhiteSpace(nota.VideoUrl) ? nota.Link : null;
}

public sealed record AmbitoPdf(string SectionId, string Titulo, List<CategoriaPdf> Categorias);
public sealed record CategoriaPdf(string SectionId, string? Titulo, List<NotaVersion> Notas);
