# Preparación local de producción — 18/11/2026

No publicar en GitHub ni desplegar producción hasta reunir y aprobar todos los cambios. La aplicación y el receptor incremental ya se probaron y desplegaron en test; este documento reúne el lote pendiente de producción, todavía sin publicar.

## Contenido del único lote productivo

- Aplicación: menú, Monitor y flujo de PDF opcional descritos abajo.
- Idioma: español de Argentina (es-AR) y etiquetas para evitar la sugerencia de traducción.
- Agente compatible con la transferencia incremental: incluido en scripts/keryx-backup-agent.py y en el paquete BackupAgent que publica la aplicación. Se conserva la configuración privada existente.
- Pruebas funcionales, permisos y del agente incluidas en la validación de CI antes de publicar la imagen.
- La limpieza de PDF antiguos no se incluye ni se ejecuta automáticamente.

## Menú y Monitor

- Monitor conserva capacidad y evolución. Mantenimiento e importación de retorno pasan a Administración → Backups y Contingencia, exclusivo del desarrollador real.
- Producción presenta dos pestañas: respaldos productivos (acceso al administrador de respaldos en test) y contingencia (mantenimiento e importador productivo existente). Crear y programar respaldos continúa siendo responsabilidad del receptor de test.
- Sincronización se oculta del menú de test y producción. No se elimina código ni se cambian sus permisos de acceso directo: retirar definitivamente los endpoints sería otro cambio.

## PDF: estado y criterio de limpieza

La publicación y descarga sin PDF físico están implementadas y verificadas localmente en ambos repositorios. Publicar deja la síntesis en Bandeja, en estado Generada y sin PDFPath; Remitir no requiere descargar ni generar un PDF y admite las síntesis anteriores que quedaron en Borrador. Se mantienen autorización, pertenencia a delegación y mantenimiento. Bandeja, Detalle, Publicadas y Archivo de PDF generan las síntesis no consolidadas desde DB incluso con una referencia antigua a un archivo eliminado. Descargar no cambia estado, ruta ni fecha, y no deja archivos. Los consolidados/conciliados siguen utilizando el PDF físico conservado, incluidos los reemplazos manuales. No se ha desplegado este nuevo flujo ni se han borrado los archivos antiguos: producción sigue con su versión anterior hasta publicar todo el lote.

Para una futura limpieza se consideran candidatos únicamente PDF generados por el sistema que tengan referencia inequívoca a una sesión o síntesis con delegación y puedan regenerarse correctamente con los datos conservados en la DB. La comparación debe usar las referencias de la DB y el nombre canónico completo correspondiente al registro, no solo un prefijo del archivo.

Se conservan siempre:

- Todas las síntesis consolidadas/conciliadas (DelegacionId nulo), incluidas las filtradas, sus PDF actuales y sus respaldos.
- Todos los PDF modificados o subidos manualmente, identificados por la procedencia registrada del archivo. Los reemplazos manuales de consolidados se siguen descargando del archivo conservado.
- Archivos compartidos con cualquier documento protegido y archivos de procedencia desconocida, sin referencia o que no puedan regenerarse.

Antes de habilitar un borrado: desplegar y verificar el nuevo flujo de producción, comprobar descargas, envío de correos y exportación de contingencia; generar un inventario de candidatos/protegidos; respaldar y validar; comprobar generación sin cambios de estado/fecha; ejecutar solo sobre el inventario revisado, con auditoría. No se implementa ni ejecuta eliminación de PDF en esta entrega local.

## Backups: integración con test ya preparada

El receptor incremental está desplegado en test (versión 8bdd3795ef7effbaa769e7b34d887eb91ab1f3a6). La base completa existente de 583,19 MB ya fue indexada, sin solicitar otra transferencia. Test ofrece completo e incremental manuales, y los horarios automáticos solicitan incremental.

El agente compatible se incluye ahora en este lote local de producción. Captura SQL de forma consistente localmente, compara el contenido del SQL y de los archivos con la última base confirmada y transfiere solo bloques nuevos o modificados. El manifiesto autoritativo también refleja eliminaciones. Los cambios de esquema quedan incluidos en el SQL reconstruido, sin aplicar parches SQL directamente a producción o a la copia de trabajo.

El NAS reconstruye un respaldo completo cifrado y autosuficiente y valida su restauración aislada antes de confirmarlo. Un fallo conserva la última copia recuperable. Se mantienen el respaldo completo previo al retorno y las protecciones durante contingencia. Las credenciales y claves de DataProtection no se sustituyen.

Hasta instalar el agente compatible en el servidor productivo, las solicitudes incrementales quedan pendientes. Los completos manuales siguen disponibles. Ningún agente se instaló ni ejecutó en producción durante esta preparación.

## Aplicación futura del lote — no ejecutada

1. Revisar el conjunto completo y publicar una sola versión cuando el usuario lo autorice. El workflow valida la aplicación y el agente y publica la imagen productiva.
2. Verificar que la imagen corresponda al commit aprobado y que CI termine correctamente.
3. El usuario ejecutará por SSH el despliegue de la aplicación con scripts/deploy-production.sh.
4. En la misma intervención deberá actualizar el agente instalado con sudo bash scripts/install-backup-listener.sh. Este instalador copia el agente nuevo, conserva credenciales y temporizador y reinicia la escucha. Publicar la imagen de la aplicación por sí solo no actualiza /usr/local/sbin/keryx-backup-agent.py.
5. Comprobar en producción el nuevo menú, Monitor sin controles operativos, publicación/remisión sin PDF obligatorio, descarga desde DB y conservación de consolidados. Comprobar en test la detección del agente compatible y la confirmación de un incremental validado.

No se realizan commits, push, despliegues, cambios de configuración ni borrados de PDF en producción durante esta preparación local.
