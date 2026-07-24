# Auditoría: métodos de I/O de disco y cobertura del `ProgressService`

> Fecha: 2026-06-23 · HEAD: `dfef315` (master) · working tree: solo este documento (sin trackear).
>
> Inventario de todos los métodos que **cargan o almacenan en disco** y de todos los que **reportan progreso**,
> indicando por método si notifica progreso, si bloquea la UI, si su entry del log es **reversible (undo)** y el
> mensaje exacto que ve el usuario. Líneas verificadas en este HEAD.

## Cómo funciona el `ProgressService`

El usuario ve el texto de `ProgressNotifier.Message`. Una operación se abre con `StartOperation()` (no bloquea la
UI) o `StartBlockingOperation()` (bloquea la UI), se reporta con `notifier.Message = …` + `ProgressNotifier.Report(...)`,
y se cierra con `FinishOperation()` / `FinishBlockingOperation()`.

Infra del feedback:
- **Progreso indeterminado:** `ProgressNotifier.IsIndeterminate` → `ProgressService.ProgressIsIndeterminate` → `ProgressBar.IsIndeterminate` (MainWindow/LoadingWindow). Lo usan descargas/copias y la carga lazy (sin % conocido).
- **Estado de error:** `ProgressNotifier.IsException` (se setea en los `catch` del servicio); `ConsoleControl` pinta esas entradas en rojo (`DangerBrush`, theme-aware vía `ThemeService`).
- **"App ocupada":** durante `StartBlockingOperation` (`SharedDataService.IsUIEnabled == false`) MainWindow muestra un overlay atenuado + `ProgressRing` + cursor de espera. No es un mensaje, es un estado visual global.
- **Prefijo unificado:** los mensajes de imagen anteponen `{platform}  |  {type}  |  …` (plataforma de `SharedDataService.SelectedPlatform`, tipo del set).
- **Carga lazy (operación "de fondo"):** `ProgressService.ReportLazyImageLoaded` (`:174`). UNA entrada COMPARTIDA por plataforma, creada con `StartBackgroundOperation` (`:148`, sin barra global), debounce 350 ms; conserva su posición y solo actualiza el mensaje. Su prefijo es **solo plataforma**, NO incluye `{type}`.
- **Undo (deshacer):** `ProgressNotifier` expone `UndoAction` (delegado opaco que cuelga la operación), `IsUndone`, `CanUndo` y `UndoCommand` (`AsyncRelayCommand`). `ConsoleControl` muestra un botón Undo por entry (visible si `CanUndo`); al deshacer, la entry se **atenúa** y se **tacha**. Cada operación reversible registra lo que hizo y cuelga su `UndoAction` del notifier que crea.

Valores de la columna *progreso*: **SÍ (directo)** el método abre/reporta; **SÍ (vía X)** lo hace un método al que llama; **NO** sin progreso.

---

## Resumen: TODOS los métodos que reportan progreso (13)

