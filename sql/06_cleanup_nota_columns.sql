-- ─────────────────────────────────────────────────────────────
-- MIGRACIÓN 06 (OPCIONAL): Limpieza de columnas migradas
-- ─────────────────────────────────────────────────────────────
-- Ejecutar SOLO después de verificar que el nuevo código
-- (versiones de notas) funciona correctamente en producción.
-- ─────────────────────────────────────────────────────────────

USE medios;

ALTER TABLE notas_prensa
    DROP INDEX idx_nota_estado,
    DROP INDEX idx_nota_categoria,
    DROP COLUMN CategoriaId,
    DROP COLUMN PartidoId,
    DROP COLUMN LocalidadId,
    DROP COLUMN Titulo,
    DROP COLUMN Texto,
    DROP COLUMN Fuente,
    DROP COLUMN Link,
    DROP COLUMN EsRepercusion,
    DROP COLUMN EstadoRevision,
    DROP COLUMN MotivoDescarte,
    DROP COLUMN OperadorRevision,
    DROP COLUMN FechaRevision;
