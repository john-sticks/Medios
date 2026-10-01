-- Complete the synthesis lifecycle fields required by the current application model.
-- Apply after 29_complete_nota_version_schema.sql.

USE medios;

ALTER TABLE sintesis
    ADD COLUMN CanalMails VARCHAR(1000) NULL AFTER CanalTodasDelegaciones,
    ADD COLUMN FechaInformada DATETIME NULL AFTER CanalMails,
    ADD COLUMN TextoImportado TEXT NULL AFTER FechaInformada;

ALTER TABLE sesiones_prensa
    ADD COLUMN SintesisConsolidadaId INT NULL AFTER UsuarioCarga,
    ADD INDEX idx_sesion_sintesis_consolidada (SintesisConsolidadaId),
    ADD CONSTRAINT fk_sesion_sintesis_consolidada
        FOREIGN KEY (SintesisConsolidadaId) REFERENCES sintesis(Id)
        ON DELETE SET NULL;
