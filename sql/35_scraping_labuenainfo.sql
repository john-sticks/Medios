-- Regla validada en test: extraer el cuerpo de la noticia, sin Últimas Noticias ni Leé también.
INSERT INTO scraping_reglas
    (Dominio, XPathTitulo, XPathCuerpo, ClasesExcluir, PreferirJsonLd, Activo, Descripcion, FechaActualizacion)
VALUES
    ('labuenainfo.com', '//h1', '//div[contains(@class,''news-page-content_body__'')]',
     'dataContainer_container__,newsPage_lee-tambien__', 0, 1,
     'Cuerpo principal; excluye tarjetas de últimas noticias y Leé también.', NOW())
ON DUPLICATE KEY UPDATE
    XPathTitulo = VALUES(XPathTitulo), XPathCuerpo = VALUES(XPathCuerpo),
    ClasesExcluir = VALUES(ClasesExcluir), PreferirJsonLd = VALUES(PreferirJsonLd),
    Activo = VALUES(Activo), Descripcion = VALUES(Descripcion), FechaActualizacion = NOW();
