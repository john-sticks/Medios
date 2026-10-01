ALTER TABLE notas_prensa_versiones
    ADD COLUMN FechaNoticia DATETIME NULL AFTER Sintesis,
    ADD INDEX idx_nv_fecha_noticia (FechaNoticia);
