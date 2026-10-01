-- Seed de datos de prueba para testing
-- Requiere que 02_alter_autorizaciones.sql ya fue ejecutado
USE medios;

-- ─────────────────────────────────────────────────────────────
-- DELEGACIONES DE PRUEBA
-- ─────────────────────────────────────────────────────────────
INSERT INTO delegaciones (Nombre, Jurisdiccion, PartidoId, Email, UsuarioCerberus, Activa) VALUES
('Delegación Mar del Plata',   'Departamental Inteligencia Criminal',
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%MAR DEL PLATA%' LIMIT 1),
    'delegacion.mdp@policia.gba.gov.ar', 'delegacion', 1),
('Delegación San Isidro',      'Departamental Inteligencia Criminal',
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%SAN ISIDRO%' LIMIT 1),
    'delegacion.si@policia.gba.gov.ar', 'deleg_si', 1),
('Delegación La Plata',        'Departamental Inteligencia Criminal',
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%LA PLATA%' LIMIT 1),
    'deleg_lp', 'deleg_lp', 1),
('Delegación Quilmes',         'Departamental Inteligencia Criminal',
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%QUILMES%' LIMIT 1),
    'deleg_ql', 'deleg_ql', 1);

-- ─────────────────────────────────────────────────────────────
-- USUARIOS APROBADOS (para que puedan ingresar en modo Mock)
-- ─────────────────────────────────────────────────────────────
INSERT INTO autorizaciones_usuario
    (usuario, nombre, email, destino, jerarquia, legajo, telefono, codigo, estado,
     fecha_solicitud, fecha_resolucion, aprobado_por, delegacion_id, ambito_id)
VALUES
-- Admin: sin restricción de ámbito
('admin',      'Admin Sistema',       'admin@medios.test',      'División Medios O.S.INT', 'Comisario',         '100001', '', '000001', 'aprobada', NOW(), NOW(), 'sistema', NULL, NULL),
-- Supervisor: sin restricción de ámbito
('supervisor', 'Supervisor División', 'supervisor@medios.test', 'División Medios O.S.INT', 'Inspector',         '100002', '', '000002', 'aprobada', NOW(), NOW(), 'admin',   NULL, NULL),
-- Analista: sin restricción de ámbito
('analista',   'Analista Criminal',   'analista@medios.test',   'División Medios O.S.INT', 'Oficial Inspector', '100003', '', '000003', 'aprobada', NOW(), NOW(), 'admin',   NULL, NULL),
-- Operador: ámbito = Mar del Plata (AreaResponsabilidad id=9)
('operador',   'Operador Medios',     'operador@medios.test',   'División Medios O.S.INT', 'Oficial',           '100004', '', '000004', 'aprobada', NOW(), NOW(), 'admin',   NULL,
    (SELECT id FROM AreaResponsabilidad WHERE AmbitoResponsabilidad LIKE '%MAR DEL PLATA%' LIMIT 1)),
-- Delegacion: asignada a Delegación Mar del Plata
('delegacion', 'Agente Delegación',   'deleg@medios.test',      'DDIC Mar del Plata',      'Oficial',           '100005', '', '000005', 'aprobada', NOW(), NOW(), 'admin',
    (SELECT Id FROM delegaciones WHERE Nombre LIKE '%Mar del Plata%' LIMIT 1), NULL);

-- ─────────────────────────────────────────────────────────────
-- SESIONES Y NOTAS DE PRUEBA
-- ─────────────────────────────────────────────────────────────
-- Sesión 1: Delegación Mar del Plata - Vespertina de hoy - Remitida
INSERT INTO sesiones_prensa (DelegacionId, Fecha, Turno, Estado, FechaCreacion, FechaRemision, UsuarioCarga)
VALUES (
    (SELECT Id FROM delegaciones WHERE Nombre LIKE '%Mar del Plata%' LIMIT 1),
    CURDATE(), 'Vespertina', 'Remitida', NOW(), NOW(), 'delegacion'
);

SET @sesion1 = LAST_INSERT_ID();

-- Notas de la sesión 1
INSERT INTO notas_prensa (SesionPrensaId, CategoriaId, PartidoId, Titulo, Texto, Fuente, Link, EstadoRevision, FechaRegistro)
VALUES
(@sesion1,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Hechos Delictuales' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%MAR DEL PLATA%' LIMIT 1),
    'ROBO A MANO ARMADA EN EL CENTRO',
    'Un sujeto armado ingresó a un comercio ubicado en Rivadavia y San Martín y sustrajo $150.000 en efectivo. La víctima no resultó herida. El delincuente escapó en motocicleta.',
    '0223', 'https://www.0223.com.ar/nota/ejemplo', 'Pendiente', NOW()),

