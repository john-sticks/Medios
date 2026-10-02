-- Corrige las altas del ABM local de caratulas/modalidades.
-- Ejecucion MANUAL en medios, con backup previo. No modifica filas ni IDs.
-- DDL: cada ALTER confirma por separado; no tiene rollback transaccional.
-- INT firmado/NOT NULL/PK se conservan. Las FK existentes se mantienen.
-- Desactiva comprobaciones SOLO en esta sesion y SOLO durante los dos ALTER.
USE medios;
SET @medios_fk_checks_anterior = @@SESSION.foreign_key_checks;
SET SESSION foreign_key_checks = 0;
ALTER TABLE caratulas_quiron MODIFY COLUMN id INT NOT NULL AUTO_INCREMENT;
ALTER TABLE modalidades_quiron MODIFY COLUMN id INT NOT NULL AUTO_INCREMENT;
SET SESSION foreign_key_checks = @medios_fk_checks_anterior;
SELECT TABLE_NAME,COLUMN_NAME,COLUMN_TYPE,EXTRA
FROM information_schema.COLUMNS
WHERE TABLE_SCHEMA='medios'
  AND TABLE_NAME IN ('caratulas_quiron','modalidades_quiron') AND COLUMN_NAME='id';
