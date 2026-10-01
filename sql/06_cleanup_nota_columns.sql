-- ─────────────────────────────────────────────────────────────
-- MIGRACIÓN 06 (OPCIONAL): Limpieza de columnas migradas
-- ─────────────────────────────────────────────────────────────
-- Ejecutar SOLO después de verificar que el nuevo código
-- (versiones de notas) funciona correctamente en producción.
-- ─────────────────────────────────────────────────────────────

USE medios;

ALTER TABLE notas_prensa
    DROP INDEX  IF EXISTS idx_nota_estado,
    DROP INDEX  IF EXISTS idx_nota_categoria,
    DROP COLUMN IF EXISTS CategoriaId,
    DROP COLUMN IF EXISTS PartidoId,
    DROP COLUMN IF EXISTS LocalidadId,
    DROP COLUMN IF EXISTS Titulo,
    DROP COLUMN IF EXISTS Texto,
    DROP COLUMN IF EXISTS Fuente,
    DROP COLUMN IF EXISTS Link,
    DROP COLUMN IF EXISTS EsRepercusion,
    DROP COLUMN IF EXISTS EstadoRevision,
    DROP COLUMN IF EXISTS MotivoDescarte,
    DROP COLUMN IF EXISTS OperadorRevision,
    DROP COLUMN IF EXISTS FechaRevision;
