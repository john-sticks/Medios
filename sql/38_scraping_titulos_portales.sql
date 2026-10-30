-- Reglas de título para los 12 portales diagnosticados (probar primero en test).
-- No cambia noticias, versiones ni las reglas de cuerpo/clases ya configuradas.
START TRANSACTION;
INSERT INTO scraping_reglas (Dominio,XPathTitulo,PreferirJsonLd,Activo,Descripcion,FechaActualizacion)
VALUES
('bynoticias.com.ar','//h1[contains(@class,''entry-title'')]',1,1,'Título de la noticia; excluye By Noticias.',NOW()),
('campananoticias.com','//*[@id=''noticia'']//*[contains(@class,''titulo_noticia'')]',1,1,'Título de noticia; excluye el prefijo Campana Noticias.',NOW()),
('delitometro.com.ar','//h1[contains(@class,''cm-entry-title'')]',1,1,'Encabezado de la noticia; excluye El Delitometro.',NOW()),
('diarioelinformante.com.ar','//*[@data-id=''b7fd84e'']//*[contains(@class,''jet-listing-dynamic-field__content'')]',1,1,'Título dinámico del artículo; excluye El Informante.',NOW()),
('diariolavanguardia.com','//div[@class=''cuadro'']/h2',1,1,'Título de la noticia; excluye Noticias y Diario La Vanguardia.',NOW()),
('eldia.com','//h1[contains(@class,''articulo__titulo'')]',1,1,'Encabezado del artículo; excluye El Dia.',NOW()),
('horadeinformarse.com.ar','//h1[contains(@class,''entry-title'')]',1,1,'Título del artículo; excluye Hora de Informarse. Verificado desde test.',NOW()),
('infoban.com.ar','//h1[contains(@class,''entry-title'')]',1,1,'Título del artículo; excluye el prefijo InfoBAN.',NOW()),
('inforegion.com.ar','//h1[contains(@class,''vw-post-title'')]',1,1,'Título del artículo; excluye InfoRegión.',NOW()),
('lujanhoy.com.ar','//h1[contains(@class,''entry-title'')]',1,1,'Título del artículo; excluye Lujan Hoy.',NOW()),
('minutog.com','//*[@id=''destacada0'']/h3',1,1,'Título de noticia; excluye sección, Minuto G y Noticias de Villa Gesell.',NOW()),
('opinandosannicolas.ar','//h1[contains(@class,''entry-title'')]',1,1,'Encabezado editorial; excluye Opinando San Nicolás.',NOW())
ON DUPLICATE KEY UPDATE
 XPathTitulo=VALUES(XPathTitulo), Activo=1,
 Descripcion=VALUES(Descripcion), FechaActualizacion=NOW();
COMMIT;
