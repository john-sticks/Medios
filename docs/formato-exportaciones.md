# Formato de PDF y Word

Las exportaciones usan los mismos snapshots, categorías, fuentes y enlaces de origen.
La fecha se presenta en Bookman Old Style negrita 20 pt.
CONTENIDO usa Arial negrita 16 pt, marrón #8B4513, 36 pt anteriores y 20 posteriores.
El índice usa Arial 10 pt, 2 pt anteriores, sin espacio posterior; sus números son Bookman 14 pt.
Categorías y ámbitos usan Bookman negrita 14 pt, #8B4513.
Los cuerpos se justifican; Word define interlineado mínimo 12 pt (no exacto).
El PDF conserva la altura natural de Bookman (aproximadamente 16,44 pt para 14 pt),
superior al mínimo solicitado y consistente con Word.
Las URLs originales son hipervínculos externos Bookman 12 pt, normales, #0F9ED5;
se conservan también cuando hay video. Se exporta la versión capturada en la síntesis,
sin reemplazarla por la primera versión ni agregar anotaciones de actualizaciones.

## Fuentes del servidor

Instalar las fuentes licenciadas del operador fuera de wwwroot en
fonts/ dentro de ContentRoot: BOOKOS.TTF, BOOKOSB.TTF, BOOKOSI.TTF,
BOOKOSBI.TTF, arial.ttf y arialbd.ttf.
No versionarlas ni publicarlas en Git. En producción se instalan en /opt/medios/fonts y Compose las monta en /app/fonts
como solo lectura. No se incluyen en la imagen publicada. En staging se conserva
la instalación privada del NAS.
En test están instaladas las fuentes exactas; sin ellas Linux mantiene URW Bookman
como sustitución existente. Al promover a otro servidor, instalar las fuentes allí
también antes de generar documentos. Word declara Bookman Old Style y Arial;
el visor debe disponer de esas fuentes para reproducirlas sin sustituciones.

Los PDF ya guardados, incluidos los reemplazados manualmente, no se reescriben
con esta actualización. El formato nuevo se aplica a las siguientes generaciones.
No hay migración de base de datos.

## Noticias y pie de página

Se mantienen 12 pt de separación entre notas. Si el título empieza con el nombre
del partido seguido de dos puntos (sin depender de tildes o mayúsculas), se omite
la línea adicional de partido/localidad y se subraya únicamente el nombre en el título.
El historial permanece disponible en la aplicación, sin imprimirse en PDF ni Word.
El pie se centra y más cerca del borde inferior:
División Medios O.S.INT en Arial normal 8; página actual y total en Bookman normal 14.

La nota final exclusiva de Vespertina usa Arial 8 cursiva, gris #606060, como la referencia reducida.

El margen superior físico sigue la referencia de 1,3 cm. Los 36 pt anteriores a CONTENIDO se aplican únicamente en el índice, sin repetirse al comenzar las páginas de noticias. La reserva de espacio para encabezados es pequeña y el cuerpo puede continuar en la siguiente página.
