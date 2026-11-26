## Paridad con UABEA Next 4 y 3× más rápido

### Texturas
- Se leen los datos que están **dentro del bundle** (`data.unity3d`, Addressables): se acabó el
  `no se pudo leer el bloque de datos (resS ausente)` en RGBA32, DXT1, DXT5, Alpha8 y demás.
- Se escanean los **Addressables de PC** (`*_Data/StreamingAssets/aa/<plataforma>`) y se abren los
  `*.unity3d`.
- Los objetos de bundle recuperan su **nombre real** (antes todos eran "Unnamed asset").

### MonoBehaviour
- Unity 6: se pasa el `RefTypeManager` correcto en todas las lecturas — se acabó el
  `refMan MUST be set to deserialize objects with ref types!`.
- Los objetos *stripped* se exportan con la plantilla vacía en lugar de fallar.
- Objetos con `SerializeReference`: antes salían como `"invalid value"`; ahora se exporta el JSON
  completo con `references.RefIds`.
- Export del juego de prueba: **17.919 objetos, 0 fallos** (antes 15.924 y 515 fallos), y el
  resultado es **reproducible** entre pasadas.

### Velocidad
- Export e import en paralelo (fase serie para nombres/plantillas/bytes + N hilos deserializando,
  cada uno con su propio reader) y escritor JSON propio sin árbol `JToken`.

| Operación (17.919 objetos) | antes | ahora |
|---|---|---|
| Export | 126 s | **43 s** |
| Import (dry-run) | ~170 s | **66 s** |

### Vista previa
- La imagen abre **ajustada al panel**; la rueda hace zoom **sobre el punto del cursor** (ya no se
  va a una esquina), se arrastra con el botón izquierdo y el doble clic vuelve a ajustar.
- Las imágenes pequeñas se amplían al ajustar y el tope de zoom sube a 64×.

### Buscador
- Ya no se congela ni cierra el programa: una sola búsqueda a la vez y lecturas protegidas.
- Encuentra textos que están en los bundles Addressables.

### Nuevo: Comparar con UABEA
Botón **Comparar con UABEA…**: eliges la carpeta que exportó UABEA y la nuestra, y dice cuántos
archivos son idénticos byte a byte, cuántos faltan (y cuántos son el mismo objeto con otro nombre)
y cuántos difieren, dejando un `comparacion_con_zub.csv`. También como `zub compare`.

### Instalación
Instalador por usuario (sin UAC, sin .NET), o el `.zip` portátil. Versión anterior: v1.0.1.
