using System.Data;
using System.Globalization;
using System.Text.Json;
using Medios.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Medios.Services.Respaldos;

public sealed record ResumenRetorno(string Huella, DateTimeOffset Fecha, IReadOnlyDictionary<string, int> Altas,
    int Adjuntos, int CambiosOmitidos, IReadOnlyList<string> Coincidencias, bool Aplicado = false)
{
    public string OrigenRespaldo { get; init; } = "";
    public string HuellaRevision => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(Coincidencias.Order(StringComparer.Ordinal))));
}

public sealed class ImportacionRetornoService(MediosDbContextFactory factory, IWebHostEnvironment environment, IConfiguration config)
{
    private const string Registro = "retornos_contingencia_importados";
    private sealed record Referencia(string Campo, string Tabla, bool Diferida = false);
    private sealed record Tabla(string Nombre, Referencia[] Referencias);
    private sealed record Fila(string Tabla, int Id, Dictionary<string, string?> Datos);

    private static readonly Tabla[] Tablas =
    [
        new("sesiones_prensa", [new("DelegacionId", "delegaciones"), new("SintesisConsolidadaId", "sintesis", true)]),
        new("notas_prensa", [new("SesionPrensaId", "sesiones_prensa"), new("DelegacionId", "delegaciones"),
            new("VersionActualId", "notas_prensa_versiones", true), new("NotaOrigenId", "notas_prensa", true)]),
        new("notas_prensa_versiones", [new("NotaId", "notas_prensa"), new("ParentVersionId", "notas_prensa_versiones", true),
            new("CategoriaId", "categorias_noticia"), new("PartidoId", "partidos_prometheus"), new("LocalidadId", "localidades_prometheus"),
            new("CaratulaQuironId", "caratulas_quiron"), new("ModalidadQuironId", "modalidades_quiron")]),
        new("sintesis", [new("DelegacionId", "delegaciones"), new("SintesisOrigenId", "sintesis", true)]),
        new("sintesis_notas", [new("SintesisId", "sintesis"), new("NotaPrensaId", "notas_prensa"), new("NotaVersionId", "notas_prensa_versiones")]),
        new("sintesis_delegacion_destino", [new("SintesisId", "sintesis"), new("DelegacionId", "delegaciones")]),
        new("nota_relaciones", [new("NotaId", "notas_prensa"), new("NotaRelacionadaId", "notas_prensa"),
            new("nota_id", "notas_prensa"), new("nota_relacionada_id", "notas_prensa")])
    ];
    private static readonly HashSet<string> Nombres = Tablas.Select(t => t.Nombre).ToHashSet(StringComparer.Ordinal);
    public bool Habilitado => config["Deployment:Environment"] == "Production";
    public bool TieneClaveConfigurada => ClaveConfigurada != null;
    internal string? ClaveConfigurada => config["RetornoProduccion:Clave"] is { Length: > 0 } clave ? clave : null;

    public async Task<ResumenRetorno> RevisarAsync(string archivo, string clave, CancellationToken ct) =>
        await ProcesarAsync(archivo, clave, null, null, null, false, ct, null, null);

    public async Task<ResumenRetorno> AplicarAsync(string archivo, string clave, string huella, string revision, string usuario,
        bool confirmarCoincidencias, CancellationToken ct, string? respaldoPrevio = null, string? shaRespaldoPrevio = null) =>
        await ProcesarAsync(archivo, clave, huella, revision, usuario, confirmarCoincidencias, ct, respaldoPrevio, shaRespaldoPrevio);

