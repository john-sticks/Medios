using Microsoft.EntityFrameworkCore;
using Medios.Entities;
using Medios.Infrastructure.Data;

namespace Medios.Services
{
    public class NotaService
    {
        private readonly IMediosDbContextFactory _factory;

        public NotaService(IMediosDbContextFactory factory)
        {
            _factory = factory;
        }

        public async Task<List<CaratulaQuiron>> GetCaratulasAsync()
        {
            using var db = _factory.Create();
            return await db.CaratulasQuiron.OrderBy(c => c.Nombre).ToListAsync();
        }

        public async Task<List<CaratulaQuiron>> GetCaratulasConModalidadesAsync()
        {
            using var db = _factory.Create();
            return await db.CaratulasQuiron
                .Include(c => c.Modalidades)
                .OrderBy(c => c.Nombre)
                .ToListAsync();
        }

        public async Task<List<ModalidadQuiron>> GetModalidadesByCaratulaAsync(int caratulaId)
        {
            using var db = _factory.Create();
            return await db.ModalidadesQuiron
                .Where(m => m.CaratulaId == caratulaId)
                .OrderBy(m => m.Nombre)
                .ToListAsync();
        }

        public async Task<List<CategoriaNoticia>> GetCategoriasAsync()
        {
            using var db = _factory.Create();
            return await db.CategoriasNoticia
                .Where(c => c.Activa)
                .OrderBy(c => c.Orden)
                .ToListAsync();
        }

        public async Task<List<Partido>> GetPartidosAsync()
        {
            using var db = _factory.Create();
            return await db.Partidos
                .OrderBy(p => p.Nombre)
                .ToListAsync();
        }

        public async Task<List<Partido>> GetPartidosByDelegacionAsync(int delegacionId)
        {
            using var db = _factory.Create();
            return await db.Partidos
                .Where(p => p.DelegacionId == delegacionId)
                .OrderBy(p => p.Nombre)
                .ToListAsync();
        }

        public async Task<List<Localidad>> GetLocalidadesByPartidoAsync(int partidoId)
        {
            using var db = _factory.Create();
            return await db.Localidades
                .Where(l => l.PartidoId == partidoId)
                .OrderBy(l => l.Nombre)
                .ToListAsync();
        }

        // ── Crear nueva versión dentro de un contexto ya abierto ──────

        private async Task<NotaVersion> CrearVersionAsync(
            MediosDbContext db,
            NotaPrensa nota,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string estadoRevision,
            string? motivoDescarte, string? operadorRevision,
            string usuario, string tipoEvento, string? resumenCambio = null,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            // Marcar versión anterior como no actual
            if (nota.VersionActualId.HasValue)
            {
                var anterior = await db.NotasVersion.FindAsync(nota.VersionActualId.Value);
                if (anterior != null) anterior.EsActual = false;
            }

            var maxVersion = await db.NotasVersion
                .Where(v => v.NotaId == nota.Id)
                .MaxAsync(v => (int?)v.Version) ?? 0;

            var nueva = new NotaVersion
            {
                Nota = nota,        // navigation property — EF resuelve NotaId automáticamente
                Version = maxVersion + 1,
                ParentVersionId = nota.VersionActualId,
                CategoriaId = categoriaId,
                AmbitoNota = ambitoNota?.Trim(),
                PartidoId = partidoId,
                LocalidadId = localidadId,
                Titulo = titulo.Trim(),
                Texto = texto.Trim(),
                Sintesis = sintesis?.Trim(),
                FechaNoticia = fechaNoticia,
                Fuente = fuente?.Trim(),
                OtrosMedios = otrosMedios?.Trim(),
                Link = link?.Trim(),
                ImagenUrl = string.IsNullOrWhiteSpace(imagenUrl) ? null : imagenUrl.Trim(),
                VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim(),
                ImagenesUrls = string.IsNullOrWhiteSpace(imagenesExtraJson) ? null : imagenesExtraJson,
                EsRepercusion = esRepercusion,
                EstadoRevision = estadoRevision,
                MotivoDescarte = motivoDescarte,
                OperadorRevision = operadorRevision,
                FechaVersion = DateTime.Now,
                Usuario = usuario,
                EsActual = true,
                TipoEvento = tipoEvento,
                ResumenCambio = resumenCambio,
                Direccion = direccion?.Trim(),
                Latitud = latitud,
                Longitud = longitud,
                CaratulaQuironId = caratulaQuironId,
                ModalidadQuironId = modalidadQuironId
            };
            // Asignar via navigation property: EF Core inserta la versión,
            // obtiene su Id y actualiza VersionActualId en el mismo SaveChanges
            nota.VersionActual = nueva;
            db.NotasVersion.Add(nueva);
            await db.SaveChangesAsync();

            return nueva;
        }

        // ── Agregar nota a sesión (delegación) ────────────────────────

        public async Task<int> AgregarAsync(
            int sesionId, int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string usuario,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();

            var nota = new NotaPrensa
            {
                SesionPrensaId = sesionId,
                FechaRegistro = DateTime.Now,
                VersionActualId = null
            };
            db.NotasPrensa.Add(nota);
            // Save intermedio para romper el ciclo FK NotaPrensa↔NotaVersion en MySQL/Pomelo
            await db.SaveChangesAsync();
            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: "Borrador",
                motivoDescarte: null, operadorRevision: null,
                usuario: usuario, tipoEvento: "Creacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return nota.Id;
        }

        public async Task<int> AgregarDesdeVersionAsync(int sesionId, int notaOrigenId, string usuario)
        {
            using var db = _factory.Create();

            var origen = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaOrigenId);

            if (origen?.VersionActual == null) return 0;

            var v = origen.VersionActual;

            var nota = new NotaPrensa
            {
                SesionPrensaId = sesionId,
                FechaRegistro = DateTime.Now,
                VersionActualId = null,
                NotaOrigenId = notaOrigenId
            };
            db.NotasPrensa.Add(nota);
            await db.SaveChangesAsync();
            await CrearVersionAsync(db, nota,
                v.CategoriaId, v.PartidoId, v.LocalidadId,
                v.Titulo, v.Texto, v.Fuente, v.Link, v.EsRepercusion,
                estadoRevision: "Borrador",
                motivoDescarte: null, operadorRevision: null,
                usuario: usuario, tipoEvento: "Ampliar",
                resumenCambio: $"Ampliación remitida (nota origen #{notaOrigenId})",
                direccion: v.Direccion, latitud: v.Latitud, longitud: v.Longitud,
                caratulaQuironId: v.CaratulaQuironId, modalidadQuironId: v.ModalidadQuironId,
                ambitoNota: v.AmbitoNota, imagenUrl: v.ImagenUrl, videoUrl: v.VideoUrl,
                imagenesExtraJson: v.ImagenesUrls, otrosMedios: v.OtrosMedios);

