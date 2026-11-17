# Respaldos productivos, copia de test y contingencia

El módulo se encuentra en **Administración → Monitor**, exclusivamente para DESARROLLADOR.
Las pestañas son Capacidad, Respaldos y Contingencia. Respaldos/Contingencia y el selector requieren
`Deployment__Environment=Test` **y** `TestBackup__Enabled=true`. No habilitarlos en producción.
El modo Mantenimiento/Normal es independiente y puede desplegarse en ambos ambientes.

## Método recomendado

- Respaldo completo diario a las 02:00 (Buenos Aires), retención de 14 días; configuración desde Monitor.
- El temporizador consulta la programación por HTTPS; en producción se conserva el ajuste horario existente. Una escucha HTTPS persistente independiente recibe las solicitudes manuales sin esperar ese temporizador.
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

1. Mantener cerrada la escritura productiva durante el retorno y respaldar ambos ambientes.
2. Desde Contingencia, cerrar la escritura de la copia y descargar el paquete cifrado.
3. En producción, con rol **DESARROLLADOR**, abrir **Administración → Monitor → Importar retorno de contingencia**.
   Seleccionar el archivo `.kbk` y pulsar **Revisar paquete**. La vista previa muestra las altas, los adjuntos
   referenciados y las posibles coincidencias con producción.
4. Pulsar **Aplicar retorno en producción** cuando las coincidencias correspondan a registros distintos.
   El sistema pausa las solicitudes productivas y exige un respaldo nuevo recibido y validado en el NAS
   antes de aplicar cambios. Ese respaldo queda conservado como previo al retorno.
   La aplicación asigna nuevos IDs, conserva los vínculos y registra el origen para evitar una segunda importación.
5. Verificar notas, sesiones, publicaciones y adjuntos antes de desactivar contingencia y reabrir la carga productiva.

La importación desde la pantalla admite altas realizadas sobre una copia limpia. Los cambios de configuración
y los registros de actividad se omiten. Los duplicados confirmados, las diferencias de catálogo y los paquetes
con modificaciones sobre actividad preexistente requieren conciliación antes de importarse.
Si reabrís la copia antes de importar, generá un paquete nuevo al terminar la carga.

Para descifrar el paquete, usá la clave privada del agente (campo `secret` de
`/etc/keryx-backup/agent.json`). Podés ingresarla en la pantalla por HTTPS. Si el acceso productivo
es por HTTP, configurá `RETORNO_PRODUCCION_CLAVE` en el archivo privado `/opt/medios/.env`
y recreá el contenedor. La instalación productiva también puede leer esa configuración mediante el montaje
de solo lectura `/etc/keryx-backup:/run/keryx-backup:ro`. El formulario usa la clave del servidor cuando está configurada.

Mientras la contención está activa en el NAS, producción muestra su enlace en el login y restringe
el acceso operativo. DESARROLLADOR conserva el acceso para revisar el retorno. Un estado remoto desconocido
mantiene la restricción. La desactivación de la contención sigue siendo manual.

## IA local al probar una copia productiva

TestBackup__OllamaBaseUrl puede fijar la ruta privada del NAS para Ollama.
Solo tiene efecto con Deployment__Environment=Test. Se usa para generación de síntesis,
clasificación y consulta de modelos, sin cambiar ia_ollama_url en ninguna base.
Así las restauraciones conservan la configuración productiva, mientras test usa su red local.
La opción se ignora en producción aunque quedara configurada accidentalmente.

## Respaldo inmediato desde el botón (06/11/2026)

El agente actualizado abre una conexión HTTPS saliente a `/api/testbackup/escuchar` (SSE, autenticada con el token existente). El botón guarda la solicitud durable y despierta la conexión. Se arranca el mismo trabajo `--once`, con el mismo bloqueo local que el timer, sin alterar la aplicación ni abrir puertos/SSH hacia el NAS. Los heartbeats de la conexión no generan dumps ni arrancan el servicio por minuto. Si la conexión se corta se reconecta con espera creciente; si el receptor reinicia, la solicitud pendiente se recupera al reconectar. El timer sigue funcionando como respaldo de la programación.

Monitor muestra «Escucha inmediata: Conectada» solo cuando hay heartbeat reciente de ese canal; un agente antiguo que consulta por hora no activa ese indicador. Sin la escucha instalada, el botón sigue dejando la solicitud pendiente para la próxima consulta.

Actualización una sola vez del agente instalado (cuando esta versión esté disponible en el checkout productivo):

```bash
cd /opt/medios/source
sudo bash scripts/install-backup-listener.sh
sudo systemctl status keryx-backup-listener.service --no-pager
```

El instalador conserva la configuración privada y el timer/drop-in horario. Respalda el script anterior e instala el listener con permisos restringidos. No ejecuta `docker stop`, `compose down`, `prune` ni reinicia Keryx. Los paquetes nuevos de instalación incluyen también este servicio. Para desactivar solo el disparo inmediato: `sudo systemctl disable --now keryx-backup-listener.service`; las solicitudes pendientes siguen siendo recogidas por el timer.
