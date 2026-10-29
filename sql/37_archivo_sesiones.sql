-- Ejecutar SOLO en el ambiente a desplegar, antes de actualizar la aplicación.
-- Columnas opcionales: no cambia estados, notas, versiones ni archivos existentes.
SET @ddl = IF((SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='sesiones_prensa' AND column_name='FechaArchivo')=0,
 'ALTER TABLE sesiones_prensa ADD COLUMN FechaArchivo DATETIME NULL', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @ddl = IF((SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='sesiones_prensa' AND column_name='UsuarioArchivo')=0,
 'ALTER TABLE sesiones_prensa ADD COLUMN UsuarioArchivo VARCHAR(100) NULL', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
SET @ddl = IF((SELECT COUNT(*) FROM information_schema.columns WHERE table_schema=DATABASE() AND table_name='sesiones_prensa' AND column_name='MotivoArchivo')=0,
 'ALTER TABLE sesiones_prensa ADD COLUMN MotivoArchivo VARCHAR(500) NULL', 'SELECT 1');
PREPARE stmt FROM @ddl; EXECUTE stmt; DEALLOCATE PREPARE stmt;
