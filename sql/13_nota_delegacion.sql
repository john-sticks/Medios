-- Agregar columna DelegacionId a notas_prensa para notas libres (SesionPrensaId = NULL)
ALTER TABLE notas_prensa
    ADD COLUMN DelegacionId INT NULL AFTER SesionPrensaId,
    ADD INDEX idx_nota_delegacion (DelegacionId),
    ADD CONSTRAINT fk_nota_delegacion
        FOREIGN KEY (DelegacionId) REFERENCES delegaciones(Id)
        ON DELETE SET NULL;
