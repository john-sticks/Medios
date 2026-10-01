-- ─────────────────────────────────────────────────────────────
-- MIGRACIÓN 09: Columna NotaOrigenId en notas_prensa
-- Permite rastrear si una nota en borrador es copia de otra
-- ─────────────────────────────────────────────────────────────

USE medios;

ALTER TABLE notas_prensa
    ADD COLUMN NotaOrigenId INT NULL,
    ADD INDEX idx_nota_origen (NotaOrigenId),
    ADD CONSTRAINT fk_nota_origen
        FOREIGN KEY (NotaOrigenId) REFERENCES notas_prensa(id) ON DELETE SET NULL;
