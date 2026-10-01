-- Usuario de prueba: rol DELEGACION, sin delegación ni ámbito asignado
-- Sirve para verificar que el sistema maneja correctamente la ausencia de scope

INSERT INTO autorizaciones_usuario
    (usuario, nombre, email, destino, jerarquia, legajo, telefono, codigo, estado,
     fecha_solicitud, fecha_resolucion, aprobado_por, delegacion_id, ambito_id)
VALUES
    ('prueba', 'Usuario Prueba', 'prueba@medios.test', 'Sin dependencia', 'Oficial',
     '999999', '', '', 'aprobada', NOW(), NOW(), 'admin', NULL, NULL);
