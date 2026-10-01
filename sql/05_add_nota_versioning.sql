-- ─────────────────────────────────────────────────────────────
-- MIGRACIÓN 05: Modelo de versiones para notas de prensa
-- ─────────────────────────────────────────────────────────────
-- Ejecutar sobre la base "medios".
-- IMPORTANTE: Aplicar ANTES de desplegar el nuevo código.
-- ─────────────────────────────────────────────────────────────

USE medios;

-- ─── FASE 1: Crear tabla de versiones ────────────────────────

CREATE TABLE IF NOT EXISTS notas_prensa_versiones (
    Id               INT           NOT NULL AUTO_INCREMENT,
    NotaId           INT           NOT NULL,
    Version          INT           NOT NULL DEFAULT 1,
    ParentVersionId  INT           NULL,
    CategoriaId      INT           NOT NULL,
    PartidoId        INT           NULL,
    LocalidadId      INT           NULL,
    Titulo           VARCHAR(500)  NOT NULL DEFAULT '',
    Texto            TEXT          NOT NULL,
    Fuente           VARCHAR(200)  NULL,
    Link             VARCHAR(2000) NULL,
    EsRepercusion    TINYINT(1)    NOT NULL DEFAULT 0,
    EstadoRevision   VARCHAR(20)   NOT NULL DEFAULT 'Pendiente',
    MotivoDescarte   VARCHAR(500)  NULL,
    OperadorRevision VARCHAR(100)  NULL,
    FechaVersion     DATETIME      NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Usuario          VARCHAR(100)  NOT NULL DEFAULT '',
    EsActual         TINYINT(1)    NOT NULL DEFAULT 1,
    TipoEvento       VARCHAR(30)   NOT NULL DEFAULT 'Creacion',
    ResumenCambio    VARCHAR(500)  NULL,
    PRIMARY KEY (Id),
    INDEX idx_nv_nota        (NotaId),
    INDEX idx_nv_nota_actual  (NotaId, EsActual),
    INDEX idx_nv_estado       (EstadoRevision),
    FOREIGN KEY (NotaId)          REFERENCES notas_prensa(Id)            ON DELETE CASCADE,
    FOREIGN KEY (ParentVersionId) REFERENCES notas_prensa_versiones(Id)  ON DELETE RESTRICT,
    FOREIGN KEY (CategoriaId)     REFERENCES categorias_noticia(Id),
    FOREIGN KEY (PartidoId)       REFERENCES Partidos(idPartido),
    FOREIGN KEY (LocalidadId)     REFERENCES localidades(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─── FASE 2: Agregar VersionActualId a notas_prensa ──────────

ALTER TABLE notas_prensa
    ADD COLUMN VersionActualId INT NULL,
    ADD INDEX idx_nota_version_actual (VersionActualId);

-- ─── FASE 3: Migrar datos — crear v1 para cada nota ──────────

INSERT INTO notas_prensa_versiones
    (NotaId, Version, ParentVersionId,
     CategoriaId, PartidoId, LocalidadId,
     Titulo, Texto, Fuente, Link, EsRepercusion,
     EstadoRevision, MotivoDescarte, OperadorRevision,
     FechaVersion, Usuario, EsActual, TipoEvento, ResumenCambio)
SELECT
    n.Id,
    1,
    NULL,
    n.CategoriaId,
    n.PartidoId,
    n.LocalidadId,
    n.Titulo,
    n.Texto,
    n.Fuente,
    n.Link,
    n.EsRepercusion,
    n.EstadoRevision,
    n.MotivoDescarte,
    n.OperadorRevision,
    COALESCE(n.FechaRevision, n.FechaRegistro),
    COALESCE(n.OperadorRevision, 'migración'),
    1,
    'Creacion',
    NULL
FROM notas_prensa n
WHERE NOT EXISTS (
    SELECT 1 FROM notas_prensa_versiones v WHERE v.NotaId = n.Id
);

-- ─── FASE 4: Apuntar VersionActualId a la v1 de cada nota ────

UPDATE notas_prensa n
INNER JOIN notas_prensa_versiones v ON v.NotaId = n.Id AND v.Version = 1
SET n.VersionActualId = v.Id
WHERE n.VersionActualId IS NULL;

-- ─── FASE 5: Hacer VersionActualId NOT NULL + FK ─────────────

ALTER TABLE notas_prensa
    MODIFY COLUMN VersionActualId INT NOT NULL;

ALTER TABLE notas_prensa
    ADD CONSTRAINT fk_nota_version_actual
        FOREIGN KEY (VersionActualId) REFERENCES notas_prensa_versiones(Id)
        ON DELETE RESTRICT;

-- ─── FASE 6: Agregar NotaVersionId a sintesis_notas ──────────

ALTER TABLE sintesis_notas
    ADD COLUMN NotaVersionId INT NULL,
    ADD INDEX idx_sn_version (NotaVersionId);

-- Apuntar cada sintesis_nota a la v1 de su nota
UPDATE sintesis_notas sn
INNER JOIN notas_prensa_versiones v ON v.NotaId = sn.NotaPrensaId AND v.Version = 1
SET sn.NotaVersionId = v.Id
WHERE sn.NotaVersionId IS NULL;

-- Hacer NOT NULL + FK
ALTER TABLE sintesis_notas
    MODIFY COLUMN NotaVersionId INT NOT NULL;

ALTER TABLE sintesis_notas
    ADD CONSTRAINT fk_sn_version
        FOREIGN KEY (NotaVersionId) REFERENCES notas_prensa_versiones(Id)
        ON DELETE RESTRICT;

-- ─────────────────────────────────────────────────────────────
-- FIN DE LA MIGRACIÓN
-- ─────────────────────────────────────────────────────────────
-- Verificación rápida post-migración:
--   SELECT COUNT(*) FROM notas_prensa WHERE VersionActualId IS NULL;   -- debe ser 0
--   SELECT COUNT(*) FROM sintesis_notas WHERE NotaVersionId IS NULL;   -- debe ser 0
--   SELECT COUNT(*) FROM notas_prensa_versiones;                       -- debe = COUNT(*) FROM notas_prensa
-- ─────────────────────────────────────────────────────────────
