-- Complete tables and columns required by the current application model.

ALTER TABLE delegaciones
    ADD COLUMN DriveSheetNotas VARCHAR(200) NULL,
    ADD COLUMN DriveSheetSintesis VARCHAR(200) NULL;

CREATE TABLE traza_eventos (
    Id INT NOT NULL AUTO_INCREMENT,
    Fecha DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    Usuario VARCHAR(100) NOT NULL DEFAULT '',
    Rol VARCHAR(20) NULL,
    NotaId INT NULL,
    SesionId INT NULL,
    SintesisId INT NULL,
    Evento VARCHAR(40) NOT NULL DEFAULT '',
    Version INT NULL,
    Estado VARCHAR(30) NULL,
    Detalle VARCHAR(500) NULL,
    PRIMARY KEY (Id),
    INDEX idx_tz_nota (NotaId),
    INDEX idx_tz_sesion (SesionId),
    INDEX idx_tz_sintesis (SintesisId)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE scraping_reglas (
    Id INT NOT NULL AUTO_INCREMENT,
    Dominio VARCHAR(150) NOT NULL,
    XPathTitulo VARCHAR(300) NULL,
    XPathCuerpo VARCHAR(300) NULL,
    ClasesExcluir VARCHAR(500) NULL,
    PreferirJsonLd TINYINT(1) NOT NULL DEFAULT 1,
    Activo TINYINT(1) NOT NULL DEFAULT 1,
    Descripcion VARCHAR(200) NULL,
    FechaActualizacion DATETIME NULL,
    PRIMARY KEY (Id),
    UNIQUE INDEX uq_dominio (Dominio)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE caratulas_quiron (
    id INT NOT NULL,
    nombre VARCHAR(100) NOT NULL,
    consumable TINYINT(1) NOT NULL DEFAULT 0,
    calificable TINYINT(1) NOT NULL DEFAULT 0,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE modalidades_quiron (
    id INT NOT NULL,
    nombre VARCHAR(100) NOT NULL,
    caratula_id INT NOT NULL,
    PRIMARY KEY (id),
    INDEX idx_modalidad_caratula (caratula_id),
    CONSTRAINT fk_modalidad_caratula
        FOREIGN KEY (caratula_id) REFERENCES caratulas_quiron(id) ON DELETE CASCADE
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;
