-- Agregar columnas de ámbito a autorizaciones_usuario
USE medios;

ALTER TABLE autorizaciones_usuario
    ADD COLUMN delegacion_id INT NULL,
    ADD COLUMN ambito_id INT NULL,
    ADD CONSTRAINT fk_aut_delegacion FOREIGN KEY (delegacion_id) REFERENCES delegaciones(Id),
    ADD CONSTRAINT fk_aut_ambito FOREIGN KEY (ambito_id) REFERENCES AreaResponsabilidad(id);
