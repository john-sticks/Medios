# Respaldos productivos, copia de test y contingencia

El módulo se encuentra en **Administración → Monitor**, exclusivamente para DESARROLLADOR.
Las pestañas son Capacidad, Respaldos y Contingencia. Respaldos/Contingencia y el selector requieren
`Deployment__Environment=Test` **y** `TestBackup__Enabled=true`. No habilitarlos en producción.
El modo Mantenimiento/Normal es independiente y puede desplegarse en ambos ambientes.

## Método recomendado

- Respaldo completo diario a las 02:00 (Buenos Aires), retención de 14 días; configuración desde Monitor.
- El agente de producción consulta el NAS cada minuto mediante HTTPS. Un timer systemd lo reinicia después del arranque.
- Dump MySQL con `--single-transaction --quick --no-tablespaces`, más PDF e imágenes de los volúmenes persistentes.
- Los datos productivos no se modifican ni se detiene el contenedor. Evitar cambios de esquema durante el dump.
- El snapshot transaccional cubre tablas InnoDB. Los archivos se copian después: no es una instantánea atómica DB/archivos.
- Cifrado AES-256-CBC/PBKDF2-SHA256 (200.000 iteraciones), autenticación HMAC-SHA256 y comprobación SHA-256 al recibir.
- Transferencia por partes de 8 MiB; máximo 512 MiB comprimidos/cifrados por copia, 10 GiB al extraer.
- La restauración es siempre manual sobre `Test-BKUP-Productiva`; nunca reemplaza automáticamente los datos de test.
- RPO orientativo diario: hasta 24 horas de actividad pueden faltar si producción se pierde antes del siguiente respaldo.
  Reducir el intervalo exige revisar carga, retención y capacidad. La programación actual admite una ejecución diaria y solicitudes manuales.
- Los respaldos del NAS no sustituyen una copia externa del NAS y de sus claves de recuperación.

## Configuración del NAS

Archivo privado `backup.env` del despliegue (no versionado), permisos 600:

```
Deployment__Environment=Test
TestBackup__Enabled=true
TestBackup__Directory=/app/operaciones
TestBackup__ReceiverUrl=https://medios.crmai.net.ar
TestBackup__CloneConnectionString=<conexión limitada a Test-BKUP-Productiva>
```

Montaje persistente `./operaciones:/app/operaciones`, directorio privado 700.
La cuenta MySQL del clon debe tener permisos únicamente sobre esa base. La importación usa mariadb-client.
El Dockerfile de test incluye ese cliente. El clon se crea vacío, sin asignar permisos sobre bases productivas.
Conservar además el volumen de claves ASP.NET Data Protection: protege la configuración y las credenciales locales.
Sin sus claves originales no se puede recuperar el secreto de los respaldos.

## Instalar una vez el agente productivo

1. En Monitor → Respaldos, abrir Instalación del agente y pulsar **Preparar instalación por SSH**.
2. Se muestra un código de un solo uso, válido diez minutos. No pegarlo en chats, repositorios o tickets.
3. Ejecutar en el servidor productivo el bloque siguiente; ingresar el código en el prompt privado.

```bash
(
set -euo pipefail
umask 077
trabajo=$(mktemp -d)
trap 'rm -rf -- "$trabajo"' EXIT
read -r -s -p 'Código de instalación: ' codigo
printf '\n'
printf 'header = "Authorization: Bearer %s"\n' "$codigo" \
  | curl --config - --fail --silent --show-error --request POST \
      https://medios.crmai.net.ar/api/testbackup/install \
      --output "$trabajo/agente.zip"
unset codigo
python3 -m zipfile -e "$trabajo/agente.zip" "$trabajo/agente"
sudo bash "$trabajo/agente/install-backup-agent.sh" \
  "$trabajo/agente/keryx-backup-agent.json"
)
```

El instalador no detiene Keryx ni escribe en MySQL. Instala un servicio y un timer del sistema.
Requiere python3, curl, openssl, mysqldump y docker. El transporte usa curl con
TLS validado, sin redirecciones, proxy ni credenciales en los argumentos del proceso. La cuenta root debe poder ejecutar mysqldump local,
como en los respaldos manuales existentes. Los volúmenes PDF/uploads deben estar bajo `/opt/medios`.
El código temporal se consume al descargar; generar otro si el despliegue/descarga falla.
La configuración permanente queda en `/etc/keryx-backup/agent.json` con permisos 600.
No subir ese archivo ni el ZIP al repositorio. Diagnóstico: `sudo journalctl -u keryx-backup.service -n 30 --no-pager`.
Para detener la programación: `sudo systemctl disable --now keryx-backup.timer`.