| # | Método | Ubicación | Bloquea UI | Undo | Mensajes (verbatim) |
|---|--------|-----------|------------|------|---------------------|
| 1 | `MatchImagesWithGamesAsync` ⚠️sin disco | `Services/ImageLoadingService.cs:244` | Sí | — | `{platform}  \|  {type}  \|  Loading images for {N} games` · `{platform}  \|  {type}  \|  {N} images for {N} games loaded` |
| 2 | `CreateImageFromUrlAsync` 🆕 | `Services/ImageLoadingService.cs:406` | No (indet.) | ✅ | `{platform}  \|  {type}  \|  {game}  \|  Downloading image... ({host})` · `…  \|  {fichero} downloaded` · error `…  \|  Image download failed` |
| 3 | `CreateImageFromFileAsync` 🆕 | `Services/ImageLoadingService.cs:481` | No (indet.) | ✅ | `{platform}  \|  {type}  \|  {game}  \|  Importing image...` · `…  \|  {fichero} imported` · error `…  \|  Image import failed` |
| 4 | `LoadFolderImagesAsync` | `Services/ImageLoadingService.cs:826` | No | — | `{folder}  \|  Folder not found` · `{folder}  \|  Scanning folder for images` · `{folder}  \|  Loading {N} images` · `{folder}  \|  {N} images loaded` |
| 5 | `LoadPlatformSetAsync` | `Services/PlatformLoadingService.cs:207` | Sí | — | `Processing platform.xml file...` · `Loading platforms...` · `Loading platforms ({platform})...` · `Preparing UI...` · error→diálogo `There was an error loading the platforms.` |
| 6 | `LoadGamesLbDatabaseAsync` | `Services/PlatformLoadingService.cs:262` | Sí | — | `Loading Launchbox games database...` · `{N} games from the Launchbox database loaded` · error→diálogo |
| 7 | `MatchImages` ⚠️sin disco | `Controls/ViewModels/GamesAuditInGalleryViewModel.cs:59` | No | — | `{platform}  \|  {type}  \|  {folder}  \|  Matched  {N}/{N} images with {N}/{N} games` |
| 8 | `OnDeleteOrphanClickedAsync` | `Controls/ViewModels/ImageAuditViewModel.cs:186` | Sí | ✅ | `{platform}  \|  {N} {type} orphan images deleted` |
| 9 | `OnGetImageDimensions` | `Controls/ViewModels/ImageAuditViewModel.cs:251` | No | — | `{platform}  \|  Retrieving dimensions of {N} {type} images` · `{platform}  \|  {N} {type} images' dimensions retrieved in {ms} ms ({K} slow fallback)` |
| 10 | `EnsureImageDimensionsLoadedAsync` | `Controls/ViewModels/ImageAuditViewModel.cs:809` | No | — | `{platform}  \|  Retrieving dimensions of {N} {type} images` · `{platform}  \|  {type} dimensions retrieved` |
| 11 | `LoadGameHighResImageBinariesAsync` 🆕 | `Services/ImageLoadingService.cs:361` | No | — | `{platform}  \|  {type}  \|  {game}  \|  Loading high‑resolution binaries ({i+1}/{N})` · `…  \|  Loading high‑resolution binaries completed` |
| 12 | `ReportLazyImageLoaded` (carga lazy, compartida por plataforma) 🆕 | `Services/ProgressService.cs:174` | No (indet.) | — | `{platform}  \|  Loading {N} images` · `{platform}  \|  {N} images loaded` |
| 13 | `ImportMatchedImagesAsync` 🆕 | `Services/ImageLoadingService.cs:594` | Sí | ✅ | `{platform}  \|  {type}  \|  Importing images ({i}/{N})` · `{platform}  \|  {type}  \|  {N} images imported` · parcial `…  \|  {K}/{N} images imported ({F} failed)` |

🆕 = nuevos/cambiados en la feature. ⚠️ = reportan progreso **sin tocar disco** (matching en RAM). ✅ (Undo) = la entry del log es reversible.

