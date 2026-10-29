# Sesiones finalizadas sin notas aptas para consolidar

Una sesión Finalizada indica que terminó su revisión. Puede contener únicamente rechazos.
El total de notas registradas y el total de notas aprobadas para consolidar son distintos.

## Caso diagnosticado en test el 29 de octubre de 2026

Sesión 111, nota 215, versión actual 301. El evento NOTA_DESCARTADA del
27/10/2026 07:33:54 y la auditoría de descarte confirman el rechazo por Medios.
No se informó motivo. Al finalizar la sesión (07:40:06), el flujo quitó las referencias
de las síntesis en curso y convirtió la versión a Sin Remitir para que se pudiera corregir.
El código de las alertas dependía del motivo escrito y no reconocía ese rechazo sin motivo.
Consolidar excluía la nota correctamente, pero su mensaje decía erróneamente que no existían notas.

La corrección reconoce el evento de versión Descarte en las versiones Sin Remitir antiguas,
sin cambiar estados, versiones ni snapshots en la base. Las alertas conservan el criterio
de linaje: al recuperar la nota dejan de considerarla un descarte pendiente.

Los descartes nuevos sin explicación y los que se procesan al finalizar mantienen
la indicación Sin motivo informado. No se inventa una causa de rechazo.

En Finalizadas, una sesión sin notas aptas sigue disponible para consultar y muestra
Sin notas aptas; su selección está deshabilitada. Consolidar lista sus notas excluidas,
el estado y el acceso al historial. Seleccionar todas ignora esos registros.
El POST revalida cada sesión y rechaza la selección completa antes de crear o consumir
una consolidada si alguna sesión no tiene notas aptas. El paso Nacional/Provincial usa
la misma política, impidiendo ofrecer notas pendientes o descartadas.

No se aprueban descartes automáticamente ni se reparan registros en producción.
