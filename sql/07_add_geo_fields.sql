-- ─────────────────────────────────────────────────────────────
-- MIGRACIÓN 07: Campos de geolocalización en notas_prensa_versiones
-- ─────────────────────────────────────────────────────────────

USE medios;

ALTER TABLE notas_prensa_versiones
    ADD COLUMN Direccion VARCHAR(500) NULL,
    ADD COLUMN Latitud   DOUBLE       NULL,
    ADD COLUMN Longitud  DOUBLE       NULL;
