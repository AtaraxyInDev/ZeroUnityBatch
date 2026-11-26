# ZeroUnity Batch 1.0.2

Motor: AssetsTools.NET (el mismo de UABEA Next).
Interfaz: Avalonia 11 + AvaloniaEdit (editor virtualizado con resaltado).

Ejecutable: dist\ZeroUnityBatch.exe
  --open "carpeta del juego"
  --open "carpeta" --search "texto"
  --open "carpeta" --import "carpeta de cambios"

## Flujo de trabajo (como UABEA)

1. Abrir la carpeta del juego.
2. Elegir tipos en el desplegable.
3. Cargar los cambios con **Reimportar > Cargar cambios de una carpeta/archivo**
   (sirve cualquier JSON/TXT exportado por esta herramienta o por UABEA).
   Los cambios quedan EN COLA: no se escribe nada.
4. La columna **MOD.** marca con ● cada objeto con cambio preparado y
   **Modificados (N)** filtra la tabla para revisar solo esos.
5. **Guardar todo (N)** escribe cada contenedor una sola vez, verifica byte a byte
   y deja .zub_bak.

Preparar/Descartar trabajan igual sobre un objeto individual.

## Búsqueda

- Escribir filtra por nombre, PathID o contenedor.
- Intro busca dentro del contenido:
  - `Continue`  -> valor en los bytes.
  - `"m_Text": "Continue"` -> valor y campo.
  - `"m_Text":` -> solo el campo (sale de la plantilla del script).
- Al abrir un resultado, el editor **salta a la línea de la coincidencia**
  y la deja seleccionada.

## Vista previa

- Ctrl+F dentro del objeto: buscar, ▲▼, Reemplazar y Todo.
- Editar / Vista alternan solo lectura.
- Objetos de cientos de KB se abren al instante.

## Novedades

- Arrastra una carpeta (o un .exe) a la ventana para abrir el juego.
- Arranca maximizada; recuerda el ancho del panel derecho y de las columnas
  (%APPDATA%\ZeroUnityBatch\layout.json).
- Botón **Abrir juego** para probar los cambios.
- Carpetas Android (APK descomprimido): detecta sola `assets/bin/Data`.
- La búsqueda mira también los cambios preparados, no solo el disco:
  si preparas un texto y buscas la traducción, aparece.
- **Vista previa**: la imagen abre ajustada al panel, la rueda hace zoom sobre el
  punto donde está el cursor, se arrastra con el botón izquierdo y el doble clic
  vuelve a ajustar. Las imágenes pequeñas se amplían (hasta 8×) al ajustar.
- **Texturas**: se leen aunque los datos estén dentro del bundle o del `.unity3d`
  (antes salía "no se pudo leer el bloque de datos (resS ausente)").
- **Objetos de bundle con nombre real** (antes todos "Unnamed asset").
- **Comando `compare`**: compara tu carpeta exportada con UABEA contra la nuestra
  y dice exactamente qué falta y qué difiere (ver "Compatibilidad con UABEA").
- Exportación e importación en paralelo: ~3× más rápido.

## Portátil: no hace falta instalar nada

