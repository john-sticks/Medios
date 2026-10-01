-- Visibilidad in-app por delegación al informar una síntesis consolidada
ALTER TABLE sintesis
    ADD COLUMN CanalTodasDelegaciones TINYINT(1) NOT NULL DEFAULT 0 AFTER CanalTodosRoles;

CREATE TABLE IF NOT EXISTS sintesis_delegacion_destino (
    Id INT NOT NULL AUTO_INCREMENT,
    SintesisId INT NOT NULL,
    DelegacionId INT NOT NULL,
    PRIMARY KEY (Id),
    UNIQUE INDEX idx_sdd_unico (SintesisId, DelegacionId),
    INDEX idx_sdd_sintesis (SintesisId),
    INDEX idx_sdd_delegacion (DelegacionId),
    FOREIGN KEY (SintesisId) REFERENCES sintesis(Id) ON DELETE CASCADE,
    FOREIGN KEY (DelegacionId) REFERENCES delegaciones(Id) ON DELETE RESTRICT
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