    private async Task<ResumenRetorno> ProcesarAsync(string archivo, string clave, string? huellaEsperada, string? revisionEsperada,
        string? usuario, bool confirmarCoincidencias, CancellationToken ct, string? respaldoPrevio, string? shaRespaldoPrevio)
    {
        if (!Habilitado) throw new InvalidOperationException("El retorno solo está disponible en producción.");
        await using var paquete = await PaqueteRetorno.AbrirAsync(archivo, clave, ct);
        var aplicar = huellaEsperada != null;
        if (aplicar && huellaEsperada != paquete.Huella) throw new InvalidOperationException("El archivo difiere del revisado.");
        var referencias = paquete.LineaBase.ToDictionary(t => t.Nombre, StringComparer.Ordinal);
        if (Nombres.Any(t => referencias.TryGetValue(t, out var tabla) && tabla.Filas.Count != 0))
            throw new InvalidOperationException("La línea base contiene actividad anterior. Se requiere conciliación específica.");

        var operativos = paquete.Manifiesto.Cambios.Where(c => Nombres.Contains(c.Tabla)).ToList();
        if (operativos.Any(c => c is not { Operacion: "Alta", HashEsperado: null, Datos: not null }))
            throw new InvalidOperationException("El paquete contiene modificaciones de actividad previa y requiere conciliación específica.");
        if (operativos.Count == 0) throw new InvalidOperationException("El paquete no contiene actividad para importar.");

        var filas = operativos.Select(c =>
        {
            var datos = new Dictionary<string, string?>(c.Datos!, StringComparer.OrdinalIgnoreCase);
            var id = Id(datos.GetValueOrDefault("Id"));
            if (c.Clave != Clave(id)) throw new InvalidOperationException("Una fila del paquete tiene una clave inconsistente.");
            return new Fila(c.Tabla, id, datos);
        }).ToList();
        var filasPorClave = filas.ToDictionary(f => (f.Tabla, f.Id));
        var archivos = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var fila in filas) ReescribirAdjuntos(fila.Datos, paquete, archivos);

        await using var db = factory.Create();
        await using var con = new MySqlConnection(db.Database.GetConnectionString());
        await con.OpenAsync(ct);
        if (con.Database != "medios") throw new InvalidOperationException("La base de destino no es la productiva.");
        if (aplicar)
        {
            await using var crear = new MySqlCommand($"""
                CREATE TABLE IF NOT EXISTS {Registro} (
                    origen CHAR(32) NOT NULL PRIMARY KEY, huella CHAR(64) NOT NULL,
                    usuario VARCHAR(200) NOT NULL, fecha_utc DATETIME(6) NOT NULL,
                    respaldo_previo CHAR(32) NULL, sha_respaldo_previo CHAR(64) NULL
                ) ENGINE=InnoDB
                """, con);
            await crear.ExecuteNonQueryAsync(ct);
        }

        await using var tx = await con.BeginTransactionAsync(aplicar ? IsolationLevel.Serializable : IsolationLevel.RepeatableRead, ct);
        var registros = await ConsultaAsync("SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@p0",
            con, tx, ct, Registro);
        if (Convert.ToInt64(registros, CultureInfo.InvariantCulture) > 0 &&
            await ConsultaAsync($"SELECT huella FROM {Registro} WHERE origen=@p0", con, tx, ct, paquete.Manifiesto.OrigenRespaldo) is string)
            throw new InvalidOperationException("Ya se importó un paquete de esta contingencia.");

        var catalogos = new HashSet<(string Tabla, int Id)>();
        var cambiosCatalogo = paquete.Manifiesto.Cambios.Where(c => !Nombres.Contains(c.Tabla))
            .Select(c => (c.Tabla, c.Clave)).ToHashSet();
        var coincidencias = new List<string>();
        var metadatos = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        foreach (var tabla in Tablas)
        {
            var grupo = filas.Where(f => f.Tabla == tabla.Nombre).ToList();
            if (grupo.Count == 0) continue;
            var columnas = await ColumnasAsync(tabla.Nombre, con, tx, ct);
            metadatos[tabla.Nombre] = columnas;
            foreach (var fila in grupo)
            {
                if (fila.Datos.Count != columnas.Count || fila.Datos.Keys.Any(c => !columnas.ContainsKey(c)))
                    throw new InvalidOperationException($"La tabla {tabla.Nombre} tiene un esquema distinto al de la copia.");
                foreach (var vinculo in tabla.Referencias)
                {
                    if (fila.Datos.GetValueOrDefault(vinculo.Campo) is not { } valor) continue;
                    var destino = (vinculo.Tabla, Id(valor));
                    if (Nombres.Contains(vinculo.Tabla))
                    {
                        if (!filasPorClave.ContainsKey(destino))
                            throw new InvalidOperationException($"Falta una referencia de {tabla.Nombre} a {vinculo.Tabla} en el paquete.");
                    }
                    else if (catalogos.Add(destino))
                    {
                        if (cambiosCatalogo.Contains((destino.Tabla, Clave(destino.Item2))))
                            throw new InvalidOperationException($"La copia modificó el catálogo {destino.Tabla} (ID {destino.Item2}). Revisá esa correspondencia antes de importar.");
                        await ValidarCatalogoAsync(destino.Tabla, destino.Item2, referencias, con, tx, ct);
                    }
                }
                await BuscarCoincidenciasAsync(fila, filasPorClave, coincidencias, con, tx, ct);
            }
        }
        var resumen = new ResumenRetorno(paquete.Huella, paquete.Manifiesto.GeneradoUtc,
            filas.GroupBy(f => f.Tabla).ToDictionary(g => g.Key, g => g.Count()),
            archivos.Count, paquete.Manifiesto.Cambios.Count - filas.Count, coincidencias)
        { OrigenRespaldo = paquete.Manifiesto.OrigenRespaldo };
        if (!aplicar) return resumen;
        if (resumen.HuellaRevision != revisionEsperada)
            throw new InvalidOperationException("Las coincidencias en producción cambiaron. Volvé a revisar el paquete.");
        if (coincidencias.Count > 0 && !confirmarCoincidencias)
            throw new InvalidOperationException("Revisá las coincidencias y confirmá si corresponden a registros distintos.");

