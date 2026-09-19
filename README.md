# Coraza

Software de proyección para iglesias: canciones, versículos bíblicos, textos e imágenes, del PC al Video Beam.
Funciona **100 % sin internet**. Construido según el *Informe de Análisis y Diseño* (Fase 1 · MVP, Fase 2 · Experiencia y Fase 3 · Novedades).

> La única excepción es el aviso de versiones nuevas, que es opcional y se puede apagar en Configuración.
> Apagado, Coraza no hace ni una sola petición a internet. El control remoto desde el celular tampoco
> sale de la red local de la iglesia.

> Inspirado en Efesios 6:14, la coraza de justicia. El programa protege el momento del culto: no falla, no distrae
> y deja reaccionar al operador con rapidez.

- **Página del proyecto:** <https://john-elith.github.io/Coraza/>
- **Descargar la última versión:** [Releases](https://github.com/John-Elith/Coraza/releases/latest) — ZIP normal y ZIP portátil para memoria USB.

## Requisitos

- Windows 10 (21H2) u 11 de 64 bits.
- Para compilar: SDK de .NET 8 o superior (la aplicación apunta a `net8.0-windows10.0.19041.0` para leer PDF con el motor de Windows).
- Para usar la versión publicada: nada más; incluye el runtime de .NET.

## Compilar, probar y ejecutar

```powershell
dotnet build Coraza.sln
dotnet test tests\Coraza.Tests
dotnet run --project src\Coraza.App
```

Distribución sin internet (carpeta + ZIP autocontenidos):

```powershell
.\publicar.ps1            # datos en Documentos\Coraza
.\publicar.ps1 -Portatil  # versión para memoria USB (datos en la carpeta Datos)
```

## Qué incluye (Fase 1)

| Área | Funciones |
|---|---|
| Doble ventana | Ventana del operador y ventana de proyección sin bordes en el Video Beam. Con una sola pantalla se abre una ventana de ensayo redimensionable. |
| Pantallas | Detecta monitores y proyectores (con su nombre), resolución y proporción. Conexión en caliente: si el Video Beam se desconecta y vuelve, la proyección regresa sola. Aviso y botón cuando Windows está en modo «Duplicar». Per-Monitor DPI V2. |
| Canciones | Todo lo que se escribe se proyecta: una sección escrita en la letra (por ejemplo `[Final]`) que quedó fuera del orden se proyecta igual al final, y el editor lo avisa. Biblioteca con secciones etiquetadas (`[Verso 1]`, `[Coro]`, `[Puente]`…), orden de interpretación (`V1 C V2 C P C`), división automática en diapositivas, búsqueda instantánea tolerante a tildes y errores, favoritas, recientes, más usadas, papelera, historial de versiones, créditos CCLI. Asistente que detecta coros repetidos y propone el orden. Importa TXT, ChordPro y CCLI SongSelect. |
| Biblia | Reina-Valera 1909 (dominio público) incluida. Entiende «jn 3 16», «Juan 3:16-18», «1 cor 13», «sal 23 1-6», «Ef 6.14», «San Juan 3:16». Búsqueda por palabras (FTS5), selector de libros → capítulos → versículos, división de pasajes largos, número de versículo en superíndice, referencia en coral. Importa Zefania XML y VPL. |
| Medios | Imágenes (JPG, PNG, GIF, WebP, BMP, TIFF, HEIC con códec) desde archivo, carpeta/USB, arrastrar y soltar o Ctrl+V. Se copian a la biblioteca. Orientación EXIF y fotos de más de 50 MP. Modos rellenar/ajustar/estirar/centrar. |
| Textos | Anuncios y avisos con plantillas (bienvenida, ofrenda, evento, cumpleaños, bautizos). |
| Orden del servicio | Arrastrar desde las bibliotecas, reordenar, encabezados de sección con colores, notas del operador, tiempo estimado, autoguardado, abrir/duplicar servicios. |
| Temas | Cuatro temas con la paleta Coraza y editor con vista previa en tiempo real: fondo (color, degradado, imagen con oscurecido y desenfoque), tipografía, tamaño relativo, sombra, caja, pie, márgenes de seguridad, división y transición. Tema por tipo de contenido o por elemento. |
| Proyección | Motor por capas (fondo, contenido, logo, negro): cambiar la letra no reinicia el fondo. Transiciones por GPU: fundido cruzado, desenfoque, deslizamiento, acercamiento y corte; si se avanza durante una transición, se completa al instante. Ajuste automático del texto. Patrón de prueba. |
| Control en vivo | Vista previa + En vivo, botones de pánico (Negro, Logo, Ocultar texto, Limpiar), saltos por sección, búsqueda universal Ctrl+K, atajos personalizables y presentadores inalámbricos. |
| Confiabilidad | Evita la suspensión durante la proyección, la salida no se cierra por error, un error nunca muestra una pantalla de fallo, respaldo diario (se conservan 30), registro de errores. |

## Qué incluye (Fase 2)

| Área | Funciones |
|---|---|
| Transiciones | 15: corte, fundido, desenfoque, deslizar, acercamiento, línea por línea, palabra por palabra, máquina de escribir, barrido Coraza, rayo de luz, persiana, cortina, giro, Ken Burns y partículas. **Vista previa antes de aplicarlas:** al pasar el mouse por la lista, el efecto se demuestra en el recuadro «Vista previa» (también con el botón ▶). Si «Reducir movimiento» está activado en Configuración, la proyección cambia **sin animación**: la vista previa lo indica y Coraza lo avisa al elegir un efecto. |
| Fondos en movimiento | Aurora, Bokeh y Ondas (generados en tiempo real, sin archivos) y fondos de video en bucle y sin sonido; velocidad e intensidad ajustables en el editor de temas. |
| Video y música | Videos MP4, WMV, MOV…: reproducir, pausar, reiniciar, volumen y bucle; el sonido sale por una sola salida. Música de fondo (MP3, WAV…) independiente de las diapositivas. |
| Documentos y fotos | PDF (cada página es una diapositiva) y PowerPoint, con botón propio en Medios. La conversión usa PowerPoint o, si no está, **LibreOffice**; sin ninguno de los dos, Coraza explica cómo exportar el PDF a mano. Presentación de fotos desde una carpeta: avanza sola cada 6 s y se repite. |
| Textos enriquecidos | `**negrita**`, `*cursiva*`, `__subrayado__`, `==resaltado==`, `{coral}colores{/}` y listas con «- », con barra de formato y vista previa en vivo. |
| Superposiciones | Marquesina (texto que corre), mensaje urgente, reloj y cuenta regresiva (minutos u hora de inicio) con texto final. Avance automático y bucle por elemento del servicio. |
| Importadores | Canciones: OpenLyrics/OpenLP, OpenSong, ProPresenter 4–6, SongSelect (.usr y .txt), ChordPro y TXT. Biblias: módulos de e-Sword, MySword y MyBible, Zefania, OSIS, USFM y VPL; cambio rápido de versión. |
| Apariencia | Tres paletas para el operador: Vino Coraza (carbón neutro con el vino en la barra y los acentos), Oscuro grafito y Claro lino. Se cambian al instante con el botón de la luna. |
| Asistente inicial | Iglesia, pantallas y Video Beam (con patrón de prueba), paleta y tema, versión bíblica y licencia CCLI. Se puede reabrir desde Configuración. |
| Pantalla completa | Esc sale de la pantalla completa; Mayús + Esc limpia. |

## Qué incluye (Fase 3, en curso)

| Área | Funciones |
|---|---|
| Control remoto | Pasa diapositivas desde el celular por la **red local**, sin internet y sin cuentas. Se enciende en Configuración → Control remoto, que muestra un código QR y un PIN de 6 dígitos. Desde el teléfono: botones de pánico (negro, logo, ocultar texto, limpiar), avanzar y retroceder, saltar a una sección de la canción, recorrer el orden del servicio y **buscar** canciones o citas bíblicas para dejarlas en vista previa. |
| Seguridad | **Un solo teléfono a la vez**: si entra otro, el primero pierde el mando en el acto y se ve en el escritorio. El PIN vive solo en memoria, se regenera al conceder y al revocar, y 5 intentos fallidos bloquean un minuto. Reiniciar Coraza revoca cualquier sesión. El tráfico va por HTTP plano dentro de la red local: **basta contra un curioso en la sala, no contra una red hostil.** |
| Firewall | La ventana lee el estado sin pedir permisos y ofrece crear la regla con **una sola elevación**. Avisa cuando la red está en perfil «Público», donde la regla no se aplica, y muestra el comando de `netsh` por si hay que hacerlo a mano. |
| Actualizaciones | Comprueba como mucho **una vez al día** si hay una versión nueva y **la descarga sola, en segundo plano**: no hay que buscar nada en ninguna página. Después avisa en la barra de mensajes y el operador decide cuándo instalarla. **Nunca durante una proyección**: ni se avisa ni se instala mientras el culto está en marcha, porque instalar cierra el programa. Se puede omitir una versión concreta o apagar el aviso por completo. Sin internet no pasa nada: no hay error, ni espera, ni retraso en el arranque. |

## Atajos principales

| Atajo | Acción |
|---|---|
| → / Espacio / Av Pág | Siguiente diapositiva |
| ← / Re Pág | Diapositiva anterior |
| ↓ / ↑ | Siguiente o anterior elemento del servicio |
| Enter | Enviar la vista previa al vivo |
| C, V + número, P, R, F | Coro, verso, puente, pre-coro, final |
| B (o «.») · L · T · Esc | Negro · Logo · Ocultar texto · Limpiar (en pantalla completa, Esc sale de ella y Mayús + Esc limpia) |
| Ctrl + K · Ctrl + B · Ctrl + N | Buscar · Biblia · Nueva canción |
| F5 · F11 | Iniciar/detener proyección · Modo concentración |

## Estructura

```
Coraza.sln
  src/Coraza.Core        modelos, analizador de citas y letras, búsqueda, diapositivas, temas
  src/Coraza.Data        SQLite (FTS5), repositorios, importador de Biblias, respaldos
  src/Coraza.Rendering   motor de proyección, transiciones, auto-ajuste, patrón de prueba
  src/Coraza.App         WPF: ventanas, MVVM, estilos, pantallas, atajos
  tests/Coraza.Tests     pruebas unitarias y de integración
  docs/index.html        página pública; GitHub Pages sirve esta carpeta (rama main, /docs)
```

La página es un único archivo autocontenido. Sus botones de descarga apuntan a
`releases/latest/download/Coraza-win-x64.zip` y `...-portatil.zip`, que son los nombres exactos que
genera `publicar.ps1`: si se renombra un ZIP, los botones dan 404 aunque la página se siga viendo bien.

Datos del usuario: `Documentos\Coraza\` (Biblioteca, Medios, Fuentes, Temas, Servicios, Respaldos, Registros).
Si junto al ejecutable existe `portable.txt`, todo se guarda en la carpeta `Datos` (versión portátil).

## Próximas fases (hoja de ruta del informe)

- **Fase 3 · Novedades:** el control remoto desde el celular ya está (ver arriba). Quedan la pantalla de escenario, la salida NDI, el morfismo de texto, las alertas y el soporte de Stream Deck y MIDI.
- **Fase 4 · Inteligencia:** detección de versículos por voz, subtítulos en vivo, fondos inteligentes, sincronización opcional.

## Licencias

- Reina-Valera 1909: dominio público (fuente: eBible.org). **Conserva la ortografía de 1909**: «crió» por «creó», «fué», «dió» y la preposición «á». No son erratas y no se corrigen, porque cambiar el texto bíblico introduciría errores (por ejemplo «crió» sí significa *criar* en Hechos 7:21 o 1 Timoteo 5:10).
- Versión Biblia Libre (VBL): español actual, © 2018-2020 Jonathan Gallagher y Shelly Barrios de Avila,
  [CC BY-SA 4.0](http://creativecommons.org/licenses/by-sa/4.0/) (fuente: eBible.org). Se distribuye sin modificar el texto.
- Traducciones modernas (RVR1960, NVI, NTV…) tienen derechos y no se incluyen. Si la iglesia tiene permiso, se importan en
  **Biblia → Importar** desde un módulo de e-Sword (`.bblx`), MySword (`.bbl.mybible`), MyBible (`.SQLite3`), Zefania, OSIS o USFM,
  y luego se cambia de versión con un clic.
- Proyectar letras con derechos de autor requiere normalmente una licencia (CCLI o equivalente).
