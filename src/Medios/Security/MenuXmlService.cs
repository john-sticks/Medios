using System.Xml.Linq;
using System.Security.Claims;

namespace Medios.Security
{
    public class MenuXmlService
    {
        private readonly IWebHostEnvironment _env;
        // Caché por nivel + fecha de modificación del XML: si el archivo cambia (deploy),
        // se recarga automáticamente sin depender de reiniciar el proceso.
        private static readonly Dictionary<string, (DateTime Mtime, XDocument Doc)> _cache = new();

        public MenuXmlService(IWebHostEnvironment env)
        {
            _env = env;
        }

        public List<MenuItem> GetMenu(ClaimsPrincipal user)
        {
            var doc = GetXml(user);
            var navigation = doc.Root?.Element("navigation");
            if (navigation == null) return new List<MenuItem>();
            return BuildItems(navigation.Elements());
        }

        // Construye los ítems del menú soportando grupos anidados (sub-menús)
        private List<MenuItem> BuildItems(IEnumerable<XElement> elements)
        {
            var result = new List<MenuItem>();
            foreach (var el in elements)
            {
                if (el.Name == "page")
                {
                    var url = MapUrl(el.Attribute("href")?.Value);
                    if (!string.IsNullOrWhiteSpace(url))
                        result.Add(new MenuItem
                        {
                            Text = el.Attribute("title")?.Value,
                            Icon = el.Attribute("icon")?.Value,
                            Url = url
                        });
                }
                else if (el.Name == "group")
                {
                    var children = BuildItems(el.Elements());
                    if (children.Count > 0)
                        result.Add(new MenuItem
                        {
                            Text = el.Attribute("title")?.Value,
                            Icon = el.Attribute("icon")?.Value,
                            Children = children
                        });
                }
            }
            return result;
        }

        public List<string> GetAllowedUrls(ClaimsPrincipal user)
        {
            var doc = GetXml(user);
            return doc.Descendants("page")
                .Select(x => MapUrl(x.Attribute("href")?.Value))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList()!;
        }

        public List<string> GetPermissions(ClaimsPrincipal user)
        {
            var doc = GetXml(user);
            return doc.Descendants("permission")
                .Select(x => x.Attribute("name")?.Value?.ToUpper())
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct()
                .ToList()!;
        }

        private XDocument GetXml(ClaimsPrincipal user)
        {
            var rol = user.FindFirst(ClaimTypes.Role)?.Value;
            var nivel = MapRolToNivel(rol);

            var file = $"permisos_{nivel}.xml";
            var path = Path.Combine(_env.ContentRootPath, "Config", "Permisos", file);

            if (!File.Exists(path))
                return new XDocument();

            var mtime = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(nivel, out var cached) && cached.Mtime == mtime)
                return cached.Doc;

            var doc = XDocument.Load(path);
            _cache[nivel] = (mtime, doc);
            return doc;
        }

        private string MapRolToNivel(string? rol)
        {
            if (string.IsNullOrWhiteSpace(rol)) return "30";

            return rol.ToUpper().Trim() switch
            {
                "DESARROLLADOR" => "10",
                "MEDIOS" => "15",
                "DELEGACION" => "30",
                _ => "30"
            };
        }

        private string? MapUrl(string? href)
        {
            if (string.IsNullOrWhiteSpace(href)) return "";
            return href.Replace("~/", "/").ToLower().TrimEnd('/');
        }
    }

    public class MenuItem
    {
        public string? Text { get; set; }
        public string? Icon { get; set; }
        public string? Url { get; set; }
        public List<MenuItem> Children { get; set; } = new();
        public bool IsGroup => Children.Count > 0;

        // URLs de todas las hojas (recursivo) — para resaltar el grupo activo
        public IEnumerable<string> LeafUrls()
        {
            if (!IsGroup && !string.IsNullOrEmpty(Url)) yield return Url!;
            foreach (var c in Children)
                foreach (var u in c.LeafUrls())
                    yield return u;
        }
    }
}
