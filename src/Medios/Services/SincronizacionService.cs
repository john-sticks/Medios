using System.Text.Json;
using System.Text.Encodings.Web;
using Medios.Entities;

namespace Medios.Services
{
    // Genera en disco la estructura de carpetas + JSON de una síntesis consolidada
    // y sus notas, lista para subir a Google Drive (Phase C). Aún sin conexión a Drive.
    //
    // Estructura:
    //   {root}/INTELIGENCIA/AAAA/MM/DD/
    //       SINTESIS/{id}_{tipo}/  datos.json · sintesis.pdf · documentos/
    //       NOTAS/{notaId}/        datos.json · imagenes/ · videos/ · documentos/
    public class SincronizacionService
    {
        private readonly SintesisService _sintesis;
        private readonly ConfiguracionService _config;
        private readonly IWebHostEnvironment _env;
        private readonly ILogger<SincronizacionService> _logger;

        public const string ClaveRoot = "sync_root_path";

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public SincronizacionService(SintesisService sintesis, ConfiguracionService config,
            IWebHostEnvironment env, ILogger<SincronizacionService> logger)
        {
            _sintesis = sintesis;
            _config = config;
            _env = env;
            _logger = logger;
        }

        public static string RootPorDefecto =>
            OperatingSystem.IsWindows() ? @"C:\MediosSync" : "/var/medios/sync";

        public async Task<string> GetRootAsync()
        {
            var v = await _config.GetAsync(ClaveRoot);
            return string.IsNullOrWhiteSpace(v) ? RootPorDefecto : v.Trim();
        }

        public async Task SetRootAsync(string? path)
        {
            await _config.SetAsync(ClaveRoot, string.IsNullOrWhiteSpace(path) ? null : path.Trim());
        }

        public class ResultadoSync
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
            public string? Carpeta { get; set; }
            public int NotasGeneradas { get; set; }
            public int ImagenesCopiadas { get; set; }
        }

