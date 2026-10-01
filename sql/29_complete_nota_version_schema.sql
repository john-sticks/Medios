-- Complete notas_prensa_versiones so it matches the current NotaVersion model.
-- Apply after 28_complete_application_schema.sql.

USE medios;

ALTER TABLE notas_prensa_versiones
    ADD COLUMN AmbitoNota VARCHAR(20) NULL AFTER CategoriaId,
    ADD COLUMN ImagenUrl VARCHAR(500) NULL AFTER Link,
    ADD COLUMN VideoUrl VARCHAR(2000) NULL AFTER ImagenUrl,
    ADD COLUMN ImagenesUrls TEXT NULL AFTER VideoUrl,
    ADD COLUMN CaratulaQuironId INT NULL AFTER Longitud,
    ADD COLUMN ModalidadQuironId INT NULL AFTER CaratulaQuironId,
    ADD INDEX idx_nv_caratula_quiron (CaratulaQuironId),
    ADD INDEX idx_nv_modalidad_quiron (ModalidadQuironId),
    ADD CONSTRAINT fk_nv_caratula_quiron
        FOREIGN KEY (CaratulaQuironId) REFERENCES caratulas_quiron(id),
    ADD CONSTRAINT fk_nv_modalidad_quiron
        FOREIGN KEY (ModalidadQuironId) REFERENCES modalidades_quiron(id);
