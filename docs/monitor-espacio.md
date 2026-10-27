# Monitor de espacio

Ruta: /Monitor. Menú Administración → Monitor, exclusivamente DESARROLLADOR (nivel 10).
MEDIOS y DELEGACION no reciben menú, permiso ni acceso directo. En producción la identidad se
autentica en Cerberus y el rol efectivo proviene de la autorización local aprobada y activa.

## Alcance
- Capacidad total, ocupada y disponible de los volúmenes visibles de aplicación, PDF e imágenes.
  Se busca el montaje más específico de cada ruta (incluidos bind mounts). Pueden compartir
  almacenamiento físico; sus capacidades no se suman.
- No informa capacidad libre del servidor remoto de MySQL, del pool del NAS ni cuotas externas.
- Tamaño estimado de tablas e índices mediante information_schema.TABLES, restringido a DATABASE().
  No expone conexiones, credenciales, contenido de noticias ni rutas físicas en la vista.
- Cuenta noticias/versiones y promedia cuerpo + síntesis de las últimas 200 versiones.
  No recorre todos los cuerpos para calcular el promedio.
- Tamaño/cantidad de archivos PDF e imágenes; respaldos de PDF como subconjunto.
  Omite enlaces simbólicos y marca como parcial cualquier recorrido incompleto.
- Indicadores 80/90 % en pantalla; no envía correos ni Telegram.

## Historial y rendimiento
Servicio en segundo plano y caché compartida por proceso: medición cada 10 minutos, sin bloquear
el arranque. Cancelable al cerrar la aplicación. Una sola medición simultánea por instancia.
Persiste en configuracion.valor, clave monitor_espacio_historial_v1: última lectura diaria UTC,
hasta 60 días y límite de 60.000 bytes. No necesita migraciones ni altera otros parámetros.
Es compatible con el despliegue de test: el historial vive en DB y no se pierde por rsync/reinicio.
El diseño actual supone una instancia de aplicación; múltiples réplicas requerirían coordinar el
colector o separar su ejecución. La DB del historial y el almacenamiento deben seguir respaldándose.

Errores parciales no impiden iniciar ni usar Keryx. La vista diferencia datos ausentes de cero.
Los mensajes del motor/conexiones no se publican. Un JSON de historial inválido no se sobrescribe.

## Estimaciones
- Tendencia: diferencia entre primera y última lectura disponible de hasta 30 días, dividida por
  días transcurridos. Exige tres días distintos y al menos 48 horas; no representa un pronóstico
  garantizado. MySQL informa tamaños aproximados que pueden actualizarse con retraso.
- Días al 80 %: usa ocupación real del volumen, incluye otros archivos del servidor. Solo con
  crecimiento positivo. Se excluyen lecturas de capacidades distintas después de ampliar disco.
- Escenario de texto: promedio de cuerpo + síntesis × noticias/día × versiones/noticia × días.
  Es tamaño lógico adicional, no tamaño físico total: excluye índices, páginas reservadas,
  PDF e imágenes. Admite 0–100.000 noticias/día y 1–100 versiones/noticia.

No elimina ni archiva registros/archivos. La promoción a producción conserva Cerberus, conexiones y volúmenes propios de ese entorno.


## Acceso en producción y promoción

SUPERVISOR se consulta en el perfil info-servicio de Cerberus con el token y la clave de servicio,
solo después de verificar una autorización local aprobada y activa. Ese perfil recibe
DESARROLLADOR como rol efectivo. No se interpreta un rol enviado por el navegador.
La consulta tiene límite de tres segundos; si falla, el acceso local vigente se conserva,
pero no se concede el privilegio de Supervisor sin confirmación del servidor central.
Los demás roles conservan su asignación local.

La cookie registra el perfil de Cerberus y el rol local al ingresar. Un cambio de rol local,
una desactivación o cambio de delegación sigue revocando la sesión, incluso para Supervisor.
Las cookies anteriores mantienen su validación; para adquirir el nuevo rol de Supervisor
es necesario volver a ingresar. No hay una consulta remota de perfil en cada página.

La imagen se descarga antes de recrear el contenedor. Data Protection, PDF e imágenes siguen
persistidos en los volúmenes existentes. Compose admite hasta 45 segundos de cierre ordenado.
El servicio usa un solo contenedor y puede quedar brevemente indisponible durante el reemplazo;
no se garantiza continuidad de solicitudes que coincidan con el reinicio. Evitar enviar formularios
durante ese intervalo. No se ejecutan migraciones ni se modifican autorizaciones por SQL.
El monitor empieza a guardar su propia clave de estadísticas en configuracion al iniciar.
