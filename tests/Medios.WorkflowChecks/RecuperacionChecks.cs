using Medios.Entities;
using Medios.Services;
using Microsoft.EntityFrameworkCore;

static class RecuperacionChecks
{
    public static async Task<int> RunAsync()
    {
        var count = 0;
        void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); count++; }
        var factory = new MemoriaFactory();
        var notas = new NotaService(factory);
        var sintesis = new SintesisService(factory, null!, notas);
        var sesiones = new SesionService(factory, null!, sintesis, notas);
        int original, anterior, destino, versionRechazada, ajeno;
        using (var db = factory.Create())
        {
            var vieja = new SesionPrensa { DelegacionId = 9, Estado = "Finalizada", Turno = "Vespertina" };
            var nueva = new SesionPrensa { DelegacionId = 9, Estado = "Borrador", UsuarioCarga = "delegacion", Turno = "Ampliacion Vespertina" };
            var otra = new SesionPrensa { DelegacionId = 2, Estado = "Borrador", UsuarioCarga = "delegacion" };
            var n = new NotaPrensa { Sesion = vieja };
            n.VersionActual = new NotaVersion { Nota = n, Version = 1, EsActual = true,
                EstadoRevision = "Sin Remitir", MotivoDescarte = "Categoría incorrecta", TipoEvento = "Descarte",
                Titulo = "Noticia", Texto = "Texto", Fuente = "Medio", Sintesis = "Resumen", VideoUrl = "video" };
            db.NotasPrensa.Add(n); db.SesionesPrensas.AddRange(nueva, otra);
            await db.SaveChangesAsync();
            original = n.Id; anterior = vieja.Id; destino = nueva.Id; ajeno = otra.Id; versionRechazada = n.VersionActual.Id;
        }
        Check((await notas.GetRechazadasAsync(9)).Count == 1, "Rechazo pendiente visible");
        Check((await sesiones.GetNotasSinRemitirByDelegacionAsync(9)).Count == 1, "Alerta previa a recuperación");
        Check(await notas.AgregarDesdeVersionAsync(ajeno, original, "Nombre", "delegacion") == 0, "No recuperar en otra delegación");
        Check(await notas.AgregarDesdeVersionAsync(destino, original, "Nombre", "otro") == 0, "No recuperar en borrador de otro usuario");
        Check(await notas.AgregarDesdeVersionAsync(destino, original, "Nombre", "delegacion") == original, "Agregar desde otra sesión recupera el mismo ID");
        using (var db = factory.Create())
        {
            var n = await db.NotasPrensa.Include(n => n.VersionActual).SingleAsync();
            Check(n.Id == original && n.NotaOrigenId == null && n.SesionPrensaId == destino, "No crea copia al recuperar");
            Check(n.VersionActual!.Version == 2 && n.VersionActual.ParentVersionId == versionRechazada, "Nueva versión conserva padre rechazado");
            Check(n.VersionActual.MotivoDescarte == null && n.VersionActual.Sintesis == "Resumen" && n.VersionActual.VideoUrl == "video", "Recuperación conserva contenido y limpia rechazo actual");
            Check((await db.NotasVersion.FindAsync(versionRechazada))!.MotivoDescarte == "Categoría incorrecta", "El motivo anterior no se sobrescribe");
        }
        Check(!(await notas.GetRechazadasAsync(9)).Any(), "Sale de Denegadas al recuperar");
        Check(!(await notas.GetDescartadasSinVincularAsync(9)).Any(), "Sale de alertas de Notas al recuperar");
        Check(!(await sesiones.GetNotasSinRemitirByDelegacionAsync(9)).Any(), "Sale de alertas de Home y Borradores al recuperar");
        Check(await notas.EditarBorradorAsync(original, "Nombre", null, null, null, "Corregida", "Texto", "Medio", null, false,
            userDelegacionId: 9, usuarioLogin: "delegacion"), "Corregir la nota original en el nuevo borrador");
        var publicada = await sesiones.ConvertirASintesisAsync(destino, "delegacion");
        Check(publicada > 0, "Remitir la nota recuperada");
        Check(!(await notas.GetRechazadasAsync(9)).Any() && !(await sesiones.GetNotasSinRemitirByDelegacionAsync(9)).Any()
            && !(await notas.GetDescartadasSinVincularAsync(9)).Any(), "Remitida no reaparece en listas ni alertas");
        var historia = await notas.GetHistorialNoticiaAsync(original);
        Check(historia.Versiones.Count == 3 && historia.SintesisPorVersion.Values.SelectMany(x => x).Any(s => s.Id == publicada), "Historial conserva versiones y nueva síntesis");
        Check(historia.Eventos.Any(t => t.Evento == "NOTA_RECUPERADA" && t.SesionId == anterior), "Conserva sesión histórica de rechazo");

        // Caso legado como producción: original rechazada y copia corregida ya remitida.
        int raiz, copia, copiaVersion, snapshot, independiente;
        using (var db = factory.Create())
        {
            var vieja = new SesionPrensa { DelegacionId = 9, Estado = "Finalizada" };
            var nueva = new SesionPrensa { DelegacionId = 9, Estado = "Remitida" };
            var n = new NotaPrensa { Sesion = vieja };
            n.VersionActual = new NotaVersion { Nota = n, Version = 1, EstadoRevision = "Sin Remitir", MotivoDescarte = "Motivo", Titulo = "Mismo título" };
            var c = new NotaPrensa { Sesion = nueva, NotaOrigen = n };
            c.VersionActual = new NotaVersion { Nota = c, Version = 2, EstadoRevision = "Remitida", Titulo = "Mismo título" };
            var distinta = new NotaPrensa { Sesion = vieja };
            distinta.VersionActual = new NotaVersion { Nota = distinta, Version = 1, EstadoRevision = "Sin Remitir", MotivoDescarte = "Pendiente real", Titulo = "Mismo título" };
            var pdf = new Sintesis { Estado = "Remitida", PDFPath = "/pdf/previo.pdf", DelegacionId = 9 };
            var consolidada = new Sintesis { Estado = "Informada", PDFPath = "/pdf/consolidada.pdf" };
            pdf.NotasIncluidas.Add(new SintesisNota { Nota = c, NotaVersion = c.VersionActual });
            consolidada.NotasIncluidas.Add(new SintesisNota { Nota = c, NotaVersion = c.VersionActual });
            db.NotasPrensa.AddRange(n, c, distinta); db.Sintesis.AddRange(pdf, consolidada);
            await db.SaveChangesAsync();
            raiz = n.Id; copia = c.Id; copiaVersion = c.VersionActual.Id; snapshot = pdf.Id; independiente = distinta.Id;
        }
        foreach (var lista in new[] { await notas.GetRechazadasAsync(9), await notas.GetDescartadasSinVincularAsync(9), await sesiones.GetNotasSinRemitirByDelegacionAsync(9) })
            Check(lista.Count == 1 && lista[0].Id == independiente, "Legado recuperado no alerta; título idéntico independiente sí");
        historia = await notas.GetHistorialNoticiaAsync(copia);
        Check(historia.NotaOriginalId == raiz && historia.Versiones.Any(v => v.NotaId == raiz) && historia.Versiones.Any(v => v.NotaId == copia), "Historial unifica el linaje sin fusionar IDs");
        Check(historia.SintesisPorVersion[copiaVersion].Count == 2, "Historial conserva todas las síntesis de una misma versión");
        using (var db = factory.Create())
        {
            var sn = await db.SintesisNotas.SingleAsync(sn => sn.SintesisId == snapshot);
            Check(sn.NotaPrensaId == copia && sn.NotaVersionId == copiaVersion && (await db.Sintesis.FindAsync(snapshot))!.PDFPath == "/pdf/previo.pdf", "Consulta histórica no cambia snapshots ni PDF");
            var c = (await db.NotasPrensa.Include(n => n.VersionActual).SingleAsync(n => n.Id == copia));
            c.VersionActual!.EstadoRevision = "Sin Remitir"; c.VersionActual.MotivoDescarte = "Nuevo rechazo";
            await db.SaveChangesAsync();
        }
        Check((await notas.GetRechazadasAsync(9)).Any(n => n.Id == copia) && !(await notas.GetRechazadasAsync(9)).Any(n => n.Id == raiz), "Un nuevo rechazo de la copia sí alerta, sin resucitar el original");
        Check((await notas.GetHistorialNoticiaAsync(raiz)).Versiones.Count == 2, "Desde el original se ve también su recuperación histórica");
        return count;
    }
}
