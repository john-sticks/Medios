-- Regla acotada: no cambia notas ya guardadas ni otros portales.
INSERT INTO scraping_reglas
    (Dominio, XPathTitulo, XPathCuerpo, PreferirJsonLd, Activo, Descripcion, FechaActualizacion)
VALUES
    ('rumoresdepehuajo.com.ar', '//h1',
     '//div[contains(concat('' '',normalize-space(@class),'' ''),'' articulo-cuerpo '')]',
     1, 1, 'Cuerpo principal de la noticia; excluye recomendaciones y últimas noticias.', NOW())
ON DUPLICATE KEY UPDATE
    XPathTitulo=VALUES(XPathTitulo), XPathCuerpo=VALUES(XPathCuerpo),
    PreferirJsonLd=VALUES(PreferirJsonLd), Activo=VALUES(Activo),
    Descripcion=VALUES(Descripcion), FechaActualizacion=VALUES(FechaActualizacion);
