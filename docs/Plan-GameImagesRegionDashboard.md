# Plan — Widget GameImagesRegionDashboard

Estado: **propuesta para aprobar** (no se ha tocado código). Fecha: 2026-07-10.

## 1. Objetivo

Nuevo widget `GameImagesRegionDashboard`, casi idéntico al `GameImagesDashboard`, pero que
gestiona las imágenes del juego seleccionado **por región** en vez de todas juntas. Incluye:

- Selector de región visual (5 elementos fijos, cada uno con badge de nº de imágenes).
- Carga por región de las miniaturas + preselección de la imagen principal por región.
- Botón "Process region" (procesa la región activa sin cambiar de juego).
- Process & next / Process & previous adaptados: dejan **una imagen por región** (favoritas + sin
  región) y purgan o no las regiones no favoritas.

## 2. Decisiones cerradas (respondidas por el usuario)

1. **Estado sincronizado con el global**: el widget usa `SharedDataService.SelectedImage` como imagen
   principal (no un estado local). Sus miniaturas sí son la lista de la región activa (lista propia).
2. **Process & next/prev**: conservar 1 imagen por región favorita + 1 sin región; `keep-region`
   SIEMPRE true (las conservadas se quedan en su subcarpeta de región). Flag "purgar no-favoritas"
   (borrar todo el bucket "Otras regiones") por defecto **true**.
3. **Process region** sobre los buckets agrupados: "Sin región" conserva una; "Otras regiones" **no
   hace nada o borra todas** según el flag de purga. (Las regiones favoritas, una sola cada una.)
4. **Configuración solo por appSettings** (JSON) en la v1, sin editor en la ventana de Settings — igual
   que hoy pasa con los criterios de preselección/proceso del dashboard normal.

## 3. Modelo de regiones (selector de 5 buckets)

La región de una imagen es `GameImage.Region` (`ImageRegion`), derivada de la **subcarpeta hoja** del
fichero (`LocalFile.FileLeafFolder`), contrastada contra `Enums/ImageRegion.cs`. "Sin región" =
`ImageRegion.NoRegion` (Key 1, `Value == ""`). Catálogo canónico: 28 regiones.

El selector tiene **5 elementos SIEMPRE fijos**, en este orden:

1. Favorita #1 (por defecto `Europe`)
2. Favorita #2 (por defecto `World`)
3. Favorita #3 (por defecto `Spain`)
4. **Otras regiones**: imágenes con región que NO es ninguna de las 3 favoritas (y no es "sin región").
5. **Sin región**: imágenes con `Region == NoRegion`.

- La lista de favoritas (máx. 3) viene de appSettings; si hay menos de 3, se muestran solo esas + los
  buckets 4 y 5 (siempre presentes).
- Cada elemento muestra un **badge** con el nº de imágenes de ese bucket (0 incluido; los favoritos se
  muestran aunque tengan 0). Conteo puro en memoria agrupando `game.Images` por región (sin decode).
- **Selección por defecto**: el primer bucket (orden 1→5) que tenga imágenes.
- Al pinchar un bucket: se cargan sus imágenes en las miniaturas y se **preselecciona** su imagen
  principal (mismas reglas que hoy, aplicadas al subconjunto de esa región), fijando
  `SharedDataService.SelectedImage`.

Nota: en sets de vídeo (sin región) todas caen en "Sin región"; el widget sigue funcionando (buckets
favoritos a 0). Se considera caso degenerado aceptable.

## 4. Arquitectura y estrategia de reutilización

El widget se monta con el **mismo patrón end-to-end** que el resto (ver §7). Puntos clave:

- **VM nuevo** `GameImagesRegionDashboardViewModel : WidgetViewModelBase`. Reproduce del dashboard
  actual: layout horizontal/vertical, tamaños de miniaturas, volumen/mute de vídeo, calidad de
  descarga, drag&drop de import, panel de search-strings, botón de borrar. Añade: estado de región
  seleccionada, lista de buckets con conteos, lista de miniaturas de la región activa, y los comandos
  de proceso por región.
