using HtmlAgilityPack;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Medios.Services;

public class ScraperService
{
    private readonly HttpClient _http;
    private readonly ScrapingReglaService _reglas;

    public ScraperService(HttpClient http, ScrapingReglaService reglas)
    {
        _http = http;
        _reglas = reglas;
        _http.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
        _http.Timeout = TimeSpan.FromSeconds(15);
    }

    // Dominios de redes sociales que requieren tratamiento especial (Open Graph vía crawler)
    private static readonly string[] DominiosSociales =
        { "facebook.com", "instagram.com", "fb.com", "fb.watch", "threads.net", "x.com", "twitter.com" };

    private static bool EsRedSocial(string dominio) =>
        DominiosSociales.Any(d => dominio == d || dominio.EndsWith("." + d));

    public async Task<(string? Titulo, string? Texto, string? Direccion, string? Fuente, DateTime? FechaNoticia, string? ImagenUrl, string? VideoUrl, string? Error)> ScrapearAsync(string url)
    {
        try
        {
            var dominio = ScrapingReglaService.NormalizarDominio(url);

            // Facebook/Instagram/X bloquean el scraping normal y cargan el contenido por JS.
            // Se extrae el texto del post desde los meta Open Graph usando un User-Agent de crawler.
            if (EsRedSocial(dominio))
                return await ScrapearRedSocialAsync(url, dominio);

            using var httpResp = await _http.GetAsync(url);
            httpResp.EnsureSuccessStatusCode();
            var htmlBytes = await httpResp.Content.ReadAsByteArrayAsync();
            var html = DecodificarHtml(htmlBytes, httpResp.Content.Headers.ContentType?.CharSet);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            // Imagen representativa de la nota (Open Graph), antes de remover nodos
            var imagenUrl = MetaContent(doc, "og:image") ?? MetaContent(doc, "twitter:image");

            // Video: meta OG/Twitter, iframes de YouTube/Vimeo, <video>, o embeds
            // sociales (Instagram/X/TikTok). Se extrae ANTES de remover los iframes.
            var videoUrl = ExtraerVideo(doc, url);

            // Regla específica del portal (si existe) para adaptar los criterios de scraping
            var regla = await _reglas.GetByDominioAsync(dominio);

            // Extraer metadatos ANTES de remover scripts (JSON-LD)
            var direccion = ExtraerDireccion(doc);
            var fechaNoticia = ExtraerFechaPublicacion(doc);

            // Eliminar scripts, estilos, nav, footer, aside y secciones de ruido
            var removeNodes = doc.DocumentNode.SelectNodes(
                "//script|//style|//nav|//footer|//aside|//header|//form|//iframe|//noscript");
            foreach (var node in removeNodes?.Cast<HtmlNode>().ToList() ?? [])
                node.Remove();

            // Eliminar bloques de "notas relacionadas" y marcadores de ruido presentes en
            // muchos portales (ej. 0221.com.ar usa class="ignore-parser" en los teasers).
            var noiseNodes = doc.DocumentNode.SelectNodes(
                "//*[contains(@class,'ignore-parser')]" +
                "| //*[contains(@class,'may-also-be')]" +
                "| //*[contains(@class,'also-interest')]" +
                "| //*[contains(@class,'lee-tambien')]" +
                "| //*[contains(@class,'related-news')]" +
                "| //*[contains(@class,'related-articles')]" +
                "| //*[contains(@class,'notas-relacionadas')]" +
                "| //*[contains(@class,'articulos-relacionados')]" +
                "| //*[contains(@class,'recomendados')]" +
                "| //section[contains(@class,'related')]" +
                "| //section[contains(@class,'recomend')]" +
                "| //section[contains(@class,'may-also')]" +
                "| //section[contains(@class,'news_background')]");
            foreach (var node in noiseNodes?.Cast<HtmlNode>().ToList() ?? [])
                node.Remove();

            // Clases de ruido específicas del portal (configurables por dominio)
            if (!string.IsNullOrWhiteSpace(regla?.ClasesExcluir))
            {
                foreach (var clase in regla!.ClasesExcluir.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    var nodos = doc.DocumentNode.SelectNodes($"//*[contains(@class,'{clase}')]");
                    foreach (var node in nodos?.Cast<HtmlNode>().ToList() ?? [])
                        node.Remove();
                }
            }

            // Título: regla del portal, sino <h1> del artículo o <title>
            string? titulo = null;
            if (!string.IsNullOrWhiteSpace(regla?.XPathTitulo))
                titulo = doc.DocumentNode.SelectSingleNode(regla!.XPathTitulo)?.InnerText.Trim();
            if (string.IsNullOrWhiteSpace(titulo))
                titulo = doc.DocumentNode.SelectSingleNode("//article//h1 | //main//h1 | //h1")?.InnerText.Trim();
            if (string.IsNullOrWhiteSpace(titulo))
                titulo = doc.DocumentNode.SelectSingleNode("//title")?.InnerText.Trim();

            titulo = LimpiarTexto(titulo);

            // Cuerpo — estrategias en orden:
            //   1. JSON-LD articleBody (si la regla lo permite y existe) — fuente más limpia
            //   2. XPath del cuerpo definido en la regla
            //   3. Heurística por especificidad (contenedores típicos)
            string? texto = null;

            var preferirJsonLd = regla?.PreferirJsonLd ?? true;
            if (preferirJsonLd && string.IsNullOrWhiteSpace(regla?.XPathCuerpo))
            {
                var jsonBody = ExtraerArticleBodyJsonLd(doc);
                if (!string.IsNullOrWhiteSpace(jsonBody) && jsonBody.Length >= 300)
                    texto = jsonBody;
            }

            // Cuerpo HTML: si la regla define un XPath, usarlo; sino heurística por especificidad
            var candidatos = !string.IsNullOrWhiteSpace(regla?.XPathCuerpo)
                ? new[] { regla!.XPathCuerpo! }
                : new[]
                {
                    "//article",
                    "//*[contains(@class,'article-body')]",
                    "//*[contains(@class,'article-content')]",
                    "//*[contains(@class,'entry-content')]",
                    "//*[contains(@class,'nota-cuerpo')]",
                    "//*[contains(@class,'post-content')]",
                    "//*[contains(@class,'content-body')]",
                    "//*[contains(@class,'story-body')]",
                    "//main",
                };

            foreach (var xpath in string.IsNullOrWhiteSpace(texto) ? candidatos : Array.Empty<string>())
            {
                // Usar SelectNodes para combinar múltiples elementos con la misma clase
                // (ej. 0221.com.ar tiene 6 <article class="article-body"> de 1 párrafo c/u)
                var nodos = doc.DocumentNode.SelectNodes(xpath);
                if (nodos == null || nodos.Count == 0) continue;
                var parrafos = new List<string>();
                foreach (var nodo in nodos)
                    foreach (var p in ExtraerParrafos(nodo))
                        if (!parrafos.Contains(p)) parrafos.Add(p);
                if (parrafos.Count >= 2) { texto = string.Join("\n\n", parrafos); break; }
            }

            // Fallback: párrafos de todo el documento
            if (string.IsNullOrWhiteSpace(texto))
            {
                var parrafos = ExtraerParrafos(doc.DocumentNode).Take(30).ToList();
                if (parrafos.Count > 0) texto = string.Join("\n\n", parrafos);
            }

            if (string.IsNullOrWhiteSpace(titulo) && string.IsNullOrWhiteSpace(texto))
                return (null, null, null, null, null, null, null, "No se pudo extraer contenido de la página.");

            var fuente = ExtraerFuente(url);
            return (titulo, texto, direccion, fuente, fechaNoticia, imagenUrl, videoUrl, null);
        }
        catch (HttpRequestException ex)
        {
            return (null, null, null, null, null, null, null, $"No se pudo acceder a la URL: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return (null, null, null, null, null, null, null, "La solicitud tardó demasiado. Verificá la URL.");
        }
        catch (Exception ex)
        {
            return (null, null, null, null, null, null, null, $"Error al procesar la página: {ex.Message}");
        }
    }

    // Scraping de redes sociales: usa el User-Agent de crawler de Facebook para obtener
    // los meta Open Graph (og:title / og:description), que contienen el texto del post.
    private async Task<(string? Titulo, string? Texto, string? Direccion, string? Fuente, DateTime? FechaNoticia, string? ImagenUrl, string? VideoUrl, string? Error)>
        ScrapearRedSocialAsync(string url, string dominio)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("User-Agent",
                "facebookexternalhit/1.1 (+http://www.facebook.com/externalhit_uatext.php)");
            using var resp = await _http.SendAsync(req);
            var htmlBytesSocial = await resp.Content.ReadAsByteArrayAsync();
            var html = DecodificarHtml(htmlBytesSocial, resp.Content.Headers.ContentType?.CharSet);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var ogTitle = LimpiarTexto(MetaContent(doc, "og:title"));
            var ogDesc  = HtmlEntity.DeEntitize(MetaContent(doc, "og:description") ?? "").Trim();

            var ogImagen = MetaContent(doc, "og:image");
            // Video solo si hay un og:video REAL (URL de media), o si la propia URL es
            // claramente de un video (reel / watch / videos / fb.watch). NO se infiere por
            // og:type, porque Facebook devuelve "video.*" hasta en posts con foto → falso
            // positivo que tomaba la URL del post (vista del posteo, no un video).
            var ogVideo = MetaContent(doc, "og:video:secure_url") ?? MetaContent(doc, "og:video:url")
                        ?? MetaContent(doc, "og:video");
            var esUrlDeVideo = Regex.IsMatch(url, @"/(reel|reels|watch|videos?)/", RegexOptions.IgnoreCase)
                            || url.Contains("fb.watch", StringComparison.OrdinalIgnoreCase);
            var videoSocial = !string.IsNullOrWhiteSpace(ogVideo) ? ogVideo
                            : (esUrlDeVideo ? LimpiarUrlSocial(url) : null);

            if (string.IsNullOrWhiteSpace(ogDesc) && string.IsNullOrWhiteSpace(ogTitle))
                return (null, null, null, null, null, null, null,
                    "No se pudo extraer el contenido. Las publicaciones de redes sociales privadas o " +
                    "eliminadas no se pueden importar; copiá y pegá el texto manualmente.");

            // El og:description suele traer el título del post en la primera línea y el cuerpo debajo.
            var lineas = ogDesc.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();

            string? titulo;
            string? texto;
            if (lineas.Count >= 2)
            {
                titulo = lineas[0];
                texto = string.Join("\n\n", lineas);
            }
            else
            {
                titulo = string.IsNullOrWhiteSpace(ogTitle) ? (lineas.FirstOrDefault() ?? "") : ogTitle;
                texto = ogDesc;
            }

            // Aviso: el contenido de redes sociales puede venir truncado por la plataforma
            if (!string.IsNullOrWhiteSpace(texto) && texto.TrimEnd().EndsWith("..."))
                texto += "\n\n[El contenido pudo quedar truncado por la red social. Verificá y completá desde la publicación original.]";

            var fuente = ExtraerFuente(url);
            return (LimpiarTexto(titulo), texto, null, fuente, null, ogImagen, videoSocial, null);
        }
        catch (Exception ex)
        {
            return (null, null, null, null, null, null, null,
                $"No se pudo acceder a la publicación de red social: {ex.Message}");
        }
    }

    // Decodifica el HTML según el charset real de la página, en vez de asumir UTF-8 (que es
    // lo que hace HttpClient.GetStringAsync por defecto). Muchos portales viejos sirven
    // Windows-1252/ISO-8859-1 sin declararlo bien, y decodificar esos bytes como UTF-8
    // reemplaza cada tilde/ñ por "�" en vez de fallar — por eso no alcanza con confiar
    // ciegamente en el charset del header cuando existe: se valida decodificando UTF-8 en
    // modo estricto y, si falla, se cae a Latin-1 (cubre Windows-1252 para el rango de
    // caracteres acentuados del español, que es idéntico en ambas codificaciones).
    private static string DecodificarHtml(byte[] bytes, string? headerCharset)
    {
        if (!string.IsNullOrWhiteSpace(headerCharset))
        {
            try
            {
                var encoding = Encoding.GetEncoding(headerCharset.Trim(' ', '"', '\''));
                if (encoding.CodePage != Encoding.UTF8.CodePage)
                    return encoding.GetString(bytes);
            }
            catch { /* charset desconocido/inválido: seguir con la detección */ }
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    // Devuelve el content de un <meta property="..."> o <meta name="...">
    private static string? MetaContent(HtmlDocument doc, string property)
    {
        var node = doc.DocumentNode.SelectSingleNode($"//meta[@property='{property}']")
                ?? doc.DocumentNode.SelectSingleNode($"//meta[@name='{property}']");
        return node?.GetAttributeValue("content", null);
    }

    // Extrae el video más representativo de la nota. Orden de prioridad:
    //   1. Open Graph / Twitter player (og:video / twitter:player)
    //   2. iframe de YouTube/Vimeo (incluye lazy-load por data-src)
    //   3. <video><source src> propio del portal
    //   4. Embeds sociales: Instagram (data-instgrm-permalink), X/Twitter, TikTok
    // Se ejecuta ANTES de remover los iframes del documento.
    private static string? ExtraerVideo(HtmlDocument doc, string urlPagina)
    {
        // 1. Meta tags de video
        var meta = MetaContent(doc, "og:video:secure_url") ?? MetaContent(doc, "og:video:url")
                 ?? MetaContent(doc, "og:video") ?? MetaContent(doc, "twitter:player");
        if (!string.IsNullOrWhiteSpace(meta) && EsUrlVideoValida(meta))
            return AbsolutaUrl(meta, urlPagina);

        // 2. iframe de YouTube / Vimeo (src o data-src de lazy-load)
        var iframes = doc.DocumentNode.SelectNodes("//iframe");
        if (iframes != null)
            foreach (var f in iframes)
            {
                var src = f.GetAttributeValue("src", null) ?? f.GetAttributeValue("data-src", null)
                        ?? f.GetAttributeValue("data-litespeed-src", null);
                if (!string.IsNullOrWhiteSpace(src)
                    && Regex.IsMatch(src, @"(youtube\.com|youtu\.be|youtube-nocookie\.com|vimeo\.com|dailymotion\.com)", RegexOptions.IgnoreCase))
                    return AbsolutaUrl(src.StartsWith("//") ? "https:" + src : src, urlPagina);
            }

        // 3. <video><source src>
        var source = doc.DocumentNode.SelectSingleNode("//video//source[@src]")
                  ?? doc.DocumentNode.SelectSingleNode("//video[@src]");
        var vsrc = source?.GetAttributeValue("src", null);
        if (!string.IsNullOrWhiteSpace(vsrc))
            return AbsolutaUrl(vsrc, urlPagina);

        // 4. Embed de Instagram (reel/post incrustado en el artículo)
        var ig = doc.DocumentNode.SelectSingleNode("//*[@data-instgrm-permalink]")
                    ?.GetAttributeValue("data-instgrm-permalink", null)
              ?? doc.DocumentNode.SelectSingleNode("//blockquote[contains(@class,'instagram-media')]//a[@href]")
                    ?.GetAttributeValue("href", null);
        if (!string.IsNullOrWhiteSpace(ig))
            return LimpiarUrlSocial(ig);

        // 4b. Embed de X/Twitter
        var tw = doc.DocumentNode.SelectSingleNode("//blockquote[contains(@class,'twitter-tweet')]//a[contains(@href,'twitter.com') or contains(@href,'x.com')]")
                    ?.GetAttributeValue("href", null);
        if (!string.IsNullOrWhiteSpace(tw))
            return LimpiarUrlSocial(tw);

        // 4c. Embed de TikTok
        var tk = doc.DocumentNode.SelectSingleNode("//blockquote[contains(@class,'tiktok-embed')]")
                    ?.GetAttributeValue("cite", null);
        if (!string.IsNullOrWhiteSpace(tk))
            return LimpiarUrlSocial(tk);

        return null;
    }

    // Filtra URLs de iframe que NO son video (ej. tag manager, ads)
    private static bool EsUrlVideoValida(string url) =>
        !Regex.IsMatch(url, @"(googletagmanager|doubleclick|adservice|/ads?/)", RegexOptions.IgnoreCase);

    // Quita los parámetros de tracking (utm_*, etc.) de un permalink social
    private static string LimpiarUrlSocial(string url)
    {
        url = HtmlEntity.DeEntitize(url).Trim();
        var i = url.IndexOf('?');
        return i > 0 ? url[..i] : url;
    }

    // Convierte una URL relativa o protocol-relative en absoluta respecto de la página
    private static string AbsolutaUrl(string url, string urlPagina)
    {
        url = url.Trim();
        if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return url;
        if (url.StartsWith("//")) return "https:" + url;
        return Uri.TryCreate(new Uri(urlPagina), url, out var abs) ? abs.ToString() : url;
    }

    // Extrae el cuerpo del artículo desde el JSON-LD schema.org (articleBody).
    // Es la fuente más limpia cuando existe (sin notas relacionadas ni tags).
    private static string? ExtraerArticleBodyJsonLd(HtmlDocument doc)
    {
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts == null) return null;

        foreach (var s in scripts)
        {
            string? body = null;
            try
            {
                using var json = JsonDocument.Parse(s.InnerText);
                body = BuscarArticleBody(json.RootElement);
            }
            catch { }

            if (!string.IsNullOrWhiteSpace(body))
            {
                var texto = HtmlEntity.DeEntitize(body);
                // El articleBody separa párrafos con secuencias de espacios/tabs → reconstruir
                var parrafos = Regex.Split(texto, @"[ \t ]{2,}|\r?\n+")
                    .Select(p => p.Trim())
                    .Where(p => p.Length > 0)
                    .ToList();
                return parrafos.Count > 1
                    ? string.Join("\n\n", parrafos)
                    : Regex.Replace(texto, @"\s+", " ").Trim();
            }
        }
        return null;
    }

    // Busca recursivamente la propiedad articleBody en el grafo JSON-LD
    private static string? BuscarArticleBody(JsonElement el)
    {
        if (el.ValueKind == JsonValueKind.Object)
        {
            if (el.TryGetProperty("articleBody", out var ab) && ab.ValueKind == JsonValueKind.String)
                return ab.GetString();
            foreach (var prop in el.EnumerateObject())
            {
                var r = BuscarArticleBody(prop.Value);
                if (!string.IsNullOrWhiteSpace(r)) return r;
            }
        }
        else if (el.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in el.EnumerateArray())
            {
                var r = BuscarArticleBody(item);
                if (!string.IsNullOrWhiteSpace(r)) return r;
            }
        }
        return null;
    }

    private static DateTime? ExtraerFechaPublicacion(HtmlDocument doc)
    {
        // 1. JSON-LD schema.org: datePublished / dateCreated
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts != null)
        {
            foreach (var s in scripts)
            {
                try
                {
                    using var json = JsonDocument.Parse(s.InnerText);
                    foreach (var prop in new[] { "datePublished", "dateCreated", "dateModified" })
                    {
                        if (json.RootElement.TryGetProperty(prop, out var dateEl)
                            && dateEl.GetString() is { } dateStr
                            && DateTime.TryParse(dateStr, out var dt))
                            return dt;
                    }
                }
                catch { }
            }
        }

        // 2. Meta tags
        var metaProps = new[]
        {
            ("property", "article:published_time"),
            ("name", "pubdate"),
            ("name", "date"),
            ("name", "DC.date"),
            ("property", "og:updated_time"),
        };
        foreach (var (attr, value) in metaProps)
        {
            var meta = doc.DocumentNode.SelectSingleNode($"//meta[@{attr}='{value}']");
            if (meta != null)
            {
                var content = meta.GetAttributeValue("content", "");
                if (DateTime.TryParse(content, out var dt)) return dt;
            }
        }

        // 3. <time datetime="...">
        var time = doc.DocumentNode.SelectSingleNode("//article//time[@datetime] | //time[@pubdate] | //time[@datetime]");
        if (time != null)
        {
            var dtAttr = time.GetAttributeValue("datetime", "");
            if (DateTime.TryParse(dtAttr, out var dt)) return dt;
        }

        return null;
    }

    // Extrae los párrafos de texto de un contenedor. Toma <p> y también <div>/<li>
    // "hoja" (sin hijos de bloque ni encabezados), para soportar sitios que arman el
    // cuerpo de la nota con <div> en lugar de <p> (o con markup malformado).
    private static List<string> ExtraerParrafos(HtmlNode contenedor)
    {
        var bloques = contenedor.SelectNodes(".//p | .//div | .//li");
        if (bloques == null) return new List<string>();

        var parrafos = new List<string>();
        foreach (var b in bloques)
        {
            // Saltar contenedores (con hijos de bloque o encabezados): así evitamos
            // duplicar texto y capturar títulos/menús; nos quedamos con las hojas de texto.
            if (b.SelectNodes("./p | ./div | ./ul | ./ol | ./table | ./section | ./article | ./figure | ./h1 | ./h2 | ./h3 | ./h4 | ./h5 | ./h6") != null)
                continue;
            var t = LimpiarTexto(b.InnerText);
            if (t.Length > 40 && !parrafos.Contains(t)) parrafos.Add(t);
        }
        return parrafos;
    }

    private static string? ExtraerFuente(string url)
    {
        try
        {
            var host = new Uri(url).Host; // ej: www.diariolavanguardia.com
            host = Regex.Replace(host, @"^www\.", ""); // diariolavanguardia.com
            // Quitar TLD: .com.ar, .net.ar, .org.ar, .ar, .com, .net, .org, etc.
            host = Regex.Replace(host, @"\.(com\.ar|net\.ar|org\.ar|gov\.ar|edu\.ar|ar|com|net|org|gov|edu|info|io|co)$", "", RegexOptions.IgnoreCase);
            return string.IsNullOrWhiteSpace(host) ? null : host;
        }
        catch { return null; }
    }

    private static string? ExtraerDireccion(HtmlDocument doc)
    {
        // 1. JSON-LD schema.org
        var scripts = doc.DocumentNode.SelectNodes("//script[@type='application/ld+json']");
        if (scripts != null)
        {
            foreach (var s in scripts)
            {
                try
                {
                    using var json = JsonDocument.Parse(s.InnerText);
                    var root = json.RootElement;
                    // Buscar en contentLocation o locationCreated
                    foreach (var prop in new[] { "contentLocation", "locationCreated", "location" })
                    {
                        if (root.TryGetProperty(prop, out var loc))
                        {
                            if (loc.ValueKind == JsonValueKind.String)
                                return loc.GetString();
                            if (loc.TryGetProperty("name", out var name))
                                return name.GetString();
                            if (loc.TryGetProperty("address", out var addr))
                            {
                                if (addr.ValueKind == JsonValueKind.String) return addr.GetString();
                                // streetAddress + addressLocality
                                var parts = new List<string>();
                                if (addr.TryGetProperty("streetAddress", out var street) && street.GetString() is { } st) parts.Add(st);
                                if (addr.TryGetProperty("addressLocality", out var city) && city.GetString() is { } ct) parts.Add(ct);
                                if (parts.Count > 0) return string.Join(", ", parts);
                            }
                        }
                    }
                }
                catch { /* JSON inválido */ }
            }
        }

        // 2. Meta tags
        var metaLoc = doc.DocumentNode
            .SelectSingleNode("//meta[@name='geo.placename' or @property='article:location']");
        if (metaLoc != null)
        {
            var content = metaLoc.GetAttributeValue("content", "");
            if (!string.IsNullOrWhiteSpace(content)) return content;
        }

        return null;
    }

    private static string LimpiarTexto(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto)) return "";
        texto = HtmlEntity.DeEntitize(texto);
        texto = Regex.Replace(texto, @"\s+", " ").Trim();
        return texto;
    }
}