        await using (var registro = new MySqlCommand($"INSERT INTO {Registro} (origen, huella, usuario, fecha_utc, respaldo_previo, sha_respaldo_previo) VALUES (@p0,@p1,@p2,UTC_TIMESTAMP(6),@p3,@p4)", con, tx))
        {
            Agregar(registro, [paquete.Manifiesto.OrigenRespaldo, paquete.Huella, usuario, respaldoPrevio, shaRespaldoPrevio]);
            await registro.ExecuteNonQueryAsync(ct);
        }
        var ids = new Dictionary<(string Tabla, int Id), long>();
        foreach (var tabla in Tablas)
        {
            foreach (var fila in filas.Where(f => f.Tabla == tabla.Nombre))
            {
                var datos = fila.Datos.Where(c => !c.Key.Equals("Id", StringComparison.OrdinalIgnoreCase))
                    .ToDictionary(StringComparer.OrdinalIgnoreCase);
                foreach (var vinculo in tabla.Referencias.Where(r => Nombres.Contains(r.Tabla)))
                {
                    if (datos.GetValueOrDefault(vinculo.Campo) is not { } valor) continue;
                    datos[vinculo.Campo] = vinculo.Diferida ? null : ids[(vinculo.Tabla, Id(valor))].ToString(CultureInfo.InvariantCulture);
                }
                var columnas = metadatos[tabla.Nombre];
                var campos = datos.Keys.ToList();
                var parametros = string.Join(",", campos.Select((_, i) => "@p" + i));
                await using var insert = new MySqlCommand($"INSERT INTO {Q(tabla.Nombre)} ({string.Join(",", campos.Select(Q))}) VALUES ({parametros})", con, tx);
                Agregar(insert, campos.Select(c => Tipo(datos[c], columnas[c])));
                await insert.ExecuteNonQueryAsync(ct);
                if (insert.LastInsertedId <= 0) throw new InvalidOperationException($"La tabla {tabla.Nombre} no generó un identificador.");
                ids.Add((tabla.Nombre, fila.Id), insert.LastInsertedId);
            }
        }
        foreach (var tabla in Tablas)
        {
            foreach (var fila in filas.Where(f => f.Tabla == tabla.Nombre))
            foreach (var refDiferida in tabla.Referencias.Where(v => v.Diferida))
            {
                if (fila.Datos.GetValueOrDefault(refDiferida.Campo) is not { } valor) continue;
                await using var update = new MySqlCommand($"UPDATE {Q(tabla.Nombre)} SET {Q(refDiferida.Campo)}=@p0 WHERE Id=@p1", con, tx);
                Agregar(update, [ids[(refDiferida.Tabla, Id(valor))], ids[(tabla.Nombre, fila.Id)]]);
                await update.ExecuteNonQueryAsync(ct);
            }
        }