(@sesion1,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Procedimientos Policiales' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%MAR DEL PLATA%' LIMIT 1),
    'DETENIDO CON COCAÍNA FRACCIONADA EN OPERATIVO PREVENTIVO',
    'Personal de la UTOI detuvo a un hombre de 28 años en la intersección de Belgrano y Colón con 45 gramos de cocaína fraccionada para su comercialización. Intervino el fiscal de Flagrancia.',
    '0223', 'https://www.0223.com.ar/nota/ejemplo2', 'Pendiente', NOW()),

(@sesion1,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Accidente Vial' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%MAR DEL PLATA%' LIMIT 1),
    'CHOQUE EN RUTA 2 CON UN HERIDO GRAVE',
    'Un automóvil Volkswagen Golf despistó a la altura del kilómetro 395 de la Ruta 2. El conductor, de 32 años, fue trasladado al Hospital Interzonal en estado grave.',
    'LA CAPITAL', 'https://www.lacapitalmdp.com/nota/ejemplo', 'Pendiente', NOW());

-- Sesión 2: Delegación San Isidro - Matutina de hoy - Remitida
INSERT INTO sesiones_prensa (DelegacionId, Fecha, Turno, Estado, FechaCreacion, FechaRemision, UsuarioCarga)
VALUES (
    (SELECT Id FROM delegaciones WHERE Nombre LIKE '%San Isidro%' LIMIT 1),
    CURDATE(), 'Matutina', 'Remitida', NOW(), NOW(), 'deleg_si'
);

SET @sesion2 = LAST_INSERT_ID();

INSERT INTO notas_prensa (SesionPrensaId, CategoriaId, PartidoId, Titulo, Texto, Fuente, Link, EstadoRevision, FechaRegistro)
VALUES
(@sesion2,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Procedimientos Policiales' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%SAN ISIDRO%' LIMIT 1),
    'NARCOMENUDEO: SIETE DETENIDOS EN OPERATIVO EN BECCAR',
    'Personal de la comisaría 1ra de San Isidro junto a la División Ejecución de Capturas realizaron tres allanamientos simultáneos en el barrio Beccar. Se secuestraron 200 envoltorios de cocaína y un arma.',
    'ZONANORTEDIARIO', 'https://zonanortediario.com.ar/nota/ejemplo', 'Pendiente', NOW()),

(@sesion2,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Violencia de Género' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%SAN ISIDRO%' LIMIT 1),
    'DETIENEN A HOMBRE POR VIOLENCIA DOMÉSTICA Y PERIMETRAL INCUMPLIDA',
    'Efectivos de la comisaría 4ta aprehendieron a un sujeto de 45 años acusado de agredir a su pareja e incumplir la medida perimetral dictada por el Juzgado de Garantías. Quedó detenido.',
    'INFOCIUDAD', '', 'Pendiente', NOW());

-- Sesión 3: División Medios (interna) - notas ya aprobadas para síntesis
INSERT INTO sesiones_prensa (DelegacionId, Fecha, Turno, Estado, FechaCreacion, FechaRemision, UsuarioCarga)
VALUES (NULL, CURDATE(), 'Vespertina', 'Revisada', NOW(), NOW(), 'operador');

SET @sesion3 = LAST_INSERT_ID();

INSERT INTO notas_prensa (SesionPrensaId, CategoriaId, PartidoId, Titulo, Texto, Fuente, Link, EstadoRevision, OperadorRevision, FechaRevision, FechaRegistro)
VALUES
(@sesion3,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Ámbito Judicial' LIMIT 1),
    (SELECT idPartido FROM Partidos WHERE Nombre LIKE '%LA MATANZA%' LIMIT 1),
    'CONDENADO A 12 AÑOS POR HOMICIDIO EN GONZALEZ CATÁN',
    'El Tribunal Oral Criminal Nro. 1 de La Matanza condenó a 12 años de prisión a un hombre de 29 años hallado culpable del homicidio de un joven ocurrido en 2023 en el barrio Las Nieves.',
    '24CON', 'https://www.24con.com/nota/ejemplo', 'Aprobada', 'Operador Medios', NOW(), NOW()),

(@sesion3,
    (SELECT Id FROM categorias_noticia WHERE Nombre = 'Notas de Interés' LIMIT 1),
    NULL,
    'CRECEN LOS CIBERDELITOS EN LA PROVINCIA: AUMENTARON 76% EN 2026',
    'La UFIJ especializada en ciberdelitos informó que durante el primer cuatrimestre de 2026 se registraron 69 víctimas, un incremento del 76,9% respecto al mismo período de 2025.',
    'DIARIO5DÍAS', '', 'Aprobada', 'Operador Medios', NOW(), NOW());