> **`ProcessImagesAsync` ya no existe:** se integró inline en `LoadGameHighResImageBinariesAsync` (#11).
> **`CreateImageFromUrlAsync`/`CreateImageFromFileAsync`** devuelven ahora `(image, notifier)` para que sus
> wrappers `AddImage…ToGameAsync` cuelguen el undo. Solo esos wrappers los llaman.

---

## A. Servicios de imágenes — `ImageLoadingService.cs` (+ `LocalFile`, `ImageBinariesCacheServices`)

| Método | Operación de disco | progreso | Mensaje(s) |
|---|---|---|---|
| `LoadGameHighResImageBinariesAsync` (`:361`) 🆕 | decodifica binarios alta-res (bucle inline `File.OpenRead` vía `LoadImageAsync`) | **SÍ (directo)** | ver #11 |
| `CreateImageFromUrlAsync` (`:406`) 🆕 | `File.WriteAllBytesAsync` (descarga web) | **SÍ (directo, indeterminado)** | ver #2 |
| `CreateImageFromFileAsync` (`:481`) 🆕 | `File.Copy` (importa local) | **SÍ (directo, indeterminado)** | ver #3 |
| `AddImageFromUrlToGameAsync` (`:542`) | delega en `CreateImageFromUrlAsync` + registra + **cuelga undo** | **SÍ (vía servicio)** | ver #2 |
| `AddImageFromFileToGameAsync` (`:560`) | delega en `CreateImageFromFileAsync` + registra + **cuelga undo** | **SÍ (vía servicio)** | ver #3 |
| `ImportMatchedImagesAsync` (`:594`) 🆕 | `File.Copy` en lote + borrado con backup (vía `FileSystemService`) si Discard | **SÍ (directo, blocking, determinado)** | ver #13 |
| `LoadFolderImagesAsync` (`:826`) | `Directory.EnumerateFiles` recursivo + dimensiones | **SÍ (directo)** | ver #4 |
| `LoadImageAsync` (`:157`) | `File.OpenRead` + `SetSourceAsync` (decodifica) | **NO** on-demand; SÍ vía `LoadGameHighResImageBinariesAsync` | — |
| `GetImageDimensionsAsync` (`:721`) | lee header de fichero | **NO** suelto; SÍ vía `LoadFolderImagesAsync` | — |
| `LoadImageDimensionsAsync` (`:766`) | lee headers en paralelo | **NO** si `progress==null`; SÍ vía llamador | — |
| `GetPlatformImageAssets` (`:311`) | `Directory.Exists`/`EnumerateFiles` (iconos/fanart) | **NO** | — |
| `BuildUniqueImagePath` (`:1027`) | `File.Exists` en bucle | **NO** | — |
| `MatchGameImages` (`:143`) | empareja ficheros con el juego (en RAM) | **NO** | — |
| `LocalFile.SetFileSize` (`Models/LocalFile.cs:94`) | `FileInfo.Length` (al construir cualquier `GameImage`) | **NO** | — |

> `MatchImagesWithGamesAsync` (`:244`) reporta progreso pero **NO toca disco** (matching en RAM). `ImageBinariesCacheServices` es caché en RAM, sin disco.

## B. Plataformas y LaunchBox — `PlatformLoadingService.cs`, `LaunchBoxService.cs`

| Método | Operación de disco | progreso | Mensaje(s) |
|---|---|---|---|
| `LoadPlatformSetAsync` (`:207`) | parsea `Platforms.xml` | **SÍ (directo)** | ver #5 |
| `LoadPlatformsAsync` (`:59`) | `File.Exists` + XML por plataforma | **SÍ (vía LoadPlatformSetAsync)** | `Loading platforms ({platform})...` |
| `LoadGamesLbDatabaseAsync` (`:262`) | abre SQLite `LaunchBox.Metadata.db` (read-only) | **SÍ (directo)** | ver #6 |
| `LoadPlatformImageFilesAsync` (`:105`) | escaneo recursivo de carpetas de imágenes | **SÍ (vía llamador)** | hereda `Loading platforms ({platform})...` |
| `LoadPlatformPacksAsync` (`:176`) | lee `LaunchBoxSettings.xml` | **SÍ (vía llamador)**, sin mensaje propio | — |
| `LaunchBoxService.InitializeAsync` (`LaunchBoxService.cs:60`) | orquesta arranque (XML + SQLite) | **SÍ (vía callees)** | los de #5/#6 |

## C. Config / persistencia / FS genérico / temas / logging — **ninguno reporta progreso**

| Método | Operación de disco | progreso |
|---|---|---|
| `FileSystemService.DeleteFileAsync` (`:35`) | borra (lanza si no existe) | **NO** |
| `FileSystemService.DeleteImageFileAsync` (`:62`) | backup (con backup-folder fijo) + borra; **devuelve la ruta del backup** (para el undo) | **NO** |
| `FileSystemService.RestoreImageFileAsync` (`:97`) 🆕 | restaura un fichero desde su backup (no lo borra). Lo usa el **undo** | **NO** |
| `FileSystemService.RenameFileAsync`/`GetNewFileName` (`:163`/`:124`) | mueve (`File.Move`) / sondea nombres | **NO** |
| `FileSystemService.LoadXmlDocument` (`:151`) | `File.ReadAllTextAsync` | **NO** |
| `GetImageDimensionsFromHeader` + `TryReadHeader/Bmp/Jpeg/Webp/ReadAt` (`:194`…) | `FileStream` lee cabeceras | **NO** |
| `PersistAndRestoreService.PersistData`/`RestoreData` (`:115`/`:145`) | escribe/lee `MM4LB.ini` (JSON) en `SettingsFolderPath` (`:36`, `%LocalAppData%\MM4LB`) | **NO** |
| `AppSettings…Validate` (`Models/AppSettings.cs:400`) | `Directory.Exists`/`File.Exists` de LaunchBox | **NO** |
| `ExceptionService.LogToFile` (`:80`) | `File.AppendAllText` a `MM4LB.log` | **NO** |
| `SetLaunchBoxFoldersViewModel.OnSave`/`OnLaunchBoxFolderChanged` | dispara `Validate` (lee disco) | **NO** |

> **Backup de imágenes:** ya no se controla por configuración. `DeleteImageFileAsync` hace backup SIEMPRE a `SettingsFolderPath\BACKUP\{plataforma}\{tipo}\{nombre}-{fileTimeUtc}{ext}` (subcarpeta de donde vive `MM4LB.ini`). Al vivir en `%LocalAppData%`, el backup es fiable y no aborta el borrado.
> `ThemeService` no toca disco (solo URIs `ms-appx:///`).

## D. ViewModels (orquestación)

| Método | Operación de disco | progreso | Mensaje(s) |
|---|---|---|---|
| `ImageAuditViewModel.OnDeleteOrphanClickedAsync` (`:186`) | **borra imágenes huérfanas** (con backup) · **reversible** | **SÍ (blocking)** | ver #8 |
| `ImageAuditViewModel.OnGetImageDimensions` (`:251`) | lee cada fichero (dimensiones) | **SÍ** | ver #9 |
| `ImageAuditViewModel.EnsureImageDimensionsLoadedAsync` (`:809`) | lee dimensiones perezosamente | **SÍ** | ver #10 |
| `GameImagesDashboardViewModel.RefreshSelectedGameImagesAsync` (`:426`) | carga binarios alta-res | **SÍ (vía `LoadGameHighResImageBinariesAsync`)** | ver #11 |
| `GameImagesDashboardViewModel.ResolveLocalImageAsync` (`:650`) | **copia** fichero soltado · **reversible** | **SÍ (vía servicio `AddImageFromFileToGameAsync`)** | ver #3 |
| `GameImagesDashboardViewModel.DownloadWebImageAsync` (`:677`) | **descarga** imagen soltada · **reversible** | **SÍ (vía servicio `AddImageFromUrlToGameAsync`)** | ver #2 |
| `ImageGridViewModel.LoadImageBinaryOnDemandAsync` (`:484`) | decodifica binario al hacer scroll | **SÍ (coalescido, indet., vía `ProgressService.ReportLazyImageLoaded`)** | ver #12 |
| `ImageGridGameViewModel.LoadGameImagesAsync` (`:412`) | escanea carpeta para emparejar | **NO** | — |
| `ImageGridGameViewModel.RefreshPlatformAverageCoverage` (`:224`) | "recorre ficheros" (cobertura) | **NO** (fondo cacheado) | — |
| `ImageCollectionImportViewModel.OnSelectFolder` (`:143`) | escanea+carga carpeta | **SÍ (vía `LoadFolderImagesAsync`)** | ver #4 |
| `ImageCollectionImportViewModel.OnImportImagesAsync` (`:96`) | **importa** las imágenes emparejadas al set · **reversible** | **SÍ (vía `ImportMatchedImagesAsync`)** | ver #13 |

> **Drag&drop del dashboard / WebView:** las altas pasan por `AddImage…ToGameAsync`, que registra (`ImageAddedToGame`) y cuelga el **undo**. El undo emite `ImageRemovedFromGame` (inverso) para que galerías, audit y la lista del dashboard reflejen la baja; las gráficas de stats refrescan vía `PlatformImagesChanged`.
> `GamesAuditInGalleryViewModel.MatchImages` (`:59`) reporta progreso **sin tocar disco** (ver #7).

## E. Arranque / ventana de carga — `LoadingWindow.xaml.cs`

| Método | Operación de disco | progreso |
|---|---|---|
| `LoadingWindow_Activated` (`:125`) | dispara la carga de arranque (sección B) | **SÍ (vía cadena → PlatformLoadingService)** — mensajes de #5/#6 |
| `AppWindow_Changed`/`GenerateRegionFromPngAsync`/`LoadRegionFromFile`/`SaveRegionToFile` | lee/escribe `.bin`/`.png` de la máscara del splash | **NO** |
| `App.OnLaunched` (`App.xaml.cs:206`) | `Directory.Exists`/`File.Exists` para decidir ventana | **NO** |

## F. Assets de UI (PNG en runtime, `ms-appx:///`, NO disco real)

`WidgetBaseControl.UpdateWidgetIconFromContent`/`UpdateCloseIcon`, `WidgetSelectorControl.CreateWidgetIcon`,
`ToolbarButtonIcon.UpdateThemeIcons` decodifican desde el paquete embebido, no desde fichero. Sin progreso.

## G. WebView — descarga de imágenes

Tres vías para añadir una imagen del navegador, **todas** confluyen en `WebViewViewModel.AddImageFromBrowserAsync`
(`Controls/ViewModels/WebViewViewModel.cs:281`) → `AddImageFromUrlToGameAsync` → `CreateImageFromUrlAsync`:

| Entrada | Ubicación | Operación de disco | progreso | Mensaje(s) |
|---|---|---|---|---|
| Menú contextual "Add to game images" | `WebViewControl.xaml.cs:269` | descarga+escribe imagen | **SÍ (vía servicio)** · reversible | ver #2 |
| Doble clic sobre imagen | `WebViewControl.xaml.cs:299` (script + WebMessage) | descarga+escribe imagen | **SÍ (vía servicio)** · reversible | ver #2 |
| Ctrl+clic sobre imagen | mismo `WebMessageReceived` | descarga+escribe imagen | **SÍ (vía servicio)** · reversible | ver #2 |

> Heredan el progreso (y el undo) del servicio. Sin overlay inline propio: feedback global (barra indeterminada + ACTIVITY LOG); el resultado aparece en el dashboard.

---

## Conclusión

- **Reportan progreso: 13 métodos.** El grupo de **añadir imagen** (descarga web / copia local) reporta a nivel de **servicio** (`CreateImageFromUrlAsync`/`CreateImageFromFileAsync`), cubriendo el drag&drop del dashboard y las 3 vías del WebView, con prefijo `{platform} | {type} | {game}`, **nombre del fichero final** y **estado de error en rojo**. La **carga alta-res** (#11) reporta directamente (bucle inline) con prefijo `{platform} | {type} | {game}`. La **carga lazy** (#12) reporta coalescida desde `ProgressService`, una entrada por plataforma (sin `{type}`), con tiempo acumulado entre ráfagas. El **import** (#13) copia en lote al set seleccionado (blocking, determinado, con borrado-con-backup si Discard).
- **Operaciones reversibles (undo desde el ACTIVITY LOG):** **#2, #3, #8, #13** (añadir imagen por descarga/copia, borrar huérfanas e importar). Cada una cuelga su `UndoAction` del notifier: borrar huérfanas restaura desde backup; el import quita las importadas y restaura las descartadas; añadir-imagen borra el fichero y desregistra. El undo emite `ImageRemovedFromGame` para refrescar galerías/audit/dashboard; las gráficas de stats vía `PlatformImagesChanged`.
- **I/O de disco que sigue SIN progreso (por diseño):** persistencia/config (`PersistData`/`RestoreData`), borrado/copia/rename/restore del `FileSystemService`, validación de rutas LaunchBox, logging, máscara del splash, `SetFileSize`, `BuildUniqueImagePath`, `GetPlatformImageAssets` — operaciones rápidas/internas. Y de **fondo**: `LoadGameImagesAsync` (escaneo de emparejado) y `RefreshPlatformAverageCoverage` (cobertura cacheada).
- **Siguen reportando progreso sin tocar disco** (matching en RAM): `MatchImagesWithGamesAsync` y `MatchImages`.
