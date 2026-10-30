-- Ejecutar antes de desplegar el módulo. Solo crea las dos tablas nuevas.
CREATE TABLE IF NOT EXISTS grupos_correo (
  Id INT NOT NULL AUTO_INCREMENT,
  Nombre VARCHAR(120) NOT NULL,
  Activo TINYINT(1) NOT NULL DEFAULT 1,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_grupos_correo_Nombre (Nombre)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS contactos_correo (
  Id INT NOT NULL AUTO_INCREMENT,
  GrupoCorreoId INT NOT NULL,
  Nombre VARCHAR(100) NOT NULL,
  Apellido VARCHAR(100) NOT NULL,
  Email VARCHAR(254) NOT NULL,
  PRIMARY KEY (Id),
  UNIQUE KEY UX_contactos_correo_Grupo_Email (GrupoCorreoId, Email),
  CONSTRAINT FK_contactos_correo_grupo FOREIGN KEY (GrupoCorreoId)
    REFERENCES grupos_correo (Id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
