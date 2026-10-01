-- Delegación especial "División Medios": no es una delegación real de la Provincia, es un
-- marcador para que las notas/síntesis que MEDIOS carga directamente (sin ninguna Delegación
-- de por medio) tengan una identidad propia en DelegacionId, en vez de compartir el mismo NULL
-- que usa la síntesis consolidada real (GetConsolidasAsync/GetInformadasAsync/GetPublicadasAsync
-- filtran por DelegacionId == NULL — si Medios usara NULL también, sus síntesis propias se
-- mezclarían con la consolidada agregada).
INSERT INTO delegaciones (Nombre, Activa)
SELECT 'División Medios', 1
WHERE NOT EXISTS (SELECT 1 FROM delegaciones WHERE Nombre = 'División Medios');

SELECT Id, Nombre, Activa FROM delegaciones WHERE Nombre = 'División Medios';
