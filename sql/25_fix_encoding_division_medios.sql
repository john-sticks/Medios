-- Corrige el mojibake UTF-8 -> Latin1 en el nombre de la delegación especial "División Medios"
-- (creada por 23_delegacion_division_medios.sql), producido por el charset de la conexión usada
-- al ejecutar el INSERT original. Ejecutar este script con el cliente mysql forzando
-- --default-character-set=utf8mb4.
SELECT Id, Nombre FROM delegaciones WHERE Nombre LIKE '%Ã%';

UPDATE delegaciones SET Nombre = 'División Medios'
WHERE Nombre LIKE 'Divisi%n Medios%' AND Nombre <> 'División Medios';
