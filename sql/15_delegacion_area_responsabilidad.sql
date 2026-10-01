ALTER TABLE delegaciones
    ADD COLUMN AreaResponsabilidadId INT NULL AFTER Jurisdiccion,
    ADD INDEX idx_delegacion_area (AreaResponsabilidadId),
    ADD CONSTRAINT fk_delegacion_area FOREIGN KEY (AreaResponsabilidadId)
        REFERENCES AreaResponsabilidad(id) ON DELETE SET NULL;
