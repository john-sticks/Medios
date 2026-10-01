-- Agregar columna PortalesPrensa a delegaciones
USE medios;
ALTER TABLE delegaciones ADD COLUMN PortalesPrensa JSON NULL;
