using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using Medios.Entities;
using W = DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using DW = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using PIC = DocumentFormat.OpenXml.Drawing.Pictures;

namespace Medios.Services;

// El texto permanece editable. Comparte snapshots, agrupación y contenido con el PDF.
public static class SintesisWordDocument
{
    private const string Bookman = "Bookman Old Style";
    private const string Marron = "8B4513";
    private const int AnchoContenido = 9808; // A4 menos márgenes de 2,5 y 1,2 cm.

    public static byte[] Crear(Sintesis sintesis, IReadOnlyList<SintesisNota> incluidas,
        IReadOnlyDictionary<int, NotaLineaTiempo> lineas, string webRoot)
    {
        using var stream = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            var body = new W.Body();
            main.Document = new W.Document(body);
            var settings = main.AddNewPart<DocumentSettingsPart>();
            settings.Settings = new W.Settings(new W.UpdateFieldsOnOpen { Val = true });
            var styles = main.AddNewPart<StyleDefinitionsPart>();
            styles.Styles = new W.Styles(new W.DocDefaults(
                new W.RunPropertiesDefault(new W.RunProperties(
                    new W.RunFonts { Ascii = Bookman, HighAnsi = Bookman, ComplexScript = Bookman },
                    new W.FontSize { Val = "28" }, new W.Languages { Val = "es-AR" })),
                new W.ParagraphPropertiesDefault(new W.ParagraphPropertiesBaseStyle(
                    new W.SpacingBetweenLines { Before = "0", After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }))));

            var ampliacion = sintesis.Tipo.StartsWith("Ampliacion", StringComparison.OrdinalIgnoreCase);
            var tipoBanner = ampliacion ? sintesis.Tipo["Ampliacion".Length..].Trim() : sintesis.Tipo;
            var banner = Path.Combine(webRoot, "img", $"sintesis_{tipoBanner.ToLowerInvariant()}.png");
            if (File.Exists(banner)) body.Append(Portada(main, banner));
            else
            {
                body.Append(Parrafo("Policía de la Provincia de Buenos Aires", 8, fuente: "Arial", centrado: true));
                body.Append(Parrafo("Superintendencia de Inteligencia Criminal", 8, fuente: "Arial", centrado: true));
                body.Append(Parrafo("División Medios O.S.INT", 8, fuente: "Arial", centrado: true));
                body.Append(Parrafo($"SÍNTESIS DE PRENSA {sintesis.Tipo.ToUpperInvariant()}", 16, true, Marron, "Arial", true, antes: 80));
                body.Append(Parrafo("Diarios del Conurbano e Interior - Agencias de Noticias", 9, fuente: "Arial", centrado: true, cursiva: true));
                body.Append(Parrafo("— Edición Digital —", 9, fuente: "Arial", centrado: true, cursiva: true));
            }
            if (sintesis.EsFiltrada)
                body.Append(Parrafo($"SÍNTESIS FILTRADA · Origen #{sintesis.SintesisOrigenId}", 10, true, centrado: true));
            var fecha = sintesis.Fecha.ToString("d 'de' MMMM 'de' yyyy", CultureInfo.GetCultureInfo("es-AR"));
            body.Append(Parrafo(fecha + (ampliacion ? " - AMPLIACIÓN" : ""), 20, true, centrado: true, antes: 200));
            body.Append(Parrafo("CONTENIDO", 16, true, Marron, "Arial", antes: 720, despues: 400));

            var grupos = SintesisPdfModel.Agrupar(incluidas);
            for (var i = 0; i < grupos.Count; i++)
            {
                var indice = Parrafo(grupos[i].Titulo, 10, fuente: "Arial", antes: 40);
                indice.ParagraphProperties!.InsertBefore(new W.Tabs(new W.TabStop
                    { Val = W.TabStopValues.Right, Leader = W.TabStopLeaderCharValues.Dot, Position = AnchoContenido }), indice.ParagraphProperties.GetFirstChild<W.SpacingBetweenLines>());
                indice.Append(new W.Run(new W.TabChar()));
                indice.Append(new W.SimpleField(new W.Run(new W.RunProperties(new W.FontSize { Val = "20" }), new W.Text("2")))
                    { Instruction = $" PAGEREF categoria_{i} \\h ", Dirty = true });
                body.Append(indice);
            }
            body.Append(new W.Paragraph(new W.Run(new W.Break { Type = W.BreakValues.Page })));

            for (var i = 0; i < grupos.Count; i++)
            {
                var grupo = grupos[i];
                var encabezado = Parrafo(grupo.Titulo, 14, true, Marron, antes: 200, despues: 200, seguir: true);
                encabezado.InsertAfter(new W.BookmarkStart { Id = i.ToString(), Name = $"categoria_{i}" }, encabezado.ParagraphProperties);
                encabezado.Append(new W.BookmarkEnd { Id = i.ToString() });
                body.Append(encabezado);
                foreach (var categoria in grupo.Categorias)
                {
                    if (categoria.Titulo != null)
                        body.Append(Parrafo(categoria.Titulo, 12, true, Marron, antes: 120, seguir: true));
                    foreach (var snapshot in categoria.Notas)
                    {
                        lineas.TryGetValue(snapshot.NotaId, out var linea);
                        var tieneHistorial = linea?.Actualizaciones.Count > 0;
                        var nota = (tieneHistorial ? linea!.Ancla : null) ?? snapshot;
                        var geo = grupo.SoloLocalidad ? SintesisPdfModel.LocalidadDentroPartido(nota) : Geografia(nota);
                        if (geo.Length > 0) body.Append(Parrafo(geo, 14, true, antes: 80, seguir: true));
                        body.Append(Parrafo(nota.Titulo.TrimEnd().TrimEnd('.').ToUpperInvariant(), 14, true, seguir: true));
                        body.Append(Parrafo(SintesisPdfModel.CuerpoConFuente(nota), 14, justificar: true));
                        var link = SintesisPdfModel.LinkVisible(nota);
                        if (!string.IsNullOrWhiteSpace(link)) body.Append(Parrafo(link, 12, color: "2196F3", despues: 200));
                        if (tieneHistorial)
                            foreach (var act in linea!.Actualizaciones)
                            {
                                var etiqueta = $"ACTUALIZACIÓN · {act.Fecha:dd/MM/yyyy}" + (string.IsNullOrEmpty(act.Turno) ? "" : $" · {act.Turno}");
                                body.Append(Parrafo(etiqueta, 8, true, Marron, "Arial", antes: 60, seguir: true));
                                body.Append(Parrafo(act.ResumenCambio ?? "", 9, fuente: "Arial"));
                            }
                    }
                    // El borde pertenece al último párrafo: un párrafo vacío adicional
                    // puede saltar solo a una página nueva al alcanzar el margen inferior.
                    var ultimo = body.Elements<W.Paragraph>().Last();
                    var propiedades = ultimo.ParagraphProperties!;
                    propiedades.InsertBefore(new W.ParagraphBorders(new W.BottomBorder
                        { Val = W.BorderValues.Single, Color = "DDDDDD", Size = 4, Space = 6 }),
                        propiedades.GetFirstChild<W.SpacingBetweenLines>());
                    propiedades.GetFirstChild<W.SpacingBetweenLines>()!.After = "240";
                }
            }
            if (string.Equals(sintesis.Tipo, "Vespertina", StringComparison.OrdinalIgnoreCase))
                body.Append(Parrafo("Nota: La información suministrada por las agencias de noticias son anticipos que refieren " +
                    "a posibles temas a tratar en el día de mañana, en los medios periodísticos nacionales o locales.",
                    8, color: "616161", fuente: "Arial", antes: 160, cursiva: true));

            var footer = main.AddNewPart<FooterPart>();
            var pie = Parrafo("División Medios O.S.INT — ", 8, color: "757575", fuente: "Arial", centrado: true);
            pie.Append(new W.SimpleField(new W.Run(new W.RunProperties(new W.RunFonts { Ascii = "Arial", HighAnsi = "Arial" }, new W.FontSize { Val = "16" }), new W.Text("1"))) { Instruction = " PAGE " });
            pie.Append(new W.Run(new W.RunProperties(new W.RunFonts { Ascii = "Arial", HighAnsi = "Arial" }, new W.FontSize { Val = "16" }),
                new W.Text(" / ") { Space = SpaceProcessingModeValues.Preserve }));
            pie.Append(new W.SimpleField(new W.Run(new W.RunProperties(new W.RunFonts { Ascii = "Arial", HighAnsi = "Arial" }, new W.FontSize { Val = "16" }), new W.Text("1"))) { Instruction = " NUMPAGES " });
            footer.Footer = new W.Footer(pie);
            body.Append(new W.SectionProperties(
                new W.FooterReference { Type = W.HeaderFooterValues.Default, Id = main.GetIdOfPart(footer) },
                new W.PageSize { Width = 11906, Height = 16838 },
                new W.PageMargin { Top = 737, Right = 680, Bottom = 680, Left = 1418, Header = 0, Footer = 200, Gutter = 0 }));
            main.Document.Save();
        }
        return stream.ToArray();
    }

    private static W.Paragraph Parrafo(string texto, int tamano, bool negrita = false, string color = "000000",
        string fuente = Bookman, bool centrado = false, int antes = 0, int despues = 0,
        bool seguir = false, bool cursiva = false, bool justificar = false)
    {
        var props = new W.ParagraphProperties();
        if (seguir) props.Append(new W.KeepNext());
        props.Append(new W.WidowControl());
        props.Append(new W.SpacingBetweenLines { Before = antes.ToString(), After = despues.ToString(), Line = "240", LineRule = W.LineSpacingRuleValues.Auto });
        var alineacion = W.JustificationValues.Left;
        if (centrado) alineacion = W.JustificationValues.Center;
        else if (justificar) alineacion = W.JustificationValues.Both;
        props.Append(new W.Justification { Val = alineacion });
        var estilo = new W.RunProperties(new W.RunFonts { Ascii = fuente, HighAnsi = fuente, ComplexScript = fuente });
        if (negrita) estilo.Append(new W.Bold());
        if (cursiva) estilo.Append(new W.Italic());
        estilo.Append(new W.Color { Val = color }, new W.FontSize { Val = (tamano * 2).ToString() });
        var run = new W.Run(estilo);
        var lineas = texto.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        for (var i = 0; i < lineas.Length; i++)
        {
            if (i > 0) run.Append(new W.Break());
            run.Append(new W.Text(lineas[i]) { Space = SpaceProcessingModeValues.Preserve });
        }
        return new W.Paragraph(props, run);
    }

    private static string Geografia(NotaVersion nota)
    {
        if (nota.Partido == null) return "";
        var localidad = SintesisPdfModel.LocalidadDentroPartido(nota);
        return nota.Partido.Nombre.ToUpperInvariant() + (localidad.Length > 0 ? " - " + localidad : "");
    }

    private static W.Paragraph Portada(MainDocumentPart main, string path)
    {
        var part = main.AddImagePart(ImagePartType.Png);
        using (var file = File.OpenRead(path)) part.FeedData(file);
        var info = SixLabors.ImageSharp.Image.Identify(path);
        var width = AnchoContenido * 635L;
        var height = width * info.Height / info.Width;
        var graphic = new A.Graphic(new A.GraphicData(new PIC.Picture(
            new PIC.NonVisualPictureProperties(new PIC.NonVisualDrawingProperties { Id = 0, Name = "Portada" }, new PIC.NonVisualPictureDrawingProperties()),
            new PIC.BlipFill(new A.Blip { Embed = main.GetIdOfPart(part) }, new A.Stretch(new A.FillRectangle())),
            new PIC.ShapeProperties(new A.Transform2D(new A.Offset { X = 0, Y = 0 }, new A.Extents { Cx = width, Cy = height }),
                new A.PresetGeometry(new A.AdjustValueList()) { Preset = A.ShapeTypeValues.Rectangle })))
            { Uri = "http://schemas.openxmlformats.org/drawingml/2006/picture" });
        var inline = new DW.Inline(new DW.Extent { Cx = width, Cy = height },
            new DW.DocProperties { Id = 1, Name = "Portada de síntesis de prensa" }, graphic)
            { DistanceFromTop = 0, DistanceFromBottom = 0, DistanceFromLeft = 0, DistanceFromRight = 0 };
        return new W.Paragraph(new W.Run(new W.Drawing(inline)));
    }
}
