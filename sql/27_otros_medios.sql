-- "Otros medios": repercusión periodística de la misma noticia en otros portales, para
-- combinar con Fuente al mostrarla entre paréntesis en el PDF (ej. "(CLARIN, LANACION)").
ALTER TABLE notas_prensa_versiones ADD COLUMN OtrosMedios VARCHAR(300) NULL AFTER Fuente;
