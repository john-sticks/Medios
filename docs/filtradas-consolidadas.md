# Crear filtradas desde Consolidadas

La acción Crear filtrada se encuentra en Bandeja → Consolidadas, para síntesis consolidadas con estado Generada (pendientes de informar). También está disponible en el detalle de esa consolidada. No aparece en Informadas; las rutas GET y POST tampoco aceptan ese estado.

La filtrada mantiene la fecha, tipo y versiones seleccionadas de la consolidada, con su propio PDF y envío. La creación no altera el origen ni duplica notas o versiones. Las filtradas existentes conservan sus datos e historial; no requiere SQL ni migraciones.

Mientras exista una filtrada vinculada, el origen no se puede eliminar, revertir ni convertir a borrador. Esto conserva su referencia y evita errores de clave foránea. Las filtradas pendientes se pueden eliminar con su acción existente; las informadas conservan su origen.
