# Comprobaciones del flujo de síntesis

```bash
dotnet run --project tests/Medios.WorkflowChecks/Medios.WorkflowChecks.csproj -c Release
```

Ejecuta los servicios reales con EF Core InMemory y datos aislados de prueba.
Reproduce dos sesiones remitidas vacías junto a una publicación con dos notas;
verifica contadores de ambas bandejas y tres ciclos de publicar/modificar sin
crear sesiones adicionales. También verifica la creación de un borrador cuando
hay distintos orígenes y el bloqueo de modificaciones de síntesis remitidas.

Comprueba además que MEDIOS y DELEGACION creen notas libres sin generar sesiones,
que MEDIOS conserve la aprobación directa al editar y que pueda incorporar la
nota posteriormente a su borrador. Rechaza incorporar notas de otra delegación.

El proveedor InMemory es una dependencia exclusiva de este proyecto de prueba.
No comprueba restricciones ni ejecución SQL de MySQL, ni genera PDF físicos.
La comprobación productiva se realiza después del despliegue manual, sin borrar
las sesiones vacías históricas ni ejecutar migraciones para esta corrección.

También comprueba publicación y reversión de síntesis propias de MEDIOS,
bloqueo de notas incluidas en publicaciones informadas, elegibilidad de notas
históricas para consolidar y el orden y formato del modelo PDF. El campo de
operador utiliza OperadorGenera y no requiere cambios del esquema.
Estas comprobaciones no reemplazan la verificación de restricciones MySQL;
el ciclo completo se verificó en test con MySQL antes de trasladarlo.

Incluye prevención de borradores duplicados, recuperación y eliminación de notas
rechazadas por la delegación, conservación de notas al eliminar solo el borrador,
selección de ámbito desde Categoría y bloqueo de finalización con notas pendientes.
Los contadores excluyen remisiones vacías. El modelo PDF agrupa por categoría y partido.
Las pruebas de autenticación de producción siguen en Medios.AuthChecks (Cerberus);
no se trasladan pruebas ni cambios del acceso Mock del NAS.

La exportación Word se valida con OpenXmlValidator y cuatro tipos de síntesis;
comprueba texto editable, portada, orden, fuentes periodísticas, historial,
restricción del aviso a Vespertina y conservación del estado y PDF originales.
Para generar muestras ficticias: agregar -- --word-samples <directorio> al comando.
El Action productivo también renderiza esas muestras con LibreOffice y conserva
las vistas previas durante cinco días. El DOCX es editable; la paginación depende
del procesador de textos y las fuentes instaladas.

InformarChecks verifica reserva persistente Informando, concurrencia por Estado, rechazo de doble POST, publicación para todos los roles y bloqueo de Word para Delegación. No envía correos reales. Si un envío queda con resultado incierto, revisar los registros e historial antes de recuperar manualmente su estado. No requiere migración de esquema.

RecuperacionChecks verifica recuperación sin duplicar notas, conservación del motivo anterior, historial de copias legadas y síntesis, y exclusión de alertas de Home/Borradores/Notas tras recuperar y remitir. Sin cambios de esquema ni fusión de registros existentes.

FiltradasChecks verifica origen informado, selección válida, snapshots congelados, envío independiente y protección frente a cambios posteriores. Aplicar sql/28_sintesis_filtradas.sql antes del despliegue.