        // Las rutas son propias del paquete. Si el proceso se interrumpe antes del commit,
        // un reintento puede reutilizar los archivos idénticos sin sobrescribir archivos existentes.
        var publicados = new List<string>();
        try
        {
            foreach (var (destino, origen) in archivos)
            {
                var ruta = Path.Combine(environment.WebRootPath, destino);
                if (await PublicarArchivoAsync(ruta, origen, ct)) publicados.Add(ruta);
            }
        }
        catch
        {
            foreach (var ruta in publicados) File.Delete(ruta);
            throw;
        }
        await tx.CommitAsync(CancellationToken.None);
        return resumen with { Aplicado = true };
    }

    private static async Task<Dictionary<string, string>> ColumnasAsync(string tabla, MySqlConnection con, MySqlTransaction tx, CancellationToken ct)
    {
        await using var engine = new MySqlCommand("SELECT ENGINE FROM information_schema.TABLES WHERE TABLE_SCHEMA=DATABASE() AND TABLE_NAME=@p0", con, tx);
        Agregar(engine, [tabla]);
        if (await engine.ExecuteScalarAsync(ct) is not string motor || !motor.Equals("InnoDB", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"La tabla {tabla} debe permitir una importación transaccional.");
        await using var cmd = new MySqlCommand($"SHOW COLUMNS FROM {Q(tabla)}", con, tx);
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (await rows.ReadAsync(ct)) result.Add(rows.GetString("Field"), rows.GetString("Type"));
        return result;
    }

    private static async Task ValidarCatalogoAsync(string tabla, int id, Dictionary<string, TablaReferencia> referencias,
        MySqlConnection con, MySqlTransaction tx, CancellationToken ct)
    {
        var clave = Clave(id);
        var esperado = referencias.GetValueOrDefault(tabla)?.Filas.FirstOrDefault(f => f.Clave == clave)?.Hash;
        await using var cmd = new MySqlCommand($"SELECT * FROM {Q(tabla)} WHERE Id=@p0", con, tx);
        Agregar(cmd, [id]);
        await using var rows = await cmd.ExecuteReaderAsync(ct);
        if (esperado == null || !await rows.ReadAsync(ct))
            throw new InvalidOperationException($"Falta una referencia del catálogo {tabla} (ID {id}).");
        var dato = new Dictionary<string, string?>();
        for (var i = 0; i < rows.FieldCount; i++)
            dato.Add(rows.GetName(i), rows.IsDBNull(i) ? null : RetornoProduccionService.Valor(rows.GetValue(i)));
        if (esperado != RetornoProduccionService.Huella(dato))
            throw new InvalidOperationException($"Cambió el catálogo {tabla} (ID {id}). Revisá esa correspondencia antes de importar.");
    }

    private static async Task BuscarCoincidenciasAsync(Fila fila, IReadOnlyDictionary<(string, int), Fila> filas,
        List<string> coincidencias, MySqlConnection con, MySqlTransaction tx, CancellationToken ct)
    {
        if (fila.Tabla == "notas_prensa" && fila.Datos.GetValueOrDefault("VersionActualId") is { } version &&
            filas.TryGetValue(("notas_prensa_versiones", Id(version)), out var nota))
        {
            var link = nota.Datos.GetValueOrDefault("Link") ?? "";
            var titulo = nota.Datos.GetValueOrDefault("Titulo");
            var encontrada = await ConsultaAsync("""
                SELECT n.Id FROM notas_prensa n JOIN notas_prensa_versiones v ON v.Id=n.VersionActualId
                WHERE (@p0 <> '' AND v.Link=@p0) OR (v.Titulo=@p1 AND DATE(v.FechaNoticia) <=> DATE(@p2))
                ORDER BY n.Id LIMIT 1
                """, con, tx, ct, link, titulo, nota.Datos.GetValueOrDefault("FechaNoticia"));
            if (encontrada != null) coincidencias.Add($"Nota {fila.Id} del paquete: {titulo} (posible coincidencia con nota {encontrada} en producción).");
        }
        if (fila.Tabla is "sesiones_prensa" or "sintesis")
        {
            var campo = fila.Tabla == "sintesis" ? "Tipo" : "Turno";
            var fecha = fila.Datos.GetValueOrDefault("Fecha");
            var turno = fila.Datos.GetValueOrDefault(campo);
            var encontrada = await ConsultaAsync($"SELECT Id FROM {Q(fila.Tabla)} WHERE Fecha=@p0 AND {Q(campo)}=@p1 AND DelegacionId <=> @p2 ORDER BY Id LIMIT 1",
                con, tx, ct, fecha, turno, fila.Datos.GetValueOrDefault("DelegacionId"));
            if (encontrada != null) coincidencias.Add($"{(fila.Tabla == "sintesis" ? "Síntesis" : "Sesión")} {fila.Id} del paquete: {fecha}, {turno} (posible coincidencia con ID {encontrada} en producción).");
        }
    }

    private static void ReescribirAdjuntos(Dictionary<string, string?> datos, PaqueteRetorno paquete, Dictionary<string, string> archivos)
    {
        string Ruta(string valor, string carpeta)
        {
            var relativa = valor.TrimStart('/');
            if (!relativa.StartsWith(carpeta + "/", StringComparison.Ordinal) || !paquete.Archivos.TryGetValue(relativa, out var origen))
                throw new InvalidOperationException("Falta un archivo referenciado en el paquete de retorno.");
            var destino = carpeta + "/retorno-" + paquete.Huella.ToLowerInvariant()[..24] + "/" + relativa[(carpeta.Length + 1)..];
            archivos.TryAdd(destino, origen);
            return "/" + destino;
        }
        if (datos.TryGetValue("PDFPath", out var pdf) && !string.IsNullOrWhiteSpace(pdf)) datos["PDFPath"] = Ruta(pdf, "pdf");
        string Imagen(string valor) => valor.StartsWith("/uploads/", StringComparison.Ordinal) || valor.StartsWith("uploads/", StringComparison.Ordinal)
            ? Ruta(valor, "uploads") : valor;
        if (datos.TryGetValue("ImagenUrl", out var imagen) && imagen != null) datos["ImagenUrl"] = Imagen(imagen);
        if (datos.TryGetValue("ImagenesUrls", out var json) && !string.IsNullOrWhiteSpace(json))
            datos["ImagenesUrls"] = JsonSerializer.Serialize((JsonSerializer.Deserialize<string[]>(json) ?? []).Select(Imagen));
    }

    private static async Task<bool> PublicarArchivoAsync(string destino, string origen, CancellationToken ct)
    {
        var carpeta = Path.GetDirectoryName(destino)!;
        Directory.CreateDirectory(carpeta);
        var temp = Path.Combine(carpeta, "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            if (!File.Exists(destino))
            {
                await using var fuente = File.OpenRead(origen);
                await using (var salida = new FileStream(temp, FileMode.CreateNew)) await fuente.CopyToAsync(salida, ct);
                File.Move(temp, destino);
                return true;
            }
            await using var anterior = File.OpenRead(destino);
            await using var nuevo = File.OpenRead(origen);
            if (!(await System.Security.Cryptography.SHA256.HashDataAsync(anterior, ct))
                .SequenceEqual(await System.Security.Cryptography.SHA256.HashDataAsync(nuevo, ct)))
                throw new InvalidOperationException("Ya existe un adjunto distinto en la ruta de retorno.");
            return false;
        }
        finally { File.Delete(temp); }
    }

    private static string Clave(int id) => JsonSerializer.Serialize(new[] { id.ToString(CultureInfo.InvariantCulture) });
    private static int Id(string? valor) => int.TryParse(valor, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
        ? id : throw new InvalidOperationException("El paquete contiene un identificador inválido.");
    private static string Q(string nombre) => "`" + nombre.Replace("`", "``") + "`";
    private static object? Tipo(string? valor, string tipo) =>
        tipo.Equals("tinyint(1)", StringComparison.OrdinalIgnoreCase) && bool.TryParse(valor, out var booleano) ? booleano ? 1 : 0 : valor;

    private static void Agregar(MySqlCommand cmd, IEnumerable<object?> valores)
    {
        var indice = 0;
        foreach (var valor in valores) cmd.Parameters.AddWithValue("@p" + indice++, valor ?? DBNull.Value);
    }

    private static async Task<object?> ConsultaAsync(string sql, MySqlConnection con, MySqlTransaction tx,
        CancellationToken ct, params object?[] valores)
    {
        await using var cmd = new MySqlCommand(sql, con, tx);
        Agregar(cmd, valores);
        return await cmd.ExecuteScalarAsync(ct);
    }
}
