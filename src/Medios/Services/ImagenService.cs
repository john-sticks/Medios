using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace Medios.Services
{
    // Descarga una imagen desde una URL, la reduce y guarda UNA sola copia en el server.
    public class ImagenService
    {
        private readonly HttpClient _http;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<ImagenService> _logger;
        private const int MaxAncho = 900;          // px — se reduce manteniendo proporción
        private const int CalidadJpeg = 80;

        public ImagenService(HttpClient http, IWebHostEnvironment env, ILogger<ImagenService> logger)
        {
            _http = http;
            _env = env;
            _logger = logger;
            _http.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
            _http.Timeout = TimeSpan.FromSeconds(15);
        }

        // Si recibe una URL externa (http), la descarga, reduce y guarda local; devuelve la ruta
        // local (/uploads/notas/xxx.jpg). Si ya es local o está vacía, la devuelve sin cambios.
        public async Task<string?> ProcesarDesdeUrlAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url)) return null;
            url = url.Trim();
            // Ya es una imagen local del sistema → no reprocesar
            if (url.StartsWith("/uploads/")) return url;
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;

            try
            {
                var bytes = await _http.GetByteArrayAsync(url);
                using var img = Image.Load(bytes);

                if (img.Width > MaxAncho)
                {
                    var alto = (int)(img.Height * (MaxAncho / (double)img.Width));
                    img.Mutate(x => x.Resize(MaxAncho, alto));
                }

                var dir = Path.Combine(_env.WebRootPath, "uploads", "notas");
                Directory.CreateDirectory(dir);
                var nombre = $"{Guid.NewGuid():N}.jpg";
                var ruta = Path.Combine(dir, nombre);
                await img.SaveAsync(ruta, new JpegEncoder { Quality = CalidadJpeg });

                return $"/uploads/notas/{nombre}";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo procesar la imagen {Url}", url);
                return null; // si falla la descarga, la nota queda sin imagen
            }
        }

        // Procesa varias URLs (descarga/reduce cada una) y devuelve la lista de rutas locales,
        // sin duplicados y sin nulos. Pensado para las imágenes adicionales de una nota.
        public async Task<List<string>> ProcesarVariasAsync(IEnumerable<string>? urls)
        {
            var resultado = new List<string>();
            if (urls == null) return resultado;
            foreach (var u in urls)
            {
                if (string.IsNullOrWhiteSpace(u)) continue;
                var local = await ProcesarDesdeUrlAsync(u);
                if (!string.IsNullOrWhiteSpace(local) && !resultado.Contains(local))
                    resultado.Add(local);
            }
            return resultado;
        }

        // Igual que ProcesarVariasAsync pero devuelve el JSON array listo para guardar
        // (null si no hay ninguna imagen válida).
        public async Task<string?> ProcesarVariasJsonAsync(IEnumerable<string>? urls)
        {
            var lista = await ProcesarVariasAsync(urls);
            return lista.Count == 0 ? null : System.Text.Json.JsonSerializer.Serialize(lista);
        }
    }
}
