# Datos personales de Cerberus

Al ingresar con autorización local se consulta el perfil autenticado de Cerberus (`users/info-servicio`) con un límite de tres segundos. Antes se validaban las credenciales sin consultar ese perfil, por lo que las solicitudes nuevas podían conservar solamente el usuario.

Las solicitudes pendientes y aprobadas actualizan nombre, correo, destino, jerarquía, legajo y teléfono al siguiente ingreso. Los valores vacíos no borran los existentes. La actualización no cambia aprobación, actividad, rol local, delegación, ámbito, código ni fechas. No requiere cambios del esquema.

Si el servicio central no entrega un perfil (incluidos permisos insuficientes), falla o devuelve datos inválidos, se conserva la identidad local y sigue siendo obligatoria la aprobación. No se consulta a otros usuarios ni se sincroniza el listado completo.

La verificación automatizada cubre solicitud nueva, actualización de pendientes y aprobados, usuario revocado, perfil no disponible, protección de campos locales y contrato HTTP. Falta contrastar una respuesta real de Cerberus para las cuentas afectadas: test usa Mock. Si info-servicio exige una asignación central ausente, se necesita resolver ese contrato antes de completar sus datos.

En producción se conserva SUPERVISOR → DESARROLLADOR con aprobación local. La misma consulta de perfil obtiene rol y datos personales; no se repite la petición. Las cuentas rechazadas o desactivadas no consultan el perfil ni recuperan acceso.
