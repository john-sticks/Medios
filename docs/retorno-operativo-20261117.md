# Retorno operativo del 17/11/2026

## Versiones revisadas

Producción parte de `8aea9e54`. Test incluye cambios posteriores hasta `e2a07fb`,
más el protocolo autenticado de contención y respaldo previo agregado para este retorno.

| Cambios de test | Situación al preparar el retorno |
| --- | --- |
| Ocultar «Es repercusión» y visibilidad de síntesis generales/filtradas | Ya incluidos en producción |
| `58d7e69`, búsqueda de notas de delegaciones y equivalencias de delegación del clon | Pendiente de promoción funcional. Requiere revisar sus diferencias por ambiente |
| Selección global de la copia, Consultas históricas y resolución de CSV/PDF | Exclusivos de test |
| Turnstile, acceso de contingencia, códigos alternativos y excepción de una cuenta | Exclusivos de test |
| Copia limpia, credenciales sin vencimiento y contingencia manual | Exclusivos de test |
| Importador de retorno, aviso productivo y bloqueo durante contención | Incluidos en el candidato productivo de este retorno |

## Orden de ejecución

1. Desplegar en el NAS el estado autenticado y el respaldo previo al retorno.
   La contingencia permanece activa y permite seguir cargando información.
2. Verificar que la producción recuperada conserva su base, sus archivos y
   `/etc/keryx-backup/agent.json`. El agente y su escucha inmediata deben estar instalados.
3. Desplegar el candidato productivo. Lee la configuración del agente mediante un montaje
   de solo lectura y consulta el estado del NAS por HTTPS con su token.
   Las consultas usan curl con conexión directa, igual que el agente de respaldos.
   El token se entrega por stdin y se mantienen las verificaciones de certificado HTTPS.
4. Mientras test está en contención, producción informa en el login el enlace de continuidad.
   Se rechazan nuevos accesos operativos y solicitudes de sesiones operativas ya abiertas.
   El rol DESARROLLADOR conserva acceso con su autenticación productiva habitual.
5. Al terminar la carga, cerrar la escritura del clon y descargar su paquete de retorno.
6. En producción, abrir Monitor → Importar retorno de contingencia y revisar el paquete.
7. Al confirmar, se bloquean las solicitudes productivas y se espera que terminen las activas.
   Se solicita al agente un nuevo respaldo productivo. El NAS debe recibirlo, validar su HMAC
   y devolver su identificador y SHA-256 antes de aplicar la importación.
   Ese respaldo queda marcado «Respaldo previo al retorno» y excluido de la retención automática.
8. Importar las altas con nuevos IDs, referencias y adjuntos, registrar el respaldo previo
   en `retornos_contingencia_importados` y validar el resultado.
9. Desactivar manualmente la contención en test. Producción recupera su acceso operativo
   en la siguiente solicitud que consulte el estado.

Producción conserva el último estado confirmado en un archivo protegido, persistente entre reinicios.
Si el NAS deja de responder después de confirmar modo normal, producción permite el acceso operativo.
Si el último estado era contención activa, mantiene el bloqueo e informa «Contactar con el administrador».
Sin un estado confirmado previo, el acceso operativo requiere que el administrador verifique el estado.
Un fallo del respaldo previo detiene la importación. El retorno no desactiva por sí mismo la contención.

## Protección de datos y despliegue

El paquete debe haberse generado desde la copia limpia. Los cambios de configuración se muestran
como omitidos. Las diferencias en catálogos referenciados detienen la importación para su revisión.
La aplicación requiere resolver las posibles coincidencias y no importa dos veces el mismo origen.

Los registros productivos y archivos existentes se conservan. Los nuevos adjuntos usan rutas
propias del paquete. La importación y su registro se confirman en una misma transacción MySQL.
El registro conserva el ID y SHA-256 del respaldo previo.

El receptor admite hasta 2 GiB cifrados, en bloques de 8 MiB, porque la última copia disponible
ya estaba próxima al límite anterior de 512 MiB.
