-- Migración 11: SesionPrensaId nullable en notas_prensa
-- Permite notas "libres" que no están vinculadas a ningún borrador

ALTER TABLE notas_prensa MODIFY COLUMN SesionPrensaId INT NULL;
