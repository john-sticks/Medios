-- Agregar DelegacionId a sintesis para validar unicidad por delegacion/fecha/tipo
ALTER TABLE sintesis
    ADD COLUMN DelegacionId INT NULL AFTER OperadorGenera,
    ADD INDEX idx_sintesis_delegacion (DelegacionId),
    ADD CONSTRAINT fk_sintesis_delegacion
        FOREIGN KEY (DelegacionId) REFERENCES delegaciones(Id) ON DELETE SET NULL;

-- Ampliar columna Tipo para "Ampliacion Matutina" / "Ampliacion Vespertina"
ALTER TABLE sintesis MODIFY COLUMN Tipo VARCHAR(30) NOT NULL DEFAULT 'Vespertina';

-- Ampliar columna Turno en sesiones_prensa
ALTER TABLE sesiones_prensa MODIFY COLUMN Turno VARCHAR(30) NOT NULL DEFAULT 'Matutina';
