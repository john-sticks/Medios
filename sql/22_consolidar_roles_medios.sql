-- Consolidación de roles: SUPERVISOR y ANALISTA se fusionan en un único rol MEDIOS.
-- Roles de negocio resultantes: MEDIOS, DELEGACION. Rol técnico separado: DESARROLLADOR.
-- Ejecutar manualmente (no forma parte del pipeline automático de deploy).

-- Distribución antes de migrar
SELECT rol, COUNT(*) AS cantidad
FROM autorizaciones_usuario
GROUP BY rol;

UPDATE autorizaciones_usuario
SET rol = 'MEDIOS'
WHERE UPPER(TRIM(rol)) IN ('SUPERVISOR', 'ANALISTA');

-- Distribución después de migrar
SELECT rol, COUNT(*) AS cantidad
FROM autorizaciones_usuario
GROUP BY rol;
