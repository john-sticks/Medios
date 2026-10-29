# Archivo reversible de sesiones finalizadas

Antes de desplegar ejecutar sql/37_archivo_sesiones.sql en la base del ambiente.
Agrega tres campos opcionales, sin actualizar registros existentes. Es repetible.

MEDIOS y DESARROLLADOR pueden archivar solo sesiones Finalizadas sin vinculación
a una consolidada. La pantalla de confirmación informa las notas aptas e impone
un motivo de hasta 500 caracteres. No aprueba ni elimina notas. Mantiene PDF,
versiones, referencias de síntesis, historial y rechazos pendientes de corrección.

El estado Archivada queda fuera de Finalizadas, Home y la selección para consolidar.
Dentro de Finalizadas, el botón Ver archivadas despliega al final la lista con la fecha de síntesis, tipo, delegación, notas, fecha
de archivo, cuenta de usuario y motivo; respeta los filtros de la bandeja.
Restaurar devuelve a Finalizadas. El último motivo se conserva, y cada ciclo queda
en traza_eventos con SESION_ARCHIVADA / SESION_RESTAURADA en la misma transacción.
Las acciones de escritura son POST con antifalsificación y validación de rol.

Estado es un token de concurrencia de EF. La consolidación seleccionada crea
la síntesis, snapshots y vínculo con las sesiones en una sola transacción MySQL;
si alguien archiva o consolida durante la operación, se revierte la creación.
No se cambia el comportamiento de las exportaciones ni de la autenticación.

ArchivoSesionesChecks cubre recuperación, contenido, snapshots, permisos,
contadores, motivos inválidos y solicitudes obsoletas. La concurrencia y las
transacciones deben verificarse también con MySQL, no solo con InMemory.

## Eliminar archivadas y sus notas

Eliminar abre una pantalla de advertencia y análisis de referencias. Tras marcar
la confirmación explícita, se elimina la sesión archivada, sus notas y versiones,
y solo sus publicaciones propias exclusivas. Se bloquea si hay referencias a
otras síntesis, publicaciones informadas/filtradas, copias, relaciones externas o
un historial compartido. El POST repite el análisis y exige el mismo alcance
confirmado. La eliminación completa es transaccional y serializable en MySQL.
Se conservan trazas, auditoría y archivos físicos como respaldo; no se ejecuta
limpieza de archivos ni se alteran publicaciones ajenas.

## Síntesis sin notas aprobadas

Quitar sin notas aptas acepta Finalizadas/Archivadas con cero notas o solo notas
rechazadas; nunca pendientes o aprobadas. Desaparece de la bandeja, pero se guarda
una cabecera mínima con estado Eliminada (fecha/tipo/delegación) y un evento por
noticia. No se borran notas ni versiones. La delegación puede recuperarlas en un
nuevo borrador con el mismo ID e historial, o eliminarlas por su flujo habitual.
Las pantallas antiguas de aprobación/descarte no pueden reabrir esa cabecera.
El borrado definitivo de archivadas bloquea notas devueltas a la delegación.