- **Estado compartido vs propio**:
  - Imagen principal: `SharedDataService.SelectedImage` (global, decisión #1).
  - Juego seleccionado / tipo de imagen: globales (`SelectedGame`, `SelectedImageSet`).
  - Miniaturas de la región activa: **colección propia del VM** (`RegionGameImages`), recalculada al
    cambiar juego/tipo/región o al alta/baja de imágenes.
- **Reutilización de UI**: el control reutiliza `GameImageControl` (preview + play badge + región) y el
  mismo patrón de `WidgetBaseControl` + `ViewModelConfigGate`.
- **Duplicación controlada**: para no desestabilizar el dashboard que ya funciona, en la v1 se
  **duplica** el VM/Control adaptándolos, y se extraen a helpers compartidos solo las piezas de bajo
  riesgo (preselección y, si procede, resolución de drops). Queda anotado como deuda una posible
  refactor futura a una clase base común `GameImagesDashboardViewModelBase`.
- **Coordinación de `SelectedImage`**: tanto el dashboard normal como el de regiones fijan
  `SelectedImage` al cambiar de juego. Con ambos visibles, "gana" el último en ejecutarse; es el
  comportamiento aceptado en la decisión #1. Se documentará en el código.

## 5. Preselección por región

Se refactoriza la preselección actual (`PreselectGameImage`, cascada Dimensions→Size→extensión) a un
**helper que recibe un `IEnumerable<GameImage>` + criterios** y devuelve la imagen preseleccionada.
El widget de regiones lo invoca con el subconjunto de la región activa. Los criterios de selección son
propios del widget (su sección de appSettings), con los mismos defaults que el dashboard normal.

## 6. Procesado por región

### 6.1 Botón "Process region" (región activa, sin cambiar de juego)
- Regiones favoritas (bucket 1–3) y "Sin región" (bucket 5): conservar la **preseleccionada** de esa
  región, borrar el resto de imágenes de esa **misma** región, y renombrar la conservada. `keep-region`
  siempre true (se queda en su subcarpeta; "sin región" no tiene subcarpeta).
- "Otras regiones" (bucket 4): según el flag de purga → **no hace nada** (flag off) o **borra todas**
  las imágenes del bucket (flag on).
- No cambia el juego seleccionado. Operación bloqueante con barra + entrada en el ACTIVITY LOG + undo,
  como el proceso actual.

### 6.2 Process & next / Process & previous (todo el juego, por región)
Equivale a "procesar todos los buckets" y luego navegar:
- Por cada región favorita con imágenes: conservar preseleccionada, borrar resto de esa región,
  renombrar conservada (keep-region true).
- "Sin región": conservar preseleccionada, borrar resto, renombrar.
- "Otras regiones": según flag de purga → borrar todas (default) o conservar una por región.
- Todo como **una única operación deshacible** (undo combinado de borrados + renombrados).
- Después, navegar al juego siguiente/anterior en `GamesFiltered` (misma lógica de bordes que hoy).

### 6.3 Servicio
- Nuevo método en `ImageLoadingService`, p. ej. `ProcessGameByRegionsAsync(game, favouriteRegions,
  processingCriteria, purgeNonFavourites, onlyRegion?)`, que:
  - Construye el conjunto "conservar" (preseleccionada por región según reglas) y el conjunto "borrar".
  - Reutiliza `DeleteMediaToBackup` (borrado por fichero + backup + eventos) y
    `Utilities.ImageFileNameToProcessedImageFileName` + `RenameFileAsync` para el renombrado.
  - Arma un único `UndoAction` combinado (rename + borrados), igual que `ProcessGameAsync`.
  - `onlyRegion` permite reusar el mismo motor para "Process region" (una sola región) y para
    process&next (todas).
- Se conserva `ProcessGameAsync` intacto para el dashboard normal.

### 6.4 Parámetros de proceso separados por dashboard
El widget de regiones tiene **su propia** sección de criterios en appSettings (independiente del
dashboard normal): criterios de selección + criterios de proceso (con `Region = Keep` fijado) + lista de
favoritas + flag de purga.

## 7. Alta del widget end-to-end (puntos de edición)

Basado en el patrón verificado (dashboard actual):

1. **AppSettings** (`Models/AppSettings.cs`): nueva propiedad de sección
   `GameImagesRegionDashboardControl` (zona L26–44) + clase anidada
   `GameImagesRegionDashboardControlSettings` (junto a L243), con: layout/thumbnails/vídeo (espejo del
   dashboard), `ImageSelectionCriteria[]`, `ImageProcessingCriteria[]` (Region=Keep),
   `FavouriteRegions` (`ImageRegion[]` default `[Europe, World, Spain]`), `PurgeNonFavouriteRegions`
   (bool, default true). Arrays (no List) para reemplazo limpio al restaurar.
2. **Persistencia** (`Services/PersistAndRestoreService.cs`): registrar
   `EnumerationJsonConverter<ImageRegion>()` en los **3** bloques de converters (L39–49, L127–134,
   L172–180), porque `ImageRegion` es `Enumeration` y hoy no está registrado.
3. **VM** nuevo `Controls/ViewModels/GameImagesRegionDashboardViewModel.cs` (`: WidgetViewModelBase`),
   con `LoadConfig`/`SaveConfig`/`Dispose`.
4. **Control** nuevo `Controls/Views/GameImagesRegionDashboardControl.xaml(.cs)` con DP `ViewModel` +
   `ViewModelConfigGate` (copia adaptada del dashboard) + el selector de región.
5. **DI** (`App.xaml.cs`, junto a L98–99): doble `AddSingleton` (VM + `IWidgetViewModelBase`).
6. **MainWindowViewModel** (`ViewModels/MainWindowViewModel.cs`): propiedad pública del VM + inyección
   en el ctor. (El alta en el selector y la persistencia de slot son automáticas vía `Widgets`.)
7. **MainWindow.xaml** (L117–146): `WidgetBaseControl` envolviendo el nuevo control
   (`TitlePrefix="REGIONS"` o similar).
8. **MainWindow.xaml.cs** (`OnWindowLoaded`, L120–132): añadir la `WidgetEntry`.
9. **Iconos**: el icono se resuelve por el nombre del tipo del control
   (`Widgets/GameImagesRegionDashboardControl.png` + `-off.png`) en los 4 temas → **8 PNGs**. Ver §9.

## 8. Fases de entrega (incrementales, compilando en cada una)

- **Fase A — Andamiaje + config**: sección AppSettings + converter `ImageRegion` + VM/Control vacíos +
  DI + alta en MainWindow + entrada en el selector. El widget aparece y se puede colocar en un slot,
  aún sin lógica de región (muestra placeholder). *Verificable: el widget se añade/quita del layout.*
- **Fase B — Modelo de regiones + selector**: agrupación de `game.Images` en los 5 buckets con
  conteos, selector visual con badges, selección por defecto y cambio de región. *Verificable: cambiar
  de juego/tipo actualiza buckets y badges; pinchar un bucket cambia la lista.*
- **Fase C — Miniaturas + preview + preselección**: lista de miniaturas de la región activa, preview
  con `GameImageControl`, preselección por región fijando `SelectedImage`, layout H/V, vídeo,
  drag&drop, borrar. *Verificable: navegación visual completa por región.*
- **Fase D — Procesado**: helper de preselección extraído, `ProcessGameByRegionsAsync`, botón "Process
  region" y adaptación de process&next/prev con el flag de purga y keep-region. Undo + progreso.
  *Verificable: procesar una región y process&next dejan el estado esperado y son deshacibles.*

## 9. Riesgos y dependencias

- **Iconos (8 PNGs)**: sin ellos, el widget cae al icono `DefaultWidget`. Propuesta v1: aceptar el
  fallback y añadir los PNGs definitivos más adelante (hay ya un TODO de iconos pendientes). Confirmar.
- **Coordinación de `SelectedImage`** entre ambos dashboards visibles (decisión #1): comportamiento
  "último gana"; documentado, no bloqueante.
- **Undo de proceso multi-región**: hay que probar bien el undo combinado (varios borrados de distintas
  regiones + varios renombrados) para que restaure rutas y subcarpetas de región correctamente.
- **`ImageRegion` en persistencia**: si se omite el converter, las favoritas se serializan mal. Incluido
  en la Fase A.
- **Duplicación VM/Control**: deuda técnica asumida en v1; refactor a base común como mejora futura.

## 10. Fuera de alcance v1

- Editor en la ventana de Settings (regiones favoritas + flags): se hará por appSettings.
- Refactor a clase base común de los dos dashboards.
- Iconos temáticos definitivos (se acepta el fallback si no se aportan).
