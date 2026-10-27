# Archivo de PDF de síntesis

Acceso: **MEDIOS → Síntesis → Archivo de PDF** (`/ArchivoPdf`).

- Delegaciones: documentos vigentes de síntesis y sesiones, agrupados por
  delegación, incluida División Medios. No duplica una misma ruta física.
- Consolidados: síntesis con `DelegacionId == null`. Permite descargar y
  reemplazar su PDF por un archivo modificado localmente de hasta 20 MB.
- Ambas pestañas muestran fecha de síntesis, tipo, estado, origen del PDF y
  fecha/hora de generación o carga. La consulta exige un período de hasta
  15 días inclusivos (predeterminado: hoy y los 14 días anteriores).
  Delegaciones exige además elegir una delegación; Consolidados solo el período.
  Los filtros se aplican en SQL, no después de cargar todos los documentos.
- Las referencias a archivos faltantes se señalan, sin enlace de descarga.

El resumen global recorre la carpeta PDF y sus subcarpetas, incluyendo respaldos
y documentos sin referencia vigente en la DB. Muestra cantidad, suma de tamaños
en MB decimales y fechas de modificación más antigua/reciente de los archivos
físicos. Es independiente del período/delegación seleccionado. No sigue enlaces
simbólicos y no cuenta referencias repetidas en la DB como archivos adicionales.

El reemplazo cambia únicamente `Sintesis.PDFPath`. No modifica las notas,
snapshots, estado, fecha de generación original ni correos enviados. Las
descargas y futuros envíos existentes usan esa misma ruta vigente.

Cada carga utiliza un nombre único con fecha UTC en el servidor; esa fecha
permite identificar el origen manual sin agregar columnas a la DB. El listado
presenta la fecha en el horario local del servidor (Buenos Aires en producción).
Las generaciones anteriores sin fecha registrada se muestran explícitamente
como «Sin fecha registrada».

Antes de reemplazar se copia el archivo anterior, si existe, a
`wwwroot/pdf/respaldos/sintesis_<id>_<guid>.pdf`. Ese respaldo no depende de la
ruta que luego pueda sobrescribir una regeneración. Los respaldos no integran
el listado de documentos vigentes; no se eliminan automáticamente.

En producción `wwwroot/pdf` está montado en `/opt/medios/source/pdfs`, por lo
que las cargas y la subcarpeta de respaldos sobreviven a despliegues/reinicios.

El módulo requiere rol MEDIOS además de `VER_SINTESIS`. La carga exige POST,
antiforgery y una síntesis consolidada con PDF previamente generado. Rechaza
una página desactualizada y utiliza `PDFPath` como token de concurrencia EF
(no requiere cambios de estructura en MySQL). El nombre enviado por el
navegador no determina el destino. La auditoría registra quién reemplazó el
documento, la ruta anterior y el nombre original de la carga.

La verificación de formato comprueba extensión, tamaño, cabecera y marcador
de final PDF; no sustituye un análisis completo del contenido del documento.

Cuando el PDF vigente es una carga manual, la vista de detalle lo advierte y
no ofrece Word: las modificaciones hechas fuera del sistema no pueden
reconstruirse desde los snapshots de las notas. El endpoint Word también lo
bloquea para evitar entregar un documento que no coincide con el PDF vigente.

Validación: `dotnet run --project tests/Medios.WorkflowChecks -c Release`.
No hay scripts SQL nuevos ni migración de datos para este módulo.
