using System.Globalization;
using System.Text;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    // Captura (importa) notas y síntesis que una delegación cargó por Google Forms en su
    // Drive (planillas de respuestas), cuando el sistema principal estuvo fuera de línea.
    // Solo lectura de Drive → escritura en la DB del sistema. Service Account (clave JSON).
    public class CapturaDriveService
    {
        private readonly IMediosDbContextFactory _factory;
        private readonly ConfiguracionService _config;
        private readonly NotaService _notaService;
        private readonly SesionService _sesionService;
        private readonly DelegacionService _delegService;
        private readonly ImagenService _imagen;
        private readonly ILogger<CapturaDriveService> _logger;

        public const string ClaveSaJson = "drive_sa_json_path";

        public CapturaDriveService(IMediosDbContextFactory factory, ConfiguracionService config,
            NotaService notaService, SesionService sesionService, DelegacionService delegService,
            ImagenService imagen, ILogger<CapturaDriveService> logger)
        {
            _factory = factory;
            _config = config;
            _notaService = notaService;
            _sesionService = sesionService;
            _delegService = delegService;
            _imagen = imagen;
            _logger = logger;
        }

        // ── Configuración de la Service Account ───────────────────────
        public async Task<string?> GetSaJsonPathAsync() => await _config.GetAsync(ClaveSaJson);
        public async Task SetSaJsonPathAsync(string? path) =>
            await _config.SetAsync(ClaveSaJson, string.IsNullOrWhiteSpace(path) ? null : path.Trim());

        private async Task<SheetsService> CrearSheetsServiceAsync()
        {
            var path = await GetSaJsonPathAsync();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                throw new InvalidOperationException(
                    "No está configurada la clave de la cuenta de servicio de Google (archivo JSON).");

            var cred = GoogleCredential.FromFile(path)
                .CreateScoped(SheetsService.Scope.Spreadsheets);
            return new SheetsService(new BaseClientService.Initializer
            {
                HttpClientInitializer = cred,
                ApplicationName = "Medios - Sincronización"
            });
        }

        // ── DTOs ──────────────────────────────────────────────────────
        public class FilaCaptura
        {
            public int Fila { get; set; }                 // número de fila en la planilla (1-based)
            public Dictionary<string, string> Campos { get; set; } = new();
            public string Get(string k) => Campos.TryGetValue(k, out var v) ? v : "";
        }

        public class PreviewCaptura
        {
            public string? Error { get; set; }
            public string HojaTitulo { get; set; } = "";
            public int ColImportado { get; set; } = -1;
            public List<FilaCaptura> Filas { get; set; } = new();
        }

        // Lee las filas NO importadas de una planilla, mapeando encabezados → campos lógicos.
        private async Task<PreviewCaptura> LeerNuevasAsync(SheetsService svc, string spreadsheetId)
        {
            var meta = await svc.Spreadsheets.Get(spreadsheetId).ExecuteAsync();
            var titulo = meta.Sheets[0].Properties.Title;

            var resp = await svc.Spreadsheets.Values.Get(spreadsheetId, $"'{titulo}'!A:Z").ExecuteAsync();
            var filas = resp.Values;
            var prev = new PreviewCaptura { HojaTitulo = titulo };
            if (filas == null || filas.Count < 2) return prev; // sin datos (solo encabezado o vacío)

            // Encabezados → índice
            var encabezados = filas[0].Select(c => Normalizar(c?.ToString() ?? "")).ToList();
            int colImportado = encabezados.FindIndex(h => h.Contains("importado"));
            prev.ColImportado = colImportado >= 0 ? colImportado : encabezados.Count; // si no existe, va al final

            for (int i = 1; i < filas.Count; i++)
            {
                var fila = filas[i];
                // ¿Ya importada?
                if (colImportado >= 0 && colImportado < fila.Count
                    && !string.IsNullOrWhiteSpace(fila[colImportado]?.ToString()))
                    continue;

                var campos = new Dictionary<string, string>();
                for (int c = 0; c < encabezados.Count && c < fila.Count; c++)
                {
                    var clave = MapearClave(encabezados[c]);
                    if (clave != null && !campos.ContainsKey(clave))
                        campos[clave] = fila[c]?.ToString()?.Trim() ?? "";
                }
                // Saltar filas totalmente vacías
                if (campos.Values.All(string.IsNullOrWhiteSpace)) continue;
                prev.Filas.Add(new FilaCaptura { Fila = i + 1, Campos = campos });
            }
            return prev;
        }

        // ── Preview (sin importar) ────────────────────────────────────
        public async Task<(PreviewCaptura Notas, PreviewCaptura Sintesis)> PreviewAsync(int delegacionId)
        {
            var deleg = await _delegService.GetByIdAsync(delegacionId);
            if (deleg == null)
                return (new PreviewCaptura { Error = "Delegación no encontrada." },
                        new PreviewCaptura { Error = "Delegación no encontrada." });

            var svc = await CrearSheetsServiceAsync();
            var notas = string.IsNullOrWhiteSpace(deleg.DriveSheetNotas)
                ? new PreviewCaptura { Error = "La delegación no tiene configurada la planilla de Notas." }
                : await SeguroLeer(svc, deleg.DriveSheetNotas!);
            var sintesis = string.IsNullOrWhiteSpace(deleg.DriveSheetSintesis)
                ? new PreviewCaptura { Error = "La delegación no tiene configurada la planilla de Síntesis." }
                : await SeguroLeer(svc, deleg.DriveSheetSintesis!);
            return (notas, sintesis);
        }

        private async Task<PreviewCaptura> SeguroLeer(SheetsService svc, string sheetId)
        {
            try { return await LeerNuevasAsync(svc, sheetId); }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo leer la planilla {Sheet}", sheetId);
                return new PreviewCaptura { Error = $"No se pudo leer la planilla: {ex.Message}" };
            }
        }

        // ── Captura efectiva ──────────────────────────────────────────
        public class ResultadoCaptura
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
            public int NotasImportadas { get; set; }
            public int SesionesCreadas { get; set; }
            public int SintesisImportadas { get; set; }
            public List<string> Avisos { get; set; } = new();
        }

        public async Task<ResultadoCaptura> CapturarAsync(int delegacionId, string operador)
        {
            var deleg = await _delegService.GetByIdAsync(delegacionId);
            if (deleg == null) return new ResultadoCaptura { Ok = false, Error = "Delegación no encontrada." };

            SheetsService svc;
            try { svc = await CrearSheetsServiceAsync(); }
            catch (Exception ex) { return new ResultadoCaptura { Ok = false, Error = ex.Message }; }

            var res = new ResultadoCaptura { Ok = true };

            // ── NOTAS ─────────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(deleg.DriveSheetNotas))
            {
                var prev = await SeguroLeer(svc, deleg.DriveSheetNotas!);
                if (prev.Error != null) res.Avisos.Add($"Notas: {prev.Error}");
                else if (prev.Filas.Count > 0)
                {
                    var importadas = await ImportarNotasAsync(deleg, prev.Filas, operador, res);
                    if (importadas.Any())
                        await MarcarImportadasAsync(svc, deleg.DriveSheetNotas!, prev.HojaTitulo, prev.ColImportado, importadas);
                }
            }

            // ── SÍNTESIS ──────────────────────────────────────────────
            if (!string.IsNullOrWhiteSpace(deleg.DriveSheetSintesis))
            {
                var prev = await SeguroLeer(svc, deleg.DriveSheetSintesis!);
                if (prev.Error != null) res.Avisos.Add($"Síntesis: {prev.Error}");
                else if (prev.Filas.Count > 0)
                {
                    var importadas = await ImportarSintesisAsync(deleg, prev.Filas, operador, res);
                    if (importadas.Any())
                        await MarcarImportadasAsync(svc, deleg.DriveSheetSintesis!, prev.HojaTitulo, prev.ColImportado, importadas);
                }
            }

            return res;
        }

        private async Task<List<int>> ImportarNotasAsync(Delegacion deleg, List<FilaCaptura> filas,
            string operador, ResultadoCaptura res)
        {
            var categorias = await _notaService.GetCategoriasAsync();
            var partidos = await _notaService.GetPartidosAsync();
            var importadas = new List<int>();

            // Agrupar por Fecha + Turno → una sesión por grupo
            var grupos = filas.GroupBy(f => (Fecha: ParseFecha(f.Get("fecha")), Turno: NormalTurno(f.Get("turno"))));
            foreach (var g in grupos)
            {
                var fecha = g.Key.Fecha ?? DateOnly.FromDateTime(DateTime.Today);
                var turno = string.IsNullOrWhiteSpace(g.Key.Turno) ? "Vespertina" : g.Key.Turno;

                int sesionId = await CrearSesionRemitidaAsync(deleg.Id, fecha, turno, operador);
                res.SesionesCreadas++;

                foreach (var f in g)
                {
                    var ambito = NormalAmbito(f.Get("ambito"));
                    int categoriaId = ResolverCategoria(categorias, f.Get("categoria"));
                    int? partidoId = ResolverPartido(partidos, f.Get("partido"));
                    int? localidadId = partidoId.HasValue
                        ? await ResolverLocalidadAsync(partidoId.Value, f.Get("localidad")) : null;

                    var titulo = f.Get("titulo");
                    var texto = f.Get("texto");
                    if (string.IsNullOrWhiteSpace(titulo) || string.IsNullOrWhiteSpace(texto))
                    {
                        res.Avisos.Add($"Fila {f.Fila}: nota sin título o texto, se omitió.");
                        continue;
                    }

                    var imagenLocal = await _imagen.ProcesarDesdeUrlAsync(f.Get("imagen"));

                    await _notaService.AgregarAsync(sesionId, categoriaId, partidoId, localidadId,
                        titulo, texto, f.Get("fuente"), f.Get("link"),
                        esRepercusion: false, usuario: operador,
                        sintesis: string.IsNullOrWhiteSpace(f.Get("sintesis")) ? null : f.Get("sintesis"),
                        ambitoNota: ambito,
                        imagenUrl: imagenLocal,
                        videoUrl: string.IsNullOrWhiteSpace(f.Get("video")) ? null : f.Get("video"));
                    res.NotasImportadas++;
                    importadas.Add(f.Fila);
                }

                // Dejar la sesión consistente: generar el PDF y marcarla Remitida
                try { await _sesionService.GenerarPdfAsync(sesionId); } catch (Exception ex) { _logger.LogWarning(ex, "PDF sesión {Id}", sesionId); }
                await MarcarSesionRemitidaAsync(sesionId);
            }
            return importadas;
        }

        private async Task<List<int>> ImportarSintesisAsync(Delegacion deleg, List<FilaCaptura> filas,
            string operador, ResultadoCaptura res)
        {
            var importadas = new List<int>();
            using var db = _factory.Create();
            foreach (var f in filas)
            {
                var texto = f.Get("sintesis");
                if (string.IsNullOrWhiteSpace(texto)) texto = f.Get("texto");
                if (string.IsNullOrWhiteSpace(texto))
                {
                    res.Avisos.Add($"Síntesis fila {f.Fila}: sin texto, se omitió.");
                    continue;
                }
                var fecha = ParseFecha(f.Get("fecha")) ?? DateOnly.FromDateTime(DateTime.Today);
                var turno = NormalTurno(f.Get("turno"));
                if (string.IsNullOrWhiteSpace(turno)) turno = "Vespertina";

                var sintesis = new Sintesis
                {
                    Fecha = fecha,
                    Tipo = turno,
                    Estado = "Generada",
                    FechaCreacion = DateTime.Now,
                    FechaGeneracion = DateTime.Now,
                    OperadorGenera = operador,
                    DelegacionId = deleg.Id,
                    TextoImportado = texto.Trim()
                };
                db.Sintesis.Add(sintesis);
                await db.SaveChangesAsync();

                // Vincular las notas de esta delegación del mismo Fecha+Turno (las recién capturadas)
                var notasVer = await db.NotasPrensa
                    .Include(n => n.Sesion)
                    .Where(n => n.Sesion != null && n.Sesion.DelegacionId == deleg.Id
                             && n.Sesion.Fecha == fecha && n.Sesion.Turno == turno
                             && n.VersionActualId != null)
                    .ToListAsync();
                int orden = 0;
                foreach (var n in notasVer)
                    db.SintesisNotas.Add(new SintesisNota
                    {
                        SintesisId = sintesis.Id,
                        NotaPrensaId = n.Id,
                        NotaVersionId = n.VersionActualId!.Value,
                        Orden = orden++
                    });
                await db.SaveChangesAsync();

                res.SintesisImportadas++;
                importadas.Add(f.Fila);
            }
            return importadas;
        }

        // ── Helpers de persistencia ───────────────────────────────────
        private async Task<int> CrearSesionRemitidaAsync(int delegacionId, DateOnly fecha, string turno, string operador)
        {
            using var db = _factory.Create();
            var sesion = new SesionPrensa
            {
                DelegacionId = delegacionId,
                Fecha = fecha,
                Turno = turno,
                Estado = "Borrador",      // se marca Remitida luego de generar el PDF
                FechaCreacion = DateTime.Now,
                UsuarioCarga = $"Captura Drive ({operador})"
            };
            db.SesionesPrensas.Add(sesion);
            await db.SaveChangesAsync();
            return sesion.Id;
        }

        private async Task MarcarSesionRemitidaAsync(int sesionId)
        {
            using var db = _factory.Create();
            var s = await db.SesionesPrensas.FindAsync(sesionId);
            if (s == null) return;
            s.Estado = "Remitida";
            s.FechaRemision = DateTime.Now;
            await db.SaveChangesAsync();
        }

        private async Task<int?> ResolverLocalidadAsync(int partidoId, string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return null;
            var locs = await _notaService.GetLocalidadesByPartidoAsync(partidoId);
            var n = Normalizar(nombre);
            return locs.FirstOrDefault(l => Normalizar(l.Nombre) == n)?.Id;
        }

        // Escribe la fecha/hora de importación en la columna "Importado" de las filas dadas.
        private async Task MarcarImportadasAsync(SheetsService svc, string sheetId, string hoja,
            int colImportado, IEnumerable<int> filas)
        {
            try
            {
                var col = LetraColumna(colImportado);
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                var data = new List<Google.Apis.Sheets.v4.Data.ValueRange>();

                // Encabezado (si la columna es nueva)
                data.Add(new Google.Apis.Sheets.v4.Data.ValueRange
                {
                    Range = $"'{hoja}'!{col}1",
                    Values = new List<IList<object>> { new List<object> { "Importado" } }
                });
                foreach (var fila in filas)
                    data.Add(new Google.Apis.Sheets.v4.Data.ValueRange
                    {
                        Range = $"'{hoja}'!{col}{fila}",
                        Values = new List<IList<object>> { new List<object> { stamp } }
                    });

                var body = new Google.Apis.Sheets.v4.Data.BatchUpdateValuesRequest
                {
                    ValueInputOption = "RAW",
                    Data = data
                };
                await svc.Spreadsheets.Values.BatchUpdate(body, sheetId).ExecuteAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "No se pudo marcar filas importadas en {Sheet}", sheetId);
            }
        }

        // ── Helpers de parsing/normalización ──────────────────────────
        private static string? MapearClave(string encabezado)
        {
            var h = encabezado;
            if (h.Contains("importado")) return null;
            if (h.Contains("fecha")) return "fecha";
            if (h.Contains("turno")) return "turno";
            if (h.Contains("ambito")) return "ambito";
            if (h.Contains("categoria")) return "categoria";
            if (h.Contains("partido")) return "partido";
            if (h.Contains("localidad")) return "localidad";
            if (h.Contains("titulo")) return "titulo";
            if (h.Contains("sintesis")) return "sintesis";
            if (h.Contains("texto")) return "texto";
            if (h.Contains("fuente")) return "fuente";
            if (h.Contains("imagen")) return "imagen";
            if (h.Contains("video")) return "video";
            if (h.Contains("link") || h.Contains("enlace")) return "link";
            return null;
        }

        private static int ResolverCategoria(List<CategoriaNoticia> cats, string nombre)
        {
            if (!string.IsNullOrWhiteSpace(nombre))
            {
                var n = Normalizar(nombre);
                var match = cats.FirstOrDefault(c => Normalizar(c.Nombre) == n)
                         ?? cats.FirstOrDefault(c => Normalizar(c.Nombre).Contains(n) || n.Contains(Normalizar(c.Nombre)));
                if (match != null) return match.Id;
            }
            // Fallback: la categoría de menor orden disponible
            return cats.OrderBy(c => c.Orden).FirstOrDefault()?.Id ?? cats.FirstOrDefault()?.Id ?? 0;
        }

        private static int? ResolverPartido(List<Partido> partidos, string nombre)
        {
            if (string.IsNullOrWhiteSpace(nombre)) return null;
            var n = Normalizar(nombre);
            return partidos.FirstOrDefault(p => Normalizar(p.Nombre) == n)?.IdPartido
                ?? partidos.FirstOrDefault(p => Normalizar(p.Nombre).Contains(n))?.IdPartido;
        }

        private static string? NormalAmbito(string v)
        {
            var n = Normalizar(v);
            if (n.Contains("nacional")) return "Nacional";
            if (n.Contains("provincial")) return "Provincial";
            if (n.Contains("partido")) return "Partido";
            return string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        }

        private static string NormalTurno(string v)
        {
            var n = Normalizar(v);
            if (n.Contains("ampliacion") && n.Contains("matutina")) return "Ampliacion Matutina";
            if (n.Contains("ampliacion") && n.Contains("vespertina")) return "Ampliacion Vespertina";
            if (n.Contains("matutina")) return "Matutina";
            if (n.Contains("vespertina")) return "Vespertina";
            if (n.Contains("especial")) return "Especial";
            return v?.Trim() ?? "";
        }

        private static DateOnly? ParseFecha(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return null;
            v = v.Trim();
            string[] formatos = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy HH:mm:ss" };
            foreach (var fmt in formatos)
                if (DateTime.TryParseExact(v, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                    return DateOnly.FromDateTime(dt);
            if (DateTime.TryParse(v, new CultureInfo("es-AR"), DateTimeStyles.None, out var dt2))
                return DateOnly.FromDateTime(dt2);
            return null;
        }

        // Quita acentos y pasa a minúsculas para comparar encabezados/valores
        private static string Normalizar(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var norm = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (var c in norm)
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        // Índice 0-based → letra de columna (0→A, 25→Z, 26→AA)
        private static string LetraColumna(int index)
        {
            var s = "";
            index++;
            while (index > 0)
            {
                int r = (index - 1) % 26;
                s = (char)('A' + r) + s;
                index = (index - 1) / 26;
            }
            return s;
        }
    }
}
