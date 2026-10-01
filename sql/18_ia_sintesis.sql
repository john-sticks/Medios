-- Columna síntesis generada por IA en versiones de notas
ALTER TABLE notas_prensa_versiones
    ADD COLUMN Sintesis TEXT NULL AFTER Texto;

-- Tabla de configuración general (clave/valor)
CREATE TABLE configuracion (
    clave VARCHAR(100) NOT NULL,
    valor TEXT NULL,
    PRIMARY KEY (clave)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_0900_ai_ci;

-- Seed: prompt por defecto para síntesis IA
INSERT INTO configuracion (clave, valor) VALUES
('ia_prompt', 'Sos un analista de seguridad de la Provincia de Buenos Aires. Analizá el siguiente texto de una nota periodística y generá una síntesis concisa (máximo 3 oraciones) que destaque los hechos principales, los actores involucrados y la zona geográfica. Respondé solo con la síntesis, sin introducción ni comentarios adicionales.'),
('ia_modelo', 'mistralai/mistral-7b-instruct:free');
