# Grupos de correo

Administración → Grupos de correo permite crear, editar, activar, desactivar y eliminar grupos.
Cada contacto tiene nombre, apellido y una dirección de correo. Un correo no puede repetirse
dentro del mismo grupo; sí puede pertenecer a varios grupos.

Acceso: MEDIOS y DESARROLLADOR, con ADMINISTRAR_CONFIGURACION (igual que Configuración Mail).
Las delegaciones no acceden al módulo.

Antes del despliegue, aplicar sql/36_grupos_correo.sql a la base del ambiente.
El script es idempotente y crea únicamente grupos_correo y contactos_correo.
No altera noticias, síntesis, autorizaciones ni configuración SMTP.

Al informar una consolidada o filtrada se muestran delegaciones con correo, grupos activos
con contactos y el campo de correos manuales. Los grupos se seleccionan explícitamente.
El servidor resuelve sus IDs y comprueba que continúen activos y con contactos.
Si alguno ya no está disponible, se detiene antes de reservar o enviar la síntesis.

Se unifican los destinatarios sin distinguir mayúsculas. El historial existente de envíos
conserva las direcciones utilizadas: editar/eliminar un grupo no cambia envíos anteriores.
No se envían correos al guardar un grupo.
