using QuestPDF.Drawing;

namespace Medios.Services;

public static class SintesisFuentes
{
    public static string Bookman { get; private set; } =
        OperatingSystem.IsWindows() ? "Bookman Old Style" : "URW Bookman";

    // Fuentes licenciadas instaladas por el operador; no se publican en wwwroot ni en Git.
    public static void Registrar(string contentRoot)
    {
        var directorio = Path.Combine(contentRoot, "fonts");
        if (!Directory.Exists(directorio)) return;
        foreach (var archivo in Directory.EnumerateFiles(directorio)
                     .Where(p => Path.GetExtension(p).Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                              || Path.GetExtension(p).Equals(".otf", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = File.OpenRead(archivo);
            FontManager.RegisterFont(stream);
        }
        if (File.Exists(Path.Combine(directorio, "BOOKOS.TTF"))
            && File.Exists(Path.Combine(directorio, "BOOKOSB.TTF")))
            Bookman = "Bookman Old Style";
    }
}
