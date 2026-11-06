# Visibilidad de síntesis publicadas por delegación

DELEGACIÓN solo lista y descarga una consolidada informada cuando `CanalTodasDelegaciones` está activo o existe un destino de su delegación en `sintesis_delegacion_destino`. `CanalTodosRoles` no omite esa condición. La misma política se usa en consulta y descarga directa por ID, también para las filtradas y para todos los usuarios de la delegación.

Se conserva el acceso general de los demás roles y el acceso a la síntesis propia de la delegación. El envío de correo no concede visibilidad en plataforma. No se modifica ni hereda la selección de la síntesis de origen: las filtradas tienen sus destinos independientes.

No requiere migración ni reescritura de publicaciones existentes. Al desplegar, las publicaciones que solo tenían Todos los Roles y ningún destino dejan de ser visibles para DELEGACIÓN. Para habilitarlas hay que seleccionar expresamente la delegación o Todas las delegaciones al informar. No se borran PDF, notas, destinatarios ni registros de envío.
