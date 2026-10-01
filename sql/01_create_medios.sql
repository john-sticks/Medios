-- Script de creación de la base de datos Medios
-- Ejecutar contra la instancia MySQL del ambiente de destino.

CREATE DATABASE IF NOT EXISTS medios
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE medios;

-- ─────────────────────────────────────────────────────────────
-- INFRAESTRUCTURA (misma estructura que Zeus/SBInteligencia)
-- ─────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS AreaResponsabilidad (
    id INT NOT NULL AUTO_INCREMENT,
    AmbitoResponsabilidad VARCHAR(50) NOT NULL,
    PRIMARY KEY (id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS Partidos (
    idPartido INT NOT NULL AUTO_INCREMENT,
    Nombre VARCHAR(200) NOT NULL,
    idAreaResponsabilidad INT NOT NULL,
    PRIMARY KEY (idPartido),
    INDEX idx_area (idAreaResponsabilidad),
    FOREIGN KEY (idAreaResponsabilidad) REFERENCES AreaResponsabilidad(id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS auditoria (
    id INT NOT NULL AUTO_INCREMENT,
    fecha DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    usuario VARCHAR(100) NOT NULL DEFAULT '',
    ip VARCHAR(50) NOT NULL DEFAULT '',
    tabla VARCHAR(50) NOT NULL DEFAULT '',
    tipo VARCHAR(10) NOT NULL DEFAULT '',
    accion VARCHAR(30) NOT NULL DEFAULT '',
    detalle JSON NULL,
    PRIMARY KEY (id),
    INDEX idx_usuario (usuario),
    INDEX idx_fecha (fecha),
    INDEX idx_tabla_accion (tabla, accion)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS autorizaciones_usuario (
    id INT NOT NULL AUTO_INCREMENT,
    usuario VARCHAR(100) NOT NULL DEFAULT '',
    nombre VARCHAR(200) NOT NULL DEFAULT '',
    email VARCHAR(200) NULL,
    destino VARCHAR(300) NULL,
    jerarquia VARCHAR(100) NULL,
    rol VARCHAR(20) NULL,
    legajo VARCHAR(50) NULL,
    telefono VARCHAR(50) NULL,
    codigo VARCHAR(6) NOT NULL DEFAULT '',
    estado VARCHAR(20) NOT NULL DEFAULT 'pendiente',
    activo TINYINT(1) NOT NULL DEFAULT 1,
    fecha_solicitud DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    fecha_resolucion DATETIME NULL,
    aprobado_por VARCHAR(100) NULL,
    PRIMARY KEY (id),
    INDEX idx_usuario (usuario),
    INDEX idx_estado (estado)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─────────────────────────────────────────────────────────────
-- CATÁLOGOS
-- ─────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS categorias_noticia (
    Id INT NOT NULL AUTO_INCREMENT,
    Nombre VARCHAR(100) NOT NULL,
    Orden INT NOT NULL DEFAULT 0,
    Activa TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS localidades (
    Id INT NOT NULL AUTO_INCREMENT,
    Nombre VARCHAR(200) NOT NULL,
    PartidoId INT NOT NULL,
    PRIMARY KEY (Id),
    INDEX idx_partido (PartidoId),
    FOREIGN KEY (PartidoId) REFERENCES Partidos(idPartido)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS delegaciones (
    Id INT NOT NULL AUTO_INCREMENT,
    Nombre VARCHAR(200) NOT NULL,
    Jurisdiccion VARCHAR(200) NULL,
    PartidoId INT NULL,
    Email VARCHAR(200) NULL,
    UsuarioCerberus VARCHAR(100) NULL,
    Activa TINYINT(1) NOT NULL DEFAULT 1,
    PRIMARY KEY (Id),
    FOREIGN KEY (PartidoId) REFERENCES Partidos(idPartido)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─────────────────────────────────────────────────────────────
-- DOMINIO PRINCIPAL
-- ─────────────────────────────────────────────────────────────

CREATE TABLE IF NOT EXISTS sesiones_prensa (
    Id INT NOT NULL AUTO_INCREMENT,
    DelegacionId INT NULL,
    Fecha DATE NOT NULL,
    Turno VARCHAR(20) NOT NULL DEFAULT 'Vespertina',
    Estado VARCHAR(20) NOT NULL DEFAULT 'Borrador',
    FechaCreacion DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FechaRemision DATETIME NULL,
    UsuarioCarga VARCHAR(100) NOT NULL DEFAULT '',
    PRIMARY KEY (Id),
    INDEX idx_sesion_delegacion_fecha (DelegacionId, Fecha, Turno),
    INDEX idx_sesion_estado (Estado),
    FOREIGN KEY (DelegacionId) REFERENCES delegaciones(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS notas_prensa (
    Id INT NOT NULL AUTO_INCREMENT,
    SesionPrensaId INT NOT NULL,
    CategoriaId INT NOT NULL,
    PartidoId INT NULL,
    LocalidadId INT NULL,
    Titulo VARCHAR(500) NOT NULL DEFAULT '',
    Texto TEXT NOT NULL,
    Fuente VARCHAR(200) NULL,
    Link VARCHAR(2000) NULL,
    EsRepercusion TINYINT(1) NOT NULL DEFAULT 0,
    EstadoRevision VARCHAR(20) NOT NULL DEFAULT 'Pendiente',
    MotivoDescarte VARCHAR(500) NULL,
    FechaRegistro DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    OperadorRevision VARCHAR(100) NULL,
    FechaRevision DATETIME NULL,
    PRIMARY KEY (Id),
    INDEX idx_nota_sesion (SesionPrensaId),
    INDEX idx_nota_estado (EstadoRevision),
    INDEX idx_nota_categoria (CategoriaId),
    FOREIGN KEY (SesionPrensaId) REFERENCES sesiones_prensa(Id),
    FOREIGN KEY (CategoriaId) REFERENCES categorias_noticia(Id),
    FOREIGN KEY (PartidoId) REFERENCES Partidos(idPartido),
    FOREIGN KEY (LocalidadId) REFERENCES localidades(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sintesis (
    Id INT NOT NULL AUTO_INCREMENT,
    Tipo VARCHAR(20) NOT NULL DEFAULT 'Vespertina',
    Fecha DATE NOT NULL,
    Estado VARCHAR(20) NOT NULL DEFAULT 'Borrador',
    PDFPath VARCHAR(500) NULL,
    FechaCreacion DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
    FechaGeneracion DATETIME NULL,
    OperadorGenera VARCHAR(100) NOT NULL DEFAULT '',
    PRIMARY KEY (Id),
    INDEX idx_sintesis_fecha_tipo (Fecha, Tipo),
    INDEX idx_sintesis_estado (Estado)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

CREATE TABLE IF NOT EXISTS sintesis_notas (
    Id INT NOT NULL AUTO_INCREMENT,
    SintesisId INT NOT NULL,
    NotaPrensaId INT NOT NULL,
    Orden INT NOT NULL DEFAULT 0,
    PRIMARY KEY (Id),
    INDEX idx_sn_sintesis (SintesisId),
    INDEX idx_sn_nota (NotaPrensaId),
    FOREIGN KEY (SintesisId) REFERENCES sintesis(Id),
    FOREIGN KEY (NotaPrensaId) REFERENCES notas_prensa(Id)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4;

-- ─────────────────────────────────────────────────────────────
-- SEED: Categorías del Instructivo oficial (orden fijo)
-- ─────────────────────────────────────────────────────────────

INSERT INTO categorias_noticia (Nombre, Orden) VALUES
('Ámbito Nacional',                      1),
('Ámbito Provincial',                    2),
('Ámbito La Plata',                      3),
('Ámbito (Partido)',                      4),
('Institucionales',                       5),
('Visita de Funcionarios',               6),
('Hechos Delictuales',                   7),
('Procedimientos Policiales',            8),
('Procedimientos de Otras Fuerzas',      9),
('Violencia de Género',                  10),
('Delitos contra la Integridad Sexual',  11),
('Búsqueda/Hallazgo de Personas',        12),
('Hallazgo de Cadáver/Restos Óseos',     13),
('Averiguación Causales de Muerte',      14),
('Denuncias',                            15),
('Movilizaciones – Protestas',           16),
('Siniestros – Catástrofes',             17),
('Intimidación Pública',                 18),
('Reclamos de Seguridad',                19),
('Ámbito Rural',                         20),
('Ámbito Judicial',                      21),
('Ámbito Penitenciario',                 22),
('Fugas – Motines y Otros',              23),
('Declaraciones de Seguridad',           24),
('Accidente Vial',                       25),
('Ámbito Deportivo',                     26),
('Ámbito Escolar',                       27),
('Notas de Opinión',                     28),
('Notas de Interés',                     29),
('Repercusiones Periodísticas',          30);

-- ─────────────────────────────────────────────────────────────
-- SEED: Migrar AreaResponsabilidad y Partidos desde Zeus
-- (ejecutar solo si no se copian desde SBInteligencia)
-- ─────────────────────────────────────────────────────────────
-- INSERT INTO medios.AreaResponsabilidad SELECT * FROM SBInteligencia.AreaResponsabilidad;
-- INSERT INTO medios.Partidos SELECT * FROM SBInteligencia.Partidos;
