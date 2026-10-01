-- Agregar columna PortalesPrensa a delegaciones
USE medios;
ALTER TABLE delegaciones ADD COLUMN IF NOT EXISTS PortalesPrensa JSON NULL;
