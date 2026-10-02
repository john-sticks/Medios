# Comprobaciones del flujo de síntesis

```bash
dotnet run --project tests/Medios.WorkflowChecks/Medios.WorkflowChecks.csproj -c Release
```

Ejecuta los servicios reales con EF Core InMemory y datos aislados de prueba.
Reproduce dos sesiones remitidas vacías junto a una publicación con dos notas;
verifica contadores de ambas bandejas y tres ciclos de publicar/modificar sin
crear sesiones adicionales. También verifica la creación de un borrador cuando
hay distintos orígenes y el bloqueo de modificaciones de síntesis remitidas.

El proveedor InMemory es una dependencia exclusiva de este proyecto de prueba.
No comprueba restricciones ni ejecución SQL de MySQL, ni genera PDF físicos.
La comprobación productiva se realiza después del despliegue manual, sin borrar
las sesiones vacías históricas ni ejecutar migraciones para esta corrección.