## Trabajar con casos reales desde test

Recibir un respaldo, elegirlo y confirmar **Restaurar únicamente la copia y sus archivos**.
La copia permanece deshabilitada durante la restauración y si falla; el respaldo cifrado se conserva.
Luego elegir **Test-BKUP-Productiva → Aplicar a mi sesión**.
La selección dura ocho horas, está firmada y vinculada al desarrollador y a la generación restaurada.
Otros usuarios de test siguen en la base original. Formularios abiertos antes del cambio se rechazan para evitar guardar en otra base.
PDF/uploads y caché del monitor están separados. El correo real y la sincronización externa quedan bloqueados en pruebas.
Restaurar reemplaza los cambios de laboratorio del clon. No restaurar sobre una copia con actividad de contingencia pendiente de recuperar.

## Caída productiva y de Cerberus

1. Asegurar que producción no esté aceptando escrituras; evitar operar simultáneamente en dos servidores.
2. Restaurar el último respaldo verificado en una copia limpia.
3. Revisar usuarios aprobados, activos, roles y delegaciones del respaldo. La emergencia usa la autorización local
   conservada, no consulta el rol externo de Cerberus. Corregir cualquier diferencia antes de dar acceso.
4. Preparar credenciales individuales y entregarlas por un canal privado. No se copian contraseñas de Cerberus.
5. Activar contingencia explícitamente por 24 horas. Un clon con modificaciones de laboratorio no puede activarse.
6. Los usuarios ingresan por `/Login/Contingencia`; deben cambiar la clave inicial. Las claves vencen a los 30 días,
   cinco fallos bloquean quince minutos y las sesiones duran treinta minutos.
7. Toda sesión de emergencia usa la copia; no permite simular otros usuarios. Al desactivar se invalidan sus sesiones.
8. En contingencia sí se permiten los envíos operativos de correo. Realizar un simulacro controlado antes de depender de esta vía.

## Mantenimiento y retorno consistente

**Mantenimiento** bloquea escrituras DELEGACION del ambiente actual, incluso por POST/PUT/PATCH/DELETE directos.
Las consultas siguen disponibles y MEDIOS/DESARROLLADOR pueden trabajar. No implica congelar a todos los roles.
La preparación de retorno, en cambio, cierra las escrituras del clon para **todos** los roles.

El paquete de retorno contiene la línea base (claves y huellas), altas/modificaciones/eliminaciones y adjuntos.
Está cifrado con el secreto privado del agente. **No existe importación automática a producción en esta versión.**
No ejecutar un restore del clon sobre una producción que recibió otros cambios.

Procedimiento para el retorno:

1. Mantener cerrada la escritura productiva durante la conciliación y respaldar ambos ambientes.
2. Preparar el paquete de retorno desde Contingencia; esperar que terminen las solicitudes activas.
3. Comparar tres fuentes: respaldo original, copia de contingencia y producción actual.
   - Cambió solo contingencia: candidato a aplicar.
   - Cambió solo producción: conservar el dato productivo.
   - Cambiaron ambas con valores distintos: conflicto que requiere decisión explícita.
4. Altas con IDs repetidos requieren reasignar IDs y actualizar todas sus referencias, incluidos versiones,
   padres de versiones, notas de origen, sesiones y síntesis. No insertar ciegamente por ID ni sustituir tablas completas.
5. Resolver eliminaciones y archivos por referencia; conservar auditoría e historial. Validar permisos/delegaciones,
   conteos, versiones, enlaces, publicaciones y PDF antes de habilitar el acceso productivo.
6. Desactivar contingencia y volver a Normal únicamente después de validar y dirigir a todos a producción.
7. Si se reabre el clon, el paquete anterior queda obsoleto: generar uno nuevo al cerrar otra vez.

La conciliación/importador de tres vías sigue siendo un paso operativo supervisado. El módulo entrega la evidencia
para realizarlo y evita presentar una restauración destructiva como sincronización.