            return nota.Id;
        }

        public async Task<bool> EliminarAsync(int notaId, string usuarioCarga)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.Sesion)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota == null) return false;
            if (nota.Sesion == null || nota.Sesion.Estado != "Borrador") return false;
            if (nota.Sesion.UsuarioCarga != usuarioCarga) return false;

            // Quitar las referencias con FK RESTRICT que bloquean el borrado:
            //  - sintesis_notas (NotaPrensaId / NotaVersionId)
            //  - self-FK ParentVersionId entre las versiones de la nota
            // (no se cargan las versiones para que EF no intente borrarlas en un orden
            //  que viole el self-FK; se eliminan por CASCADE de NotaId al borrar la nota).
            await db.SintesisNotas.Where(sn => sn.NotaPrensaId == notaId).ExecuteDeleteAsync();
            await db.NotasVersion.Where(v => v.NotaId == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ParentVersionId, (int?)null));
            // EF Core no genera UPDATE para entidades en estado Deleted; nullar VersionActualId
            // directamente en DB para romper el RESTRICT antes de que CASCADE elimine las versiones.
            await db.NotasPrensa.Where(n => n.Id == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.VersionActualId, (int?)null));
            await db.NotasPrensa.Where(n => n.Id == notaId).ExecuteDeleteAsync();
            return true;
        }

        // ── Aprobar (sin modificar contenido) ────────────────────────

        public async Task<bool> AprobarAsync(int notaId, string operador)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null) return false;

            // Actualizar en-place sin crear nueva versión
            nota.VersionActual.EstadoRevision = "Aprobada";
            nota.VersionActual.OperadorRevision = operador;
            nota.VersionActual.TipoEvento = "Aprobacion";
            await db.SaveChangesAsync();

            return true;
        }

        // ── Modificar contenido y aprobar ─────────────────────────────

        public async Task<bool> ModificarYAprobarAsync(
            int notaId, string operador,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link, bool esRepercusion,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null) return false;

            // Modificar crea una nueva versión que CONSERVA el estado de revisión anterior
            // (no retrocede el flujo). La aprobación es una acción aparte del operador.
            var estadoPrevio = nota.VersionActual.EstadoRevision;
            var operadorPrevio = nota.VersionActual.OperadorRevision;

            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: estadoPrevio,
                motivoDescarte: null, operadorRevision: operadorPrevio,
                usuario: operador, tipoEvento: "Modificacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return true;
        }

        // ── Editar nota en borrador (delegación, sin cambiar estado) ──

        public async Task<bool> EditarBorradorAsync(
            int notaId, string usuario,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link, bool esRepercusion,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null,
            int? userDelegacionId = null, string? usuarioLogin = null,
            DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.Sesion)
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota == null) return false;
            if (nota.Sesion == null || nota.Sesion.Estado != "Borrador") return false;

            // Acepta nombre de display O login username (sesiones antiguas pueden tener cualquiera)
            bool esCreador = string.Equals(nota.Sesion.UsuarioCarga, usuario, StringComparison.OrdinalIgnoreCase)
                          || (usuarioLogin != null && string.Equals(nota.Sesion.UsuarioCarga, usuarioLogin, StringComparison.OrdinalIgnoreCase));
            bool mismaDelegacion = userDelegacionId.HasValue
                && nota.Sesion.DelegacionId == userDelegacionId;
            if (!esCreador && !mismaDelegacion) return false;

            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: "Borrador",
                motivoDescarte: null, operadorRevision: null,
                usuario: usuario, tipoEvento: "Modificacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return true;
        }

        // ── Descartar ─────────────────────────────────────────────────

        public async Task<bool> DescartarAsync(int notaId, string operador, string? motivo)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null) return false;

            // Una nota descartada se desvincula de las síntesis en curso (no Informadas):
            // no debe pertenecer a ninguna síntesis, y así no hay conflicto si luego la
            // delegación la elimina o la re-trata.
            var sn = await db.SintesisNotas
                .Include(x => x.Sintesis)
                .Where(x => x.NotaPrensaId == notaId && x.Sintesis.Estado != "Informada")
                .ToListAsync();
            db.SintesisNotas.RemoveRange(sn);

            // Actualizar en-place sin crear nueva versión
            nota.VersionActual.EstadoRevision = "Descartada";
            nota.VersionActual.MotivoDescarte = motivo;
            nota.VersionActual.OperadorRevision = operador;
            nota.VersionActual.TipoEvento = "Descarte";
            await db.SaveChangesAsync();

            return true;
        }

        // ── Ampliar: agregar información sin cambiar estado ───────────

        public async Task<bool> AmpliarAsync(
            int notaId, string usuario,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string resumenCambio,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null) return false;

            // Una nueva versión (ampliación) conserva el estado de la versión anterior:
            // agregar información no debe retroceder el flujo de revisión.
            var estadoPrevio = nota.VersionActual.EstadoRevision;
            var operadorPrevio = nota.VersionActual.OperadorRevision;

            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: estadoPrevio,
                motivoDescarte: null,
                operadorRevision: operadorPrevio,
                usuario: usuario, tipoEvento: "Ampliacion",
                resumenCambio: resumenCambio,
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return true;
        }

        // Cuando el OPERADOR o superior modifica/amplía una nota que ya está en una síntesis,
        // la síntesis debe tomar como referencia la ÚLTIMA versión (no la congelada). Actualiza
        // sintesis_notas → VersionActual en las síntesis aún en curso (no en las ya Informadas).
        public async Task ActualizarReferenciaSintesisAsync(int notaId)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa.FindAsync(notaId);
            if (nota?.VersionActualId == null) return;

            var refs = await db.SintesisNotas
                .Include(sn => sn.Sintesis)
                .Where(sn => sn.NotaPrensaId == notaId && sn.Sintesis.Estado != "Informada")
                .ToListAsync();
            if (refs.Count == 0) return;

            foreach (var sn in refs)
                sn.NotaVersionId = nota.VersionActualId.Value;
            await db.SaveChangesAsync();
        }

        // ── Editar versión "Sin Remitir" en-place (no crea nueva versión) ─

        public async Task<bool> EditarVersionSinRemitirAsync(
            int notaId, string usuario,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string resumenCambio,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null) return false;
            if (nota.VersionActual.EstadoRevision is not ("Sin Remitir" or "Borrador")) return false;

            var v = nota.VersionActual;
            v.CategoriaId = categoriaId;
            v.AmbitoNota = ambitoNota?.Trim();
            v.PartidoId = partidoId;
            v.LocalidadId = localidadId;
            v.Titulo = titulo.Trim();
            v.Texto = texto.Trim();
            v.Sintesis = sintesis?.Trim();
            v.Fuente = fuente?.Trim();
            v.Link = link?.Trim();
            v.ImagenUrl = string.IsNullOrWhiteSpace(imagenUrl) ? null : imagenUrl.Trim();
            v.VideoUrl = string.IsNullOrWhiteSpace(videoUrl) ? null : videoUrl.Trim();
            v.ImagenesUrls = string.IsNullOrWhiteSpace(imagenesExtraJson) ? null : imagenesExtraJson;
            v.EsRepercusion = esRepercusion;
            v.ResumenCambio = resumenCambio.Trim();
            v.Direccion = direccion?.Trim();
            v.Latitud = latitud;
            v.Longitud = longitud;
            v.FechaNoticia = fechaNoticia;
            v.CaratulaQuironId = caratulaQuironId;
            v.ModalidadQuironId = modalidadQuironId;
            v.FechaVersion = DateTime.Now;
            v.Usuario = usuario;

            await db.SaveChangesAsync();
            return true;
        }

        // ── Buscador de notas ─────────────────────────────────────────

        public async Task<List<NotaBuscadorItem>> BuscarAsync(
            string? texto,
            int? categoriaId,
            int? partidoId,
            string? estadoRevision,
            DateOnly? fechaDesde,
            DateOnly? fechaHasta,
            int? delegacionId,
            string? estadoSesion,
            double? geoLat = null,
            double? geoLng = null,
            double? geoRadioKm = null,
            bool usarFechaNoticia = false)
        {
            using var db = _factory.Create();

            bool usaGeo = geoLat.HasValue && geoLng.HasValue && geoRadioKm.HasValue && geoRadioKm.Value > 0;
            double latMin = 0, latMax = 0, lngMin = 0, lngMax = 0;
            if (usaGeo)
            {
                double radio = geoRadioKm!.Value;
                double latDelta = radio / 111.0;
                double lngDelta = radio / (111.0 * Math.Cos(geoLat!.Value * Math.PI / 180.0));
                latMin = geoLat.Value - latDelta; latMax = geoLat.Value + latDelta;
                lngMin = geoLng!.Value - lngDelta; lngMax = geoLng.Value + lngDelta;
            }

            // ── Query 1: notas con sesión ─────────────────────────────
            var qSesion = db.NotasPrensa
                .Include(n => n.VersionActual).ThenInclude(v => v!.Categoria)
                .Include(n => n.VersionActual).ThenInclude(v => v!.Partido)
                .Include(n => n.Sesion).ThenInclude(s => s!.Delegacion)
                .Where(n => n.VersionActualId != null && n.SesionPrensaId != null)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(texto))
            {
                var t = texto.Trim().ToLower();
                qSesion = qSesion.Where(n =>
                    n.VersionActual!.Titulo.ToLower().Contains(t) ||
                    n.VersionActual.Texto.ToLower().Contains(t) ||
                    (n.VersionActual.Fuente != null && n.VersionActual.Fuente.ToLower().Contains(t)));
            }
            if (categoriaId.HasValue)
                qSesion = qSesion.Where(n => n.VersionActual!.CategoriaId == categoriaId.Value);
            if (partidoId.HasValue)
                qSesion = qSesion.Where(n => n.VersionActual!.PartidoId == partidoId.Value);
            if (!string.IsNullOrEmpty(estadoRevision))
                qSesion = qSesion.Where(n => n.VersionActual!.EstadoRevision == estadoRevision);
            if (usarFechaNoticia)
            {
                // Usa FechaNoticia si existe, sino FechaRegistro como fallback
                var desdeDate = fechaDesde?.ToDateTime(TimeOnly.MinValue);
                var hastaDate = fechaHasta?.ToDateTime(TimeOnly.MaxValue);
                if (desdeDate.HasValue)
                    qSesion = qSesion.Where(n =>
                        (n.VersionActual!.FechaNoticia ?? n.FechaRegistro) >= desdeDate.Value);
                if (hastaDate.HasValue)
                    qSesion = qSesion.Where(n =>
                        (n.VersionActual!.FechaNoticia ?? n.FechaRegistro) <= hastaDate.Value);
            }
            else
            {
                if (fechaDesde.HasValue)
                    qSesion = qSesion.Where(n => n.Sesion!.Fecha >= fechaDesde.Value);
                if (fechaHasta.HasValue)
                    qSesion = qSesion.Where(n => n.Sesion!.Fecha <= fechaHasta.Value);
            }
            if (delegacionId.HasValue)
                qSesion = qSesion.Where(n => n.Sesion!.DelegacionId == delegacionId.Value);
            if (!string.IsNullOrEmpty(estadoSesion))
                qSesion = qSesion.Where(n => n.Sesion!.Estado == estadoSesion);
            if (usaGeo)
                qSesion = qSesion.Where(n =>
                    n.VersionActual!.Latitud != null && n.VersionActual.Longitud != null &&
                    n.VersionActual.Latitud >= latMin && n.VersionActual.Latitud <= latMax &&
                    n.VersionActual.Longitud >= lngMin && n.VersionActual.Longitud <= lngMax);

            var notasSesion = await qSesion
                .OrderByDescending(n => n.Sesion!.Fecha)
                .ThenByDescending(n => n.FechaRegistro)
                .ToListAsync();

            // ── Query 2: notas libres (sin sesión) ────────────────────
            // Solo cuando no hay filtros exclusivos de sesión
            bool hayFiltroSesion = (!usarFechaNoticia && (fechaDesde.HasValue || fechaHasta.HasValue)) || !string.IsNullOrEmpty(estadoSesion);
            List<Entities.NotaPrensa> notasLibres = [];
            if (!hayFiltroSesion)
            {
                var qLibres = db.NotasPrensa
                    .Include(n => n.VersionActual).ThenInclude(v => v!.Categoria)
                    .Include(n => n.VersionActual).ThenInclude(v => v!.Partido)
                    .Include(n => n.Delegacion)
                    .Where(n => n.VersionActualId != null && n.SesionPrensaId == null)
                    .AsQueryable();

                if (!string.IsNullOrWhiteSpace(texto))
                {
                    var t = texto.Trim().ToLower();
                    qLibres = qLibres.Where(n =>
                        n.VersionActual!.Titulo.ToLower().Contains(t) ||
                        n.VersionActual.Texto.ToLower().Contains(t) ||
                        (n.VersionActual.Fuente != null && n.VersionActual.Fuente.ToLower().Contains(t)));
                }
                if (categoriaId.HasValue)
                    qLibres = qLibres.Where(n => n.VersionActual!.CategoriaId == categoriaId.Value);
                if (partidoId.HasValue)
                    qLibres = qLibres.Where(n => n.VersionActual!.PartidoId == partidoId.Value);
                if (!string.IsNullOrEmpty(estadoRevision))
                    qLibres = qLibres.Where(n => n.VersionActual!.EstadoRevision == estadoRevision);
                if (delegacionId.HasValue)
                    qLibres = qLibres.Where(n => n.DelegacionId == delegacionId.Value);
                if (usarFechaNoticia)
                {
                    var desdeDate2 = fechaDesde?.ToDateTime(TimeOnly.MinValue);
                    var hastaDate2 = fechaHasta?.ToDateTime(TimeOnly.MaxValue);
                    if (desdeDate2.HasValue)
                        qLibres = qLibres.Where(n =>
                            (n.VersionActual!.FechaNoticia ?? n.FechaRegistro) >= desdeDate2.Value);
                    if (hastaDate2.HasValue)
                        qLibres = qLibres.Where(n =>
                            (n.VersionActual!.FechaNoticia ?? n.FechaRegistro) <= hastaDate2.Value);
                }
                if (usaGeo)
                    qLibres = qLibres.Where(n =>
                        n.VersionActual!.Latitud != null && n.VersionActual.Longitud != null &&
                        n.VersionActual.Latitud >= latMin && n.VersionActual.Latitud <= latMax &&
                        n.VersionActual.Longitud >= lngMin && n.VersionActual.Longitud <= lngMax);

                notasLibres = await qLibres
                    .OrderByDescending(n => n.FechaRegistro)
                    .ToListAsync();
            }

            // ── Proyección y merge ────────────────────────────────────
            IEnumerable<NotaBuscadorItem> items = notasSesion.Select(n => new NotaBuscadorItem
            {
                NotaId = n.Id,
                VersionActualId = n.VersionActualId!.Value,
                Titulo = n.VersionActual!.Titulo,
                CategoriaNombre = n.VersionActual.Categoria?.Nombre ?? "",
                AmbitoNota = n.VersionActual.AmbitoNota,
                PartidoNombre = n.VersionActual.Partido?.Nombre,
                EstadoRevision = n.VersionActual.EstadoRevision,
                TipoEvento = n.VersionActual.TipoEvento,
                SesionId = n.SesionPrensaId!.Value,
                SesionFecha = n.Sesion!.Fecha,
                SesionTurno = n.Sesion.Turno,
                SesionEstado = n.Sesion.Estado,
                DelegacionNombre = n.Sesion.Delegacion?.Nombre,
                FechaVersion = n.VersionActual.FechaVersion,
                FechaNoticia = n.VersionActual.FechaNoticia,
                FechaRegistro = n.FechaRegistro,
                Latitud = n.VersionActual.Latitud,
                Longitud = n.VersionActual.Longitud,
                EsLibre = false,
                DistanciaKm = usaGeo && n.VersionActual.Latitud.HasValue
                    ? HaversineKm(geoLat!.Value, geoLng!.Value, n.VersionActual.Latitud.Value, n.VersionActual.Longitud!.Value)
                    : null
            }).Concat(notasLibres.Select(n => new NotaBuscadorItem
            {
                NotaId = n.Id,
                VersionActualId = n.VersionActualId!.Value,
                Titulo = n.VersionActual!.Titulo,
                CategoriaNombre = n.VersionActual.Categoria?.Nombre ?? "",
                PartidoNombre = n.VersionActual.Partido?.Nombre,
                EstadoRevision = n.VersionActual.EstadoRevision,
                TipoEvento = n.VersionActual.TipoEvento,
                SesionId = 0,
                SesionFecha = DateOnly.FromDateTime(n.FechaRegistro),
                SesionTurno = "",
                SesionEstado = "Sin sesión",
                DelegacionNombre = n.Delegacion?.Nombre,
                FechaVersion = n.VersionActual.FechaVersion,
                FechaNoticia = n.VersionActual.FechaNoticia,
                FechaRegistro = n.FechaRegistro,
                Latitud = n.VersionActual.Latitud,
                Longitud = n.VersionActual.Longitud,
                EsLibre = true,
                DistanciaKm = usaGeo && n.VersionActual.Latitud.HasValue
                    ? HaversineKm(geoLat!.Value, geoLng!.Value, n.VersionActual.Latitud.Value, n.VersionActual.Longitud!.Value)
                    : null
            }));

            // Enriquecer con estado de síntesis (una nota puede estar en varias síntesis:
            // la de su delegación y la consolidada → tomar la más reciente por SintesisId)
            var notaIds = items.Select(i => i.NotaId).Distinct().ToList();
            var sintesisEstados = await db.SintesisNotas
                .Where(sn => notaIds.Contains(sn.NotaPrensaId))
                .Select(sn => new { sn.NotaPrensaId, sn.SintesisId, sn.Sintesis.Estado })
                .ToListAsync();
            var sintesisMap = sintesisEstados
                .GroupBy(x => x.NotaPrensaId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.SintesisId).First().Estado);

            items = items.Select(i =>
            {
                if (sintesisMap.TryGetValue(i.NotaId, out var se))
                    i.SintesisEstado = se;
                return i;
            });

            if (usaGeo)
                items = items
                    .Where(i => i.DistanciaKm.HasValue && i.DistanciaKm.Value <= geoRadioKm!.Value)
                    .OrderBy(i => i.DistanciaKm);

            return items.ToList();
        }

        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            var dLat = (lat2 - lat1) * Math.PI / 180.0;
            var dLon = (lon2 - lon1) * Math.PI / 180.0;
            var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                  + Math.Cos(lat1 * Math.PI / 180.0) * Math.Cos(lat2 * Math.PI / 180.0)
                  * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        // ── Traza / ciclo de vida de una nota ─────────────────────────
        // Devuelve todos los eventos (versiones de la nota + síntesis donde participó)
        // ordenados cronológicamente, para el módulo Consulta → Traza.
        public async Task<NotaTraza?> GetTrazaAsync(int notaId)
        {
            using var db = _factory.Create();

            var nota = await db.NotasPrensa
                .Include(n => n.Delegacion)
                .Include(n => n.Sesion).ThenInclude(s => s!.Delegacion)
                .Include(n => n.Versiones)
                .FirstOrDefaultAsync(n => n.Id == notaId);
            if (nota == null) return null;

            var traza = new NotaTraza
            {
                NotaId = nota.Id,
                Titulo = nota.VersionActual?.Titulo ?? nota.Versiones.OrderByDescending(v => v.Version).FirstOrDefault()?.Titulo ?? $"Nota #{nota.Id}",
                Delegacion = nota.Sesion?.Delegacion?.Nombre ?? nota.Delegacion?.Nombre,
                FechaRegistro = nota.FechaRegistro,
                NotaOrigenId = nota.NotaOrigenId
            };

            // Eventos de versión de la nota
            foreach (var v in nota.Versiones.OrderBy(v => v.Version))
            {
                traza.Eventos.Add(new TrazaEvento
                {
                    Fecha = v.FechaVersion,
                    Tipo = "Nota",
                    TipoEvento = v.TipoEvento,
                    Estado = v.EstadoRevision,
                    Version = v.Version,
                    Usuario = v.Usuario,
                    Detalle = v.ResumenCambio,
                    MotivoDescarte = v.MotivoDescarte
                });
            }

            // Eventos de síntesis donde participó (snapshot de versión incluido)
            var enSintesis = await db.SintesisNotas
                .Include(sn => sn.Sintesis).ThenInclude(s => s.Delegacion)
                .Where(sn => sn.NotaPrensaId == notaId)
                .ToListAsync();
            foreach (var sn in enSintesis)
            {
                var s = sn.Sintesis;
                var consolidada = s.DelegacionId == null;
                traza.Eventos.Add(new TrazaEvento
                {
                    Fecha = s.FechaInformada ?? s.FechaGeneracion ?? s.FechaCreacion,
                    Tipo = "Sintesis",
                    TipoEvento = consolidada ? "Consolidada" : "Síntesis Delegación",
                    Estado = s.Estado,
                    Usuario = s.OperadorGenera,
                    Detalle = $"{s.Tipo} — {s.Fecha:dd/MM/yyyy}" +
                              (s.Delegacion != null ? $" · {s.Delegacion.Nombre}" : "")
                });
            }

            traza.Eventos = traza.Eventos.OrderBy(e => e.Fecha).ThenBy(e => e.Version ?? 0).ToList();
            return traza;
        }

        // Busca notas por título/id para el selector del módulo Traza
        public async Task<List<NotaBuscadorItem>> BuscarParaTrazaAsync(string? q)
        {
            using var db = _factory.Create();
            var query = db.NotasPrensa
                .Include(n => n.VersionActual).ThenInclude(v => v!.Categoria)
                .Include(n => n.Sesion).ThenInclude(s => s!.Delegacion)
                .Include(n => n.Delegacion)
                .Where(n => n.VersionActualId != null)
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var t = q.Trim().ToLower();
                if (int.TryParse(t, out var idBuscado))
                    query = query.Where(n => n.Id == idBuscado || n.VersionActual!.Titulo.ToLower().Contains(t));
                else
                    query = query.Where(n => n.VersionActual!.Titulo.ToLower().Contains(t)
                                          || (n.VersionActual.Fuente != null && n.VersionActual.Fuente.ToLower().Contains(t)));
            }

            var notas = await query
                .OrderByDescending(n => n.FechaRegistro)
                .Take(50)
                .ToListAsync();

            return notas.Select(n => new NotaBuscadorItem
            {
                NotaId = n.Id,
                Titulo = n.VersionActual!.Titulo,
                CategoriaNombre = n.VersionActual.Categoria?.Nombre ?? "",
                AmbitoNota = n.VersionActual.AmbitoNota,
                EstadoRevision = n.VersionActual.EstadoRevision,
                DelegacionNombre = n.Sesion?.Delegacion?.Nombre ?? n.Delegacion?.Nombre,
                FechaRegistro = n.FechaRegistro
            }).ToList();
        }

        // ── Línea de tiempo de una noticia (linaje + actualizaciones) ──
        //
        // Para cada nota mostrada en una síntesis (versionPorNota: notaId → versión congelada),
        // resuelve el LINAJE completo (cadena NotaOrigenId hacia atrás + versiones por nota) y
        // devuelve:
        //   - Ancla: la versión ORIGINAL (v1, texto base de la noticia)
        //   - Actualizaciones: las versiones posteriores con ResumenCambio, hasta la fecha de la
        //     versión congelada (corte temporal: una síntesis no muestra ampliaciones futuras)
        public async Task<Dictionary<int, NotaLineaTiempo>> GetLineasTiempoAsync(
            Dictionary<int, NotaVersion> versionPorNota)
        {
            var result = new Dictionary<int, NotaLineaTiempo>();
            if (versionPorNota.Count == 0) return result;

            using var db = _factory.Create();

            foreach (var (notaId, vShown) in versionPorNota)
            {
                // 1. Linaje: subir por NotaOrigenId hasta la raíz
                var lineage = new List<int>();
                int? cur = notaId;
                int guard = 0;
                while (cur.HasValue && guard++ < 20 && !lineage.Contains(cur.Value))
                {
                    lineage.Add(cur.Value);
                    cur = await db.NotasPrensa
                        .Where(n => n.Id == cur.Value)
                        .Select(n => n.NotaOrigenId)
                        .FirstOrDefaultAsync();
                }

                // 2. Versiones del linaje hasta el corte temporal (la versión congelada en esta síntesis)
                var corte = vShown.FechaVersion;
                var versiones = await db.NotasVersion
                    .Include(v => v.Categoria)
                    .Include(v => v.Partido)
                    .Include(v => v.Localidad)
                    .Where(v => lineage.Contains(v.NotaId) && v.FechaVersion <= corte)
                    .OrderBy(v => v.FechaVersion)
                    .ThenBy(v => v.Version)
                    .ToListAsync();

                if (versiones.Count == 0) continue;

                var ancla = versiones.First();
                var updates = versiones.Skip(1)
                    .Where(v => !string.IsNullOrWhiteSpace(v.ResumenCambio)
                             && v.EstadoRevision != "Descartada")
                    .ToList();

                // 3. Turno/fecha de la síntesis donde se publicó cada actualización (para la etiqueta)
                var verIds = updates.Select(u => u.Id).ToList();
                var turnoByVer = new Dictionary<int, Sintesis>();
                if (verIds.Count > 0)
                {
                    var snaps = await db.SintesisNotas
                        .Include(sn => sn.Sintesis)
                        .Where(sn => verIds.Contains(sn.NotaVersionId) && sn.Sintesis.Estado != "Borrador")
                        .ToListAsync();
                    turnoByVer = snaps
                        .GroupBy(sn => sn.NotaVersionId)
                        .ToDictionary(g => g.Key, g => g.OrderBy(s => s.SintesisId).First().Sintesis);
                }

                var items = updates.Select(u => new NotaTimelineItem
                {
                    Fecha = turnoByVer.TryGetValue(u.Id, out var s)
                        ? s.Fecha.ToDateTime(TimeOnly.MinValue) : u.FechaVersion,
                    Turno = turnoByVer.TryGetValue(u.Id, out var s2) ? s2.Tipo : null,
                    Usuario = u.Usuario,
                    ResumenCambio = u.ResumenCambio,
                    TipoEvento = u.TipoEvento,
                    EstadoRevision = u.EstadoRevision
                }).ToList();

                result[notaId] = new NotaLineaTiempo { Ancla = ancla, Actualizaciones = items };
            }

            return result;
        }

        public async Task<NotaPrensa?> GetDetalleAsync(int id)
        {
            using var db = _factory.Create();
            return await db.NotasPrensa
                .Include(n => n.Delegacion)
                .Include(n => n.Sesion)
                    .ThenInclude(s => s!.Delegacion)
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Categoria)
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Partido)
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Localidad)
                .Include(n => n.Versiones.OrderBy(v => v.Version))
                    .ThenInclude(v => v.Categoria)
                .Include(n => n.Versiones.OrderBy(v => v.Version))
                    .ThenInclude(v => v.Partido)
                .Include(n => n.Versiones.OrderBy(v => v.Version))
                    .ThenInclude(v => v.Localidad)
                .Include(n => n.Relaciones)
                    .ThenInclude(r => r.NotaRelacionada)
                        .ThenInclude(nr => nr.VersionActual)
                            .ThenInclude(v => v!.Categoria)
                .Include(n => n.Relaciones)
                    .ThenInclude(r => r.NotaRelacionada)
                        .ThenInclude(nr => nr.Sesion)
                            .ThenInclude(s => s!.Delegacion)
                .FirstOrDefaultAsync(n => n.Id == id);
        }

        public async Task<Dictionary<int, Sintesis>> GetSintesisPorVersionAsync(int notaId)
        {
            using var db = _factory.Create();
            var versionIds = await db.NotasVersion
                .Where(v => v.NotaId == notaId)
                .Select(v => v.Id)
                .ToListAsync();

            var snList = await db.SintesisNotas
                .Include(sn => sn.Sintesis)
                .Where(sn => versionIds.Contains(sn.NotaVersionId))
                .ToListAsync();

            return snList
                .GroupBy(sn => sn.NotaVersionId)
                .ToDictionary(g => g.Key, g => g.First().Sintesis);
        }

        public async Task<bool> AgregarRelacionAsync(int notaId, int relacionadaId, string usuario)
        {
            if (notaId == relacionadaId) return false;

            using var db = _factory.Create();

            var existe = await db.NotasRelaciones
                .AnyAsync(r => r.NotaId == notaId && r.NotaRelacionadaId == relacionadaId);
            if (existe) return true;

            var ambasExisten = await db.NotasPrensa.CountAsync(n => n.Id == notaId || n.Id == relacionadaId) == 2;
            if (!ambasExisten) return false;

            db.NotasRelaciones.Add(new NotaRelacion { NotaId = notaId, NotaRelacionadaId = relacionadaId, Usuario = usuario });
            db.NotasRelaciones.Add(new NotaRelacion { NotaId = relacionadaId, NotaRelacionadaId = notaId, Usuario = usuario });
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<bool> QuitarRelacionAsync(int notaId, int relacionadaId)
        {
            using var db = _factory.Create();
            var filas = await db.NotasRelaciones
                .Where(r => (r.NotaId == notaId && r.NotaRelacionadaId == relacionadaId) ||
                            (r.NotaId == relacionadaId && r.NotaRelacionadaId == notaId))
                .ToListAsync();

            if (filas.Count == 0) return false;
            db.NotasRelaciones.RemoveRange(filas);
            await db.SaveChangesAsync();
            return true;
        }

        public async Task<List<NotaBuscadorItem>> BuscarParaRelacionarAsync(string q, int excluirNotaId)
        {
            using var db = _factory.Create();
            var termino = q.Trim().ToLower();

            var yaRelacionadas = await db.NotasRelaciones
                .Where(r => r.NotaId == excluirNotaId)
                .Select(r => r.NotaRelacionadaId)
                .ToListAsync();

            var notas = await db.NotasPrensa
                .Include(n => n.VersionActual)
                    .ThenInclude(v => v!.Categoria)
                .Include(n => n.Sesion)
                    .ThenInclude(s => s!.Delegacion)
                .Where(n => n.Id != excluirNotaId
                    && !yaRelacionadas.Contains(n.Id)
                    && n.VersionActualId != null
                    && n.SesionPrensaId != null
                    && n.VersionActual!.Titulo.ToLower().Contains(termino))
                .OrderByDescending(n => n.Sesion!.Fecha)
                .Take(10)
                .ToListAsync();

            return notas.Select(n => new NotaBuscadorItem
            {
                NotaId = n.Id,
                VersionActualId = n.VersionActualId!.Value,
                Titulo = n.VersionActual!.Titulo,
                CategoriaNombre = n.VersionActual.Categoria?.Nombre ?? "",
                AmbitoNota = n.VersionActual.AmbitoNota,
                SesionFecha = n.Sesion!.Fecha,
                DelegacionNombre = n.Sesion.Delegacion?.Nombre
            }).ToList();
        }

        // ── Notas libres (sin sesión) ──────────────────────────────────

        public async Task<List<NotaPrensa>> GetNotasLibresAsync(string usuario)
        {
            using var db = _factory.Create();
            return await db.NotasPrensa
                .Include(n => n.VersionActual!)
                    .ThenInclude(v => v.Categoria)
                .Include(n => n.VersionActual!)
                    .ThenInclude(v => v.Partido)
                .Where(n => n.SesionPrensaId == null && n.VersionActual!.Usuario == usuario)
                .OrderByDescending(n => n.FechaRegistro)
                .ToListAsync();
        }

        // Notas descartadas por el Analista que NO tienen copia en ningún borrador/sesión activa
        public async Task<List<NotaPrensa>> GetDescartadasSinVincularAsync(int delegacionId)
        {
            using var db = _factory.Create();
            var descartadas = await db.NotasPrensa
                .Include(n => n.VersionActual!)
                    .ThenInclude(v => v.Categoria)
                .Include(n => n.VersionActual!)
                    .ThenInclude(v => v.Partido)
                .Include(n => n.Sesion)
                .Where(n => n.SesionPrensaId != null
                         && n.Sesion!.DelegacionId == delegacionId
                         && n.VersionActual!.EstadoRevision == "Sin Remitir"
                         && n.VersionActual.MotivoDescarte != null)
                .OrderByDescending(n => n.FechaRegistro)
                .ToListAsync();

            if (!descartadas.Any()) return descartadas;

            // Excluir las que ya tienen copia en cualquier sesión (borrador o remitida)
            var ids = descartadas.Select(n => n.Id).ToList();
            var conCopia = await db.NotasPrensa
                .Where(n => n.NotaOrigenId.HasValue && ids.Contains(n.NotaOrigenId!.Value))
                .Select(n => n.NotaOrigenId!.Value)
                .Distinct()
                .ToListAsync();

            return descartadas.Where(n => !conCopia.Contains(n.Id)).ToList();
        }

        public async Task<int> AgregarLibreAsync(
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string usuario,
            string? direccion = null, double? latitud = null, double? longitud = null,
            int? delegacionId = null, string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null, bool aprobadaDirectamente = false)
        {
            using var db = _factory.Create();

            var nota = new NotaPrensa
            {
                SesionPrensaId = null,
                DelegacionId = delegacionId,
                FechaRegistro = DateTime.Now,
                VersionActualId = null
            };
            db.NotasPrensa.Add(nota);
            await db.SaveChangesAsync();
            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: aprobadaDirectamente ? "Aprobada" : "Sin Remitir",
                motivoDescarte: null, operadorRevision: aprobadaDirectamente ? usuario : null,
                usuario: usuario, tipoEvento: "Creacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return nota.Id;
        }

        public async Task EliminarNotaConSintesisAsync(int notaId)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa.FirstOrDefaultAsync(n => n.Id == notaId);
            if (nota == null) return;

            await db.SintesisNotas.Where(sn => sn.NotaPrensaId == notaId).ExecuteDeleteAsync();
            await db.NotasVersion.Where(v => v.NotaId == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ParentVersionId, (int?)null));
            await db.NotasPrensa.Where(n => n.Id == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.VersionActualId, (int?)null));
            await db.NotasPrensa.Where(n => n.Id == notaId).ExecuteDeleteAsync();
        }

        public async Task<bool> EliminarLibreAsync(int notaId, string usuario)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .FirstOrDefaultAsync(n => n.Id == notaId && n.SesionPrensaId == null);
            if (nota == null) return false;

            // Autorización: solo el creador (Usuario de la versión actual). Proyección para
            // NO trackear la versión (evita que EF intente borrarla violando el self-FK).
            var usuarioCreador = await db.NotasVersion
                .Where(v => v.Id == nota.VersionActualId)
                .Select(v => v.Usuario).FirstOrDefaultAsync();
            if (usuarioCreador != usuario) return false;

            await db.SintesisNotas.Where(sn => sn.NotaPrensaId == notaId).ExecuteDeleteAsync();
            await db.NotasVersion.Where(v => v.NotaId == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.ParentVersionId, (int?)null));
            await db.NotasPrensa.Where(n => n.Id == notaId)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.VersionActualId, (int?)null));
            await db.NotasPrensa.Where(n => n.Id == notaId).ExecuteDeleteAsync();
            return true;
        }

        public async Task<bool> EditarLibreAsync(
            int notaId, string usuario,
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link, bool esRepercusion,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? sintesis = null, int? userDelegacionId = null,
            DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId && n.SesionPrensaId == null);

            if (nota?.VersionActual == null) return false;

            // Permitir si es el creador o si la nota es de la misma delegación
            bool esCreador = nota.VersionActual.Usuario == usuario;
            bool mismaDelegacion = userDelegacionId.HasValue
                && nota.DelegacionId == userDelegacionId;
            if (!esCreador && !mismaDelegacion) return false;

            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: nota.VersionActual.EstadoRevision == "Aprobada" ? "Aprobada" : "Sin Remitir",
                motivoDescarte: null, operadorRevision: nota.VersionActual.OperadorRevision,
                usuario: usuario, tipoEvento: "Modificacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return true;
        }

        // ── Carga directa de División Medios (sin sesión de delegación) ──

        public async Task<int> AgregarDivisionAsync(
            int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string operador, DateOnly fecha, string turno,
            int? delegacionId = null,
            string? sintesis = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();

            // Nace en Borrador (no Remitida): así MEDIOS puede seguir agregando notas y recién
            // "Publicar" cuando quiera, igual que una Delegación arma su propio borrador. Una vez
            // publicada (ConvertirASintesisAsync la pasa a Remitida) no se reutiliza más — una
            // nota nueva del mismo turno arranca un borrador distinto.
            var sesion = await db.SesionesPrensas
                .FirstOrDefaultAsync(s => s.DelegacionId == delegacionId
                    && s.Fecha == fecha
                    && s.Turno == turno
                    && s.UsuarioCarga == operador
                    && s.Estado == "Borrador");

            if (sesion == null)
            {
                sesion = new SesionPrensa
                {
                    DelegacionId = delegacionId,
                    Fecha = fecha,
                    Turno = turno,
                    Estado = "Borrador",
                    FechaCreacion = DateTime.Now,
                    UsuarioCarga = operador
                };
                db.SesionesPrensas.Add(sesion);
                await db.SaveChangesAsync();
            }

            var nota = new NotaPrensa
            {
                SesionPrensaId = sesion.Id,
                FechaRegistro = DateTime.Now,
                VersionActualId = null
            };
            db.NotasPrensa.Add(nota);
            await db.SaveChangesAsync();
            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: "Aprobada",
                motivoDescarte: null, operadorRevision: operador,
                usuario: operador, tipoEvento: "Creacion",
                sintesis: sintesis,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return nota.Id;
        }

        // Agrega una nota directamente en una sesión Revisada/Finalizada por el ANALISTA.
        // EstadoRevision = "Agregada" — no requiere revisión del OPERADOR.
        public async Task<int> AgregarAnalistaAsync(
            int sesionId, int? categoriaId, int? partidoId, int? localidadId,
            string titulo, string texto, string? fuente, string? link,
            bool esRepercusion, string usuario,
            string? sintesis = null, DateTime? fechaNoticia = null,
            int? caratulaQuironId = null, int? modalidadQuironId = null,
            string? direccion = null, double? latitud = null, double? longitud = null,
            string? ambitoNota = null, string? imagenUrl = null, string? videoUrl = null,
            string? imagenesExtraJson = null, string? otrosMedios = null)
        {
            using var db = _factory.Create();
            // Validar estado SIN rastrear la sesión (proyección): mismo patrón que AgregarAsync,
            // evita que el grafo de la sesión interfiera con la resolución del FK circular nota↔versión
            var estado = await db.SesionesPrensas
                .Where(s => s.Id == sesionId)
                .Select(s => s.Estado)
                .FirstOrDefaultAsync();
            if (estado == null || (estado != "Remitida" && estado != "Finalizada"))
                return 0;

            var nota = new NotaPrensa { SesionPrensaId = sesionId, FechaRegistro = DateTime.Now, VersionActualId = null };
            db.NotasPrensa.Add(nota);
            // Guardar la nota SOLA primero (VersionActualId null, sin versión aún): rompe el ciclo
            // de INSERT NotaPrensa↔NotaVersion. Luego CrearVersionAsync inserta la versión y
            // actualiza VersionActualId con un UPDATE (no un INSERT circular).
            await db.SaveChangesAsync();
            await CrearVersionAsync(db, nota,
                categoriaId, partidoId, localidadId,
                titulo, texto, fuente, link, esRepercusion,
                estadoRevision: "Agregada",
                motivoDescarte: null, operadorRevision: null,
                usuario: usuario, tipoEvento: "Creacion",
                direccion: direccion, latitud: latitud, longitud: longitud,
                sintesis: sintesis, fechaNoticia: fechaNoticia,
                caratulaQuironId: caratulaQuironId, modalidadQuironId: modalidadQuironId,
                ambitoNota: ambitoNota, imagenUrl: imagenUrl, videoUrl: videoUrl,
                imagenesExtraJson: imagenesExtraJson, otrosMedios: otrosMedios);

            return nota.Id;
        }

        // Elimina una nota con EstadoRevision "Agregada" (ANALISTA puede deshacer).
        // Solo permitido mientras la sesión NO esté Finalizada.
        public async Task<bool> EliminarNotaAgregadaAsync(int notaId, string usuario)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.VersionActual)
                .Include(n => n.Sesion)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null || nota.VersionActual.EstadoRevision != "Agregada")
                return false;
            if (nota.VersionActual.Usuario != usuario) return false;
            if (nota.Sesion?.Estado == "Finalizada") return false;

            var sinNotas = await db.SintesisNotas.Where(sn => sn.NotaPrensaId == notaId).ToListAsync();
            db.SintesisNotas.RemoveRange(sinNotas);
            nota.VersionActualId = null;
            db.NotasPrensa.Remove(nota);
            await db.SaveChangesAsync();
            return true;
        }

        // Descarta el borrador activo. Si era la única versión, elimina la nota completa.
        // Retorna (éxito, nota fue eliminada).
        public async Task<(bool success, bool notaEliminada)> DescartarBorradorAsync(int notaId)
        {
            using var db = _factory.Create();
            var nota = await db.NotasPrensa
                .Include(n => n.Versiones)
                .Include(n => n.VersionActual)
                .FirstOrDefaultAsync(n => n.Id == notaId);

            if (nota?.VersionActual == null || nota.VersionActual.EstadoRevision != "Borrador")
                return (false, false);

            var borrador = nota.VersionActual;
            var anterior = nota.Versiones
                .Where(v => v.Id != borrador.Id)
                .OrderByDescending(v => v.Version)
                .FirstOrDefault();

            if (anterior == null)
            {
                // Única versión → eliminar nota completa + referencias
                var sinNotas = await db.SintesisNotas.Where(sn => sn.NotaPrensaId == notaId).ToListAsync();
                db.SintesisNotas.RemoveRange(sinNotas);

                var relaciones = await db.NotasRelaciones
                    .Where(r => r.NotaId == notaId || r.NotaRelacionadaId == notaId).ToListAsync();
                db.NotasRelaciones.RemoveRange(relaciones);

                nota.VersionActualId = null;
                db.NotasPrensa.Remove(nota);
                await db.SaveChangesAsync();
                return (true, true);
            }

            // Revertir a la versión anterior
            nota.VersionActual = anterior;
            anterior.EsActual = true;
            borrador.EsActual = false;
            db.NotasVersion.Remove(borrador);
            await db.SaveChangesAsync();
            return (true, false);
        }
    }

    // Trazabilidad: ciclo de vida completo de una nota (versiones + síntesis)
    public class NotaTraza
    {
        public int NotaId { get; set; }
        public string Titulo { get; set; } = "";
        public string? Delegacion { get; set; }
        public DateTime FechaRegistro { get; set; }
        public int? NotaOrigenId { get; set; }                  // si es copia de otra (linaje)
        public List<TrazaEvento> Eventos { get; set; } = new(); // ordenados cronológicamente
    }

    public class TrazaEvento
    {
        public DateTime Fecha { get; set; }
        public string Tipo { get; set; } = "";          // Nota | Sintesis
        public string TipoEvento { get; set; } = "";    // Creacion/Ampliacion/Aprobacion... | Consolidada/Informada...
        public string Estado { get; set; } = "";        // EstadoRevision de la versión | Estado de la síntesis
        public int? Version { get; set; }               // nro de versión (eventos de nota)
        public string? Usuario { get; set; }
        public string? Detalle { get; set; }            // ResumenCambio / motivo / tipo de síntesis
        public string? MotivoDescarte { get; set; }
    }

    // Línea de tiempo consolidada de una noticia para mostrar en la síntesis
    public class NotaLineaTiempo
    {
        public NotaVersion? Ancla { get; set; }                 // versión original (texto base)
        public List<NotaTimelineItem> Actualizaciones { get; set; } = new();
    }

    public class NotaTimelineItem
    {
        public DateTime Fecha { get; set; }
        public string? Turno { get; set; }          // turno de la síntesis donde se publicó la actualización
        public string Usuario { get; set; } = "";
        public string? ResumenCambio { get; set; }
        public string TipoEvento { get; set; } = "";
        public string EstadoRevision { get; set; } = "";
    }
}
