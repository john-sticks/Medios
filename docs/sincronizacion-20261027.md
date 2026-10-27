# Sincronización funcional: test y producción — 27/10/2026

## Producción hacia test

Se trasladan los controles existentes de Cerberus: autorización local aprobada y activa para ingresar, validación del rol, rechazo de tokens vacíos y cierre de sesiones cuyos permisos fueron modificados. Se conserva el proveedor Mock de test y su normalización de nombres de usuario. Se incorporan sus 42 comprobaciones a CI de staging.

## Test hacia producción

- Archivo de PDF para MEDIOS: filtro por delegación y hasta 15 días, pestaña de consolidados, tamaño y fechas del almacenamiento y reemplazo del PDF conservando respaldo.
- Menú Síntesis sin acceso lateral a Consolidar; el botón aparece solamente en Finalizadas si existen sesiones finalizadas.
- Publicadas exige búsqueda con fechas, hasta 15 días; no carga todo al abrir.
- Un PDF reemplazado manualmente se identifica por origen y fecha. Se oculta su descarga Word porque no contiene esas modificaciones.
- Se conserva la autenticación Cerberus de producción y se incorpora la normalización de cuentas del proveedor Mock para comprobar ambos ambientes con las mismas pruebas.

## Base de datos

No se requieren ALTER TABLE, tablas nuevas ni actualización de autorizaciones de usuarios para estos módulos. El menú depende de XML; el reemplazo usa la columna existente sintesis.PDFPath. La anotación ConcurrencyCheck controla reemplazos simultáneos sin crear columnas.

Test fue verificado sobre MySQL 8.0.46: existen los campos de autorización local, filtros, PDFPath, fechas, canales de publicación y SintesisOrigenId. Los scripts históricos de creación/completado difieren entre repositorios y no deben ejecutarse indiscriminadamente sobre bases ya inicializadas.

Con autorización del usuario se agregaron en test CAÑUELAS (39), SAN VICENTE (40), TRES DE FEBRERO (41), con los partidos 51, 63, 113 respectivamente. En test salen de LA PLATA (12) y SAN MARTIN (22); los IDs internos de producción son distintos y no se copiaron. Se respaldó el catálogo antes de la transacción y se verificó que los demás partidos no cambiaran. Usuarios, notas y sesiones se conservaron.

Localidades de test / producción: Cañuelas 19/19, San Vicente 7/8, Tres de Febrero 15/21. Se mantienen los catálogos existentes; sincronizar localidades requiere una tarea separada de comparación de datos.

La configuración SMTP ya había sido trasladada de test a producción en una operación anterior. Esta sincronización no vuelve a copiar credenciales ni configuraciones privadas.

## Diferencias intencionales

Auth: Mock en test, Cerberus en producción. Conexiones DB, confianza del proxy conocido, valores predeterminados de Ollama, Docker y procesos de despliegue corresponden a cada red. No se reemplazan con los del otro ambiente.

## Validación

En cada repositorio: 357 comprobaciones WorkflowChecks y 42 AuthChecks, todas correctas. La prueba de archivo PDF incluye filtros, exclusión de otros roles, descarga exacta, respaldo, rechazo de cargas inválidas y detección de reemplazos simultáneos. CI vuelve a compilar y ejecutar las pruebas antes de publicar/desplegar.
