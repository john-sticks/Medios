# Comprobaciones de autenticación

Ejecutar desde la raíz del repositorio:

```bash
dotnet run --project tests/Medios.AuthChecks/Medios.AuthChecks.csproj -c Release
```

No agrega paquetes de prueba ni conecta con Cerberus o MySQL reales. Comprueba
respuestas del endpoint de credenciales mediante un HttpMessageHandler de prueba,
y las reglas locales para cuentas pendientes, roles, desactivación y sesiones
con asignaciones anteriores. Un fallo termina con código distinto de cero.

En modo Cerberus, Medios valida las credenciales en `auth/token` y no consulta
`info-servicio` durante el ingreso. Todas las cuentas, incluidas las de rol
DESARROLLADOR, necesitan una autorización local aprobada, activa y con rol
válido. Las nuevas cuentas generan una solicitud pendiente; no reciben acceso
por obtener un token. La desactivación global en Cerberus sigue impidiendo
obtener un token nuevo, según las pruebas realizadas en producción.

Antes de desplegar este cambio, verificar la autorización local de la cuenta
administradora. Una cuenta que dependía del rol de Cerberus y no tiene un rol
local aprobado no podrá administrar Medios después de la transición.

Los perfiles nuevos se identifican por su nombre de usuario; los demás datos
se toman del registro local, sin consultar el perfil restringido de Cerberus.
La validación de login, solicitudes, aprobación y revocación contra la base
real debe completarse después del despliegue manual.
