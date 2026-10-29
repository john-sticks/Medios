# Publicadas: PDF por delegación y exclusión de rechazadas

DELEGACION solo dispone de PDF en Publicadas. Detalle de una consolidada está
bloqueado también por URL (incluidos PDF originales y reemplazados). El detalle
de síntesis propias de la delegación se conserva para su flujo de trabajo.

El permiso de lectura depende de una delegación activa: propias o informadas
para Todos los Roles, Todas las Delegaciones o el destino específico. Todos los
usuarios de esa delegación tienen el mismo acceso; OperadorGenera no autoriza.
Sin delegación válida no se listan ni descargan documentos. Word permanece
prohibido a DELEGACION. Las acciones de modificación conservan además el control
del operador y no permiten gestionar consolidadas a una delegación.

Las rutas directas /pdf/ devuelven 404, incluso para usuarios autenticados:
los archivos se descargan mediante controladores que comprueban autorización.
También se valida la delegación en descarga/detalle de sesión y detalle de nota.

Detalle y contadores usan el mismo filtro de rechazos que las exportaciones
nuevas, incluyendo descartes legados sin motivo. No se borra información ni se
modifican PDF almacenados. Las filtradas conservan las versiones capturadas;
una versión snapshot marcada como descartada se excluye igualmente.

Sin migraciones de base, cambios de roles ni regeneración automática de PDF.
