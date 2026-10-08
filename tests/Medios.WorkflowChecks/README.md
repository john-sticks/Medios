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
