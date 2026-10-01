-- Migración: reemplazar FKs de tablas viejas por tablas Prometheus

-- 1. Quitar FKs que apuntan a tablas viejas
ALTER TABLE delegaciones
    DROP FOREIGN KEY delegaciones_ibfk_1,
    DROP FOREIGN KEY fk_delegacion_area;

ALTER TABLE notas_prensa_versiones
    DROP FOREIGN KEY notas_prensa_versiones_ibfk_4,
    DROP FOREIGN KEY notas_prensa_versiones_ibfk_5;

ALTER TABLE portales_prensa
    DROP FOREIGN KEY fk_portal_partido;

ALTER TABLE autorizaciones_usuario
    DROP FOREIGN KEY fk_aut_ambito;

-- 2. Agregar columna DelegacionPrometheusId a delegaciones
ALTER TABLE delegaciones
    ADD COLUMN DelegacionPrometheusId INT NULL AFTER AreaResponsabilidadId;

-- 3. Asignar delegacion mock a prometheus (Mar del Plata = id 9)
UPDATE delegaciones SET DelegacionPrometheusId = 9 WHERE Id = 1;

-- 4. Recrear FKs apuntando a tablas prometheus
ALTER TABLE delegaciones
    ADD CONSTRAINT fk_delegacion_partido_p
        FOREIGN KEY (PartidoId) REFERENCES partidos_prometheus(id) ON DELETE SET NULL,
    ADD CONSTRAINT fk_delegacion_super_p
        FOREIGN KEY (AreaResponsabilidadId) REFERENCES superintendencias_prometheus(id) ON DELETE SET NULL,
    ADD CONSTRAINT fk_delegacion_prometheus
        FOREIGN KEY (DelegacionPrometheusId) REFERENCES delegaciones_prometheus(id) ON DELETE SET NULL;

ALTER TABLE notas_prensa_versiones
    ADD CONSTRAINT fk_version_partido_p
        FOREIGN KEY (PartidoId) REFERENCES partidos_prometheus(id) ON DELETE SET NULL,
    ADD CONSTRAINT fk_version_localidad_p
        FOREIGN KEY (LocalidadId) REFERENCES localidades_prometheus(id) ON DELETE SET NULL;

ALTER TABLE portales_prensa
    ADD CONSTRAINT fk_portal_partido_p
        FOREIGN KEY (PartidoId) REFERENCES partidos_prometheus(id) ON DELETE SET NULL;

ALTER TABLE autorizaciones_usuario
    ADD CONSTRAINT fk_aut_ambito_p
        FOREIGN KEY (ambito_id) REFERENCES superintendencias_prometheus(id) ON DELETE SET NULL;
