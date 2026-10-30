# Promoción de funciones probadas en test — 29/10/2026

Incluye Word desde Finalizadas, reapertura para corregir con nuevas aprobaciones,
grupos de correo, reglas de títulos para doce portales y Monitor con Mantenimiento/Normal.
El código de respaldos y contingencia queda protegido por la doble habilitación exclusiva
para test. Compose productivo fija Deployment__Environment=Production y TestBackup__Enabled=false.
Se conserva la autenticación productiva: Cerberus, aprobación local y SUPERVISOR como DESARROLLADOR.

Antes de reiniciar la aplicación, conservar un dump privado de medios y aplicar:

1. sql/36_grupos_correo.sql: crea grupos_correo y contactos_correo si no existen.
2. sql/38_scraping_titulos_portales.sql: inserta/actualiza doce reglas de título.

No ejecutar los demás scripts históricos. No se trasladan datos de prueba, grupos,
usuarios, contraseñas ni configuraciones SMTP desde la DB de test.
No se modifican noticias existentes, PDF guardados ni las fuentes privadas de producción.
La regla que impide descargar Word cuando se subió manualmente un PDF se conserva.

El agente se instala aparte desde Monitor de test con código de un solo uso ingresado
por prompt privado. Su comunicación es HTTPS hacia medios.crmai.net.ar. Producción
no recibe credenciales SSH ni acceso MySQL del NAS. La instalación no reinicia Keryx.
No activar programación o contingencia sin verificar primero un respaldo recibido.
La conciliación de retorno sigue siendo supervisada: no hay importación automática.
