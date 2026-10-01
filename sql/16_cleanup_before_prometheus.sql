-- Limpieza previa a la migración a tablas Prometheus
-- Elimina delegaciones de prueba con PartidoId apuntando a tabla Partidos vieja
DELETE FROM delegaciones WHERE Id IN (2, 3, 4, 5);

-- Limpiar campos de delegacion mock que referencian tablas viejas
UPDATE delegaciones SET PartidoId = NULL, AreaResponsabilidadId = NULL WHERE Id = 1;

-- Limpiar ambito_id en autorizaciones (apuntaba a AreaResponsabilidad vieja)
UPDATE autorizaciones_usuario SET ambito_id = NULL WHERE ambito_id IS NOT NULL;