Todo lo de Android viaja en `dist\tools\` (unos 92 MB):

```
dist\ZeroUnityBatch.exe      ← el programa
dist\tools\jre\              ← Java propio (jlink)
dist\tools\apktool.jar
dist\tools\zipalign.exe
dist\tools\apksigner\apksigner.jar
dist\tools\debug.keystore    ← firma de depuración (pass: android)
dist\tools\Il2CppDumper\
```

Si esas carpetas están, el programa las usa y no busca nada en el sistema
(`Comprobar herramientas` lo indica como **PORTÁTIL**). Si falta alguna, cae a las
rutas habituales de `C:\Android` como respaldo.

## Instalador (Windows)

`installer\out\ZeroUnityBatch-Setup-1.0.0.exe` (97 MB). Instala para el usuario
actual, **sin permisos de administrador**, en `%LOCALAPPDATA%\Programs\ZeroUnity Batch`:
exe *self-contained* (no hace falta tener .NET), `classdata.tpk`, `tools\` completo
(jre, apktool, zipalign, apksigner, keystore, Il2CppDumper), accesos directos
en Inicio y escritorio, y desinstalador.

Para rehacerlo:

```
dotnet publish src\Zub.App\Zub.App.csproj -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o installer\stage
xcopy /E /I /Y dist\tools installer\stage\tools
"C:\Users\mrzero\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer\ZeroUnityBatch.iss
```

Comprobado: instalación silenciosa en carpeta limpia, `--tools` contesta
`portátil=True` (usa su propio `tools\`), `--selftest` sobre Third Crisis Neon Nights
indexa 555.549 objetos con la base 2022.3.62f3, y el desinstalador no deja restos.

## Menú Android (todo dentro del programa)

- **Abrir .apk (descomprimir)…** — apktool saca el APK a una carpeta y la abre.
- **Generar DLLs (Il2CppDumper)** — lee `libil2cpp.so` + `global-metadata.dat` y deja las DLLs
  en `ZubDummy/DummyDll`; luego recarga el proyecto con nombres y campos reales.
- **Empaquetar y firmar APK…** — apktool b → zipalign → apksigner con
  `~/.android/debug.keystore` (contraseña `android`). Borra los intermedios.
- **Comprobar herramientas** — dice qué encontró (java, apktool, zipalign, apksigner, keystore).

Busca sola: `C:\Android\ApkTool\apktool.jar`, `C:\Android\build-tools\*\`,
`C:\Android\UberApkSigner\` y el JDK de Adoptium.

## Reutilizar JSON de la build de PC

Al reimportar, cada JSON se valida contra el objeto destino: no se empareja nada que luego
falle al escribirse. El registro resume `exactos / por nombre / por PathID / copias / sin pareja`.

## Ports Android (APK descomprimido)

- Detecta `assets/bin/Data` y une los serializados troceados (`.split0`, `.split1`, …),
  que es donde viven `resources.assets` y los `sharedassets` del juego.
- Abre los `*.bundle` de `assets/aa/Android` (Addressables).
- Busca las DLLs del juego para leer los campos de los MonoBehaviour:
  `DummyDll` junto al juego o `ZubDummy/DummyDll` (salida de Il2CppDumper).
  Sin DLLs solo se ven los campos básicos (`m_Name`, `m_Enabled`…).
- Los cambios en `.split` se guardan uniendo, reescribiendo y volviendo a partir,
  con verificación y `.zub_bak`.

## Limitaciones de los ports Android

- Los assets **dentro** de un `.bundle` ya se guardan: se reescribe el serializado interno y se
  vuelve a comprimir el bundle con el mismo formato (LZ4/LZMA). Tarda ~30-50 s por bundle de 92 MB
  porque recomprime el archivo entero, igual que UABEA.
- Los JSON que no emparejan suelen ser de otra versión del juego (PC vs Android).

## Compatibilidad con UABEA

Los JSON exportados por UABEA Next 4 se leen tal cual (mismo formato), incluidos
los que llevan `-CAB-<hash>-` en el nombre (AssetBundles/Addressables).

Para saber cómo de iguales vamos en un juego concreto, exporta el mismo tipo de
objeto con UABEA y con nosotros y compara las dos carpetas:

```text
ZeroUnityBatch.exe compare "ruta\export_uabea" "ruta\export_zub" --exact --out informe.csv
```

Dice cuántos archivos son idénticos byte a byte, cuántos nos faltan (o van con otro
nombre) y cuántos difieren; con `--exact` compara el contenido, no solo el tamaño.
Devuelve 3 si hay cualquier diferencia, así se puede usar en un script.

Ejemplo real (ZH-GALxSurvival, 17.920 objetos): 17.583 idénticos, 24 con otro
nombre, 1 sin pareja y 312 distintos — de esos 312, **311 son objetos que UABEA
tampoco pudo leer** (`Asset failed to deserialize.`) y en 189 de ellos nosotros sí
sacamos el JSON completo.

## Pendiente

- Reimportar Texture2D.
- Texturas Crunched (crnlib).
- Addressables (StreamingAssetsa).
