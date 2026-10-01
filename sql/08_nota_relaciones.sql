-- Migración 08: tabla de relaciones entre notas
CREATE TABLE nota_relaciones (
  id                  INT AUTO_INCREMENT PRIMARY KEY,
  nota_id             INT NOT NULL,
  nota_relacionada_id INT NOT NULL,
  fecha_relacion      DATETIME NOT NULL DEFAULT NOW(),
  usuario             VARCHAR(100) NOT NULL DEFAULT '',
  UNIQUE KEY uq_relacion (nota_id, nota_relacionada_id),
  KEY idx_relacion_nota       (nota_id),
  KEY idx_relacion_relacionada (nota_relacionada_id),
  CONSTRAINT fk_relacion_nota       FOREIGN KEY (nota_id)             REFERENCES notas_prensa(id) ON DELETE CASCADE,
  CONSTRAINT fk_relacion_relacionada FOREIGN KEY (nota_relacionada_id) REFERENCES notas_prensa(id) ON DELETE CASCADE
);
