using System.Globalization;
using System.Text;
using Medios.Entities;

namespace Medios.Services;

public static class CategoriaNotaPolicy
{
    public static string Ambito(string? nombreCategoria)
    {
        var nombre = new StringBuilder();
        foreach (var c in (nombreCategoria ?? "").Normalize(NormalizationForm.FormD))
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark
                && char.IsLetter(c)) nombre.Append(char.ToUpperInvariant(c));
        return nombre.ToString() switch
        {
            "AMBITONACIONAL" => "Nacional",
            "AMBITOPROVINCIAL" => "Provincial",
            _ => "Partido"
        };
    }

    // Las versiones históricas conservan el ámbito elegido con el formulario anterior.
    public static string AmbitoDeNota(NotaVersion nota) =>
        nota.AmbitoNota is "Nacional" or "Provincial" or "Partido"
            ? nota.AmbitoNota : Ambito(nota.Categoria?.Nombre);
}

public sealed record SeleccionCategoriaNota(string? Ambito, string? Error);
