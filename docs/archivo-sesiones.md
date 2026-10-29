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