        // Genera la estructura de una síntesis consolidada. La síntesis debe ser consolidada
        // (DelegacionId null) — el caso de uso principal descrito por el usuario.
        public async Task<ResultadoSync> GenerarSintesisAsync(int sintesisId)
        {
            var s = await _sintesis.GetByIdAsync(sintesisId);
            if (s == null)
                return new ResultadoSync { Ok = false, Error = "No se encontró la síntesis." };
            if (s.DelegacionId != null)
                return new ResultadoSync { Ok = false, Error = "Solo se sincronizan síntesis consolidadas." };

            string root;
            try
            {
                root = await GetRootAsync();
                var baseDia = Path.Combine(root, "INTELIGENCIA",
                    s.Fecha.Year.ToString("D4"),
                    s.Fecha.Month.ToString("D2"),
                    s.Fecha.Day.ToString("D2"));

                // ── SINTESIS/{id}_{tipo} ──────────────────────────────
                var tipoSafe = Limpiar(s.Tipo);
                var dirSintesis = Path.Combine(baseDia, "SINTESIS", $"{s.Id}_{tipoSafe}");
                Directory.CreateDirectory(Path.Combine(dirSintesis, "documentos"));

                var notas = s.NotasIncluidas
                    .OrderBy(sn => sn.Orden)
                    .Select(sn => sn.NotaVersion)
                    .ToList();

                var sintesisJson = new
                {
                    id = s.Id,
                    tipo = s.Tipo,
                    fecha = s.Fecha.ToString("yyyy-MM-dd"),
                    estado = s.Estado,
                    fechaInformada = s.FechaInformada?.ToString("yyyy-MM-ddTHH:mm:ss"),
                    canalTodosRoles = s.CanalTodosRoles,
                    canalMails = s.CanalMails,
                    operadorGenera = s.OperadorGenera,
                    cantidadNotas = notas.Count,
                    notas = notas.Select(v => new { notaId = v.NotaId, versionId = v.Id, titulo = v.Titulo }).ToList()
                };
                await File.WriteAllTextAsync(
                    Path.Combine(dirSintesis, "datos.json"),
                    JsonSerializer.Serialize(sintesisJson, JsonOpts));

                // Copiar el PDF de la síntesis si existe
                if (!string.IsNullOrEmpty(s.PDFPath))
                {
                    var pdfFisico = Path.Combine(_env.WebRootPath, s.PDFPath.TrimStart('/'));
                    if (File.Exists(pdfFisico))
                        File.Copy(pdfFisico, Path.Combine(dirSintesis, "sintesis.pdf"), overwrite: true);
                }

                // ── NOTAS/{notaId} ────────────────────────────────────
                int imagenes = 0;
                foreach (var v in notas)
                {
                    var dirNota = Path.Combine(baseDia, "NOTAS", v.NotaId.ToString());
                    var dirImg = Path.Combine(dirNota, "imagenes");
                    Directory.CreateDirectory(dirImg);
                    Directory.CreateDirectory(Path.Combine(dirNota, "videos"));
                    Directory.CreateDirectory(Path.Combine(dirNota, "documentos"));

                    // Imagen principal + adicionales → carpeta imagenes/
                    string? imagenArchivo = null;
                    var imagenesArchivos = new List<string>();
                    var todas = new List<string>();
                    if (!string.IsNullOrEmpty(v.ImagenUrl)) todas.Add(v.ImagenUrl);
                    todas.AddRange(v.ImagenesExtra);

                    foreach (var rel in todas)
                    {
                        if (string.IsNullOrEmpty(rel) || !rel.StartsWith("/")) continue;
                        var imgFisico = Path.Combine(_env.WebRootPath, rel.TrimStart('/'));
                        if (!File.Exists(imgFisico)) continue;
                        var nombre = Path.GetFileName(imgFisico);
                        File.Copy(imgFisico, Path.Combine(dirImg, nombre), overwrite: true);
                        imagenesArchivos.Add(nombre);
                        imagenArchivo ??= nombre;     // la primera es la principal
                        imagenes++;
                    }

                    var notaJson = new
                    {
                        notaId = v.NotaId,
                        versionId = v.Id,
                        version = v.Version,
                        ambito = v.AmbitoNota,
                        categoria = v.Categoria?.Nombre,
                        partido = v.Partido?.Nombre,
                        localidad = v.Localidad?.Nombre,
                        titulo = v.Titulo,
                        texto = v.Texto,
                        sintesis = v.Sintesis,
                        fuente = v.Fuente,
                        link = v.Link,
                        esRepercusion = v.EsRepercusion,
                        fechaNoticia = v.FechaNoticia?.ToString("yyyy-MM-ddTHH:mm:ss"),
                        direccion = v.Direccion,
                        latitud = v.Latitud,
                        longitud = v.Longitud,
                        imagen = imagenArchivo,        // archivo principal dentro de imagenes/ (o null)
                        imagenes = imagenesArchivos,   // todos los archivos en imagenes/
                        videoUrl = v.VideoUrl          // enlace externo (no se descarga)
                    };
                    await File.WriteAllTextAsync(
                        Path.Combine(dirNota, "datos.json"),
                        JsonSerializer.Serialize(notaJson, JsonOpts));
                }

                return new ResultadoSync
                {
                    Ok = true,
                    Carpeta = dirSintesis,
                    NotasGeneradas = notas.Count,
                    ImagenesCopiadas = imagenes
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error al generar estructura de sincronización para síntesis {Id}", sintesisId);
                return new ResultadoSync { Ok = false, Error = $"Error al escribir en disco: {ex.Message}" };
            }
        }

        // Quita caracteres no aptos para nombre de carpeta
        private static string Limpiar(string s)
        {
            var invalidos = Path.GetInvalidFileNameChars();
            var limpio = new string(s.Where(c => !invalidos.Contains(c)).ToArray());
            return limpio.Replace(' ', '_');
        }
    }
}
