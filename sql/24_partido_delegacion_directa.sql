-- Relación directa Partido -> Delegación (tabla propia `delegaciones`), reemplazando el
-- vínculo indirecto vía el catálogo Prometheus (Partido.DelegacionPrometheusId ==
-- Delegacion.DelegacionPrometheusId) como fuente de verdad para "qué partidos tiene una
-- delegación". Superintendencia (AreaResponsabilidad) deja de usarse para esto; queda solo
-- como dato descriptivo.
ALTER TABLE partidos_prometheus
    ADD COLUMN DelegacionId INT NULL AFTER delegacion_id,
    ADD CONSTRAINT fk_partido_delegacion FOREIGN KEY (DelegacionId) REFERENCES delegaciones(Id) ON DELETE SET NULL;

-- Backfill: matchear por el vínculo Prometheus compartido que se usaba hasta ahora
UPDATE partidos_prometheus pp
JOIN delegaciones d ON d.DelegacionPrometheusId = pp.delegacion_id
SET pp.DelegacionId = d.Id;

-- Partidos que quedaron sin delegación asignada tras el backfill (asignar a mano en /partido/editar)
SELECT pp.id, pp.nombre FROM partidos_prometheus pp WHERE pp.DelegacionId IS NULL;
