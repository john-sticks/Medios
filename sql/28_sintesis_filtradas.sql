-- Aplicar antes de desplegar el codigo que usa SintesisOrigenId.
-- Conserva las sintesis existentes. Puede ejecutarse nuevamente.
SET @existe = (SELECT COUNT(*) FROM information_schema.COLUMNS
    WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'sintesis' AND COLUMN_NAME = 'SintesisOrigenId');
SET @ddl = IF(@existe = 0,
    'ALTER TABLE sintesis ADD COLUMN SintesisOrigenId INT NULL, ADD INDEX idx_sintesis_origen (SintesisOrigenId), ADD CONSTRAINT fk_sintesis_origen FOREIGN KEY (SintesisOrigenId) REFERENCES sintesis(Id) ON DELETE RESTRICT',
    'SELECT ''SintesisOrigenId ya existe'' AS Resultado');
PREPARE stmt FROM @ddl;
EXECUTE stmt;
DEALLOCATE PREPARE stmt;
