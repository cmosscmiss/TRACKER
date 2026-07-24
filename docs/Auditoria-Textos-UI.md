# Auditoría de textos de UI — MM4LB

> Fecha: 2026-07-17
> Alcance: **labels de todos los botones** y **todo el texto de interfaz que NO es de datos** (títulos, cabeceras,
> tooltips, placeholders, mensajes de progreso/error, títulos y botones de diálogos, textos de pills/gráficas…).
> Se excluyen: valores enlazados por binding (`{x:Bind}`, `{Binding}`, `{StaticResource}`, `{ThemeResource}`),
> iconos (`Glyph`/`FontIcon`/`IconName`), datos de dominio (nombres de plataformas/juegos, cifras), claves de
> recurso, rutas/URIs, comentarios de código y logs internos que no llegan a la UI.

## Metodología

Extracción exhaustiva de literales de texto sobre:

- **57 ficheros XAML** (40 en `Controls/Views`, 8 en `Controls/Dialogs`, 3 en `Views`, 5 en `Resources`, `App.xaml`).
- **Code-behind** (`*.xaml.cs`) de `Controls/Views`, `Controls/Dialogs`, `Views`.
- **ViewModels** (`ViewModels`, `Controls/ViewModels`) y **Services** (mensajes de progreso, errores, diálogos, pills).

Los `Resources/*.xaml` (Buttons, Controls, Theme, Typography, GenericControls), `App.xaml`, `LoadingWindow.xaml` y
`AppDialog.xaml` **no contienen texto literal de UI** (solo estilos/plantillas; los textos de `AppDialog` los aporta
`DialogsService` en código, ver §4).

---

## 1. Inventario — Controls/Views (XAML)

### AboutSettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 28 | Heading | MM4LB |
| 29 | Heading | Media Manager for LaunchBox |
| 38-39 | Heading | Organiza, empareja y audita la media de tu colección de LaunchBox: imágenes por tipo y región, vídeos, cobertura por plataforma y herramientas de limpieza — todo desde un panel de widgets configurable. |
| 44 | Heading | DETAILS |
| 46 | Heading | Build |
| 49 | Heading | Runtime |
| 52 | Heading | Architecture |
| 55 | Heading | Data source |
| 63 | Heading | THIRD-PARTY COMPONENTS |
| 78 | Heading | El color de acento sigue al tema activo. |
| 79-80 | Heading | © 2026 MM4LB. No afiliado a LaunchBox / Unbroken Software. Todas las marcas pertenecen a sus respectivos dueños. |

### AuditPanelControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 31 | AppBarButton | Check media |
| 33 | AppBarButton | Only discrep. |
| 34 | AppBarButton | Selected media type |
| 42 | Label | Imported file |
| 45 | InfoBar Title | Warnings |
| 51-56 | Header | Game / Category / LaunchBox / MM4LB / Δ / Status |
| 70 | Heading | Run a check to compare the LaunchBox audit Excel with the media matched by MM4LB. |
| 77 | Formato | Showing {0} of {1} |
| 90-94 | Label/Desc | Compared / Discrepancies / Wrong files / Not matched / Not in Excel (con subdescripciones) |

### ChartTypeSelectorControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 31, 42, 59, 75 | ToolTip | Help / Chart type / Top N / Sort |
| 45-50 | Menú | Bars / H-Bars / Line / Area / Pie / Ring |
| 62-67 | Menú | Top 5 / Top 10 / Top 20 / Top 50 / Top 100 / All |
| 78-80 | Menú | No sort / Asc ↑ / Desc ↓ |

### ConsoleControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 50, 53 | ToolTip | Cancel / Undo |
| 69-70 | Label/Desc | Cached media files / Backup media files (con subdescripciones) |
| 72 | AppBarButton + ToolTip | Backup · "Clean backup folder" |
| 80 | Heading | Cache usage: |
| 90 | Label/Desc | Cache usage / % used |

### FooterEventViewerControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 76, 81 | ToolTip | Cancel / Undo |
| 89, 93, 97 | ToolTip | Older event / Newer event / Latest event |

### FooterSoundControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 14, 41 | ToolTip | Sound / Mute |

### GameDetailsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 42 | Heading | In LaunchBox database |
| 51 | Heading | Not in LaunchBox database |
| 113, 115 | Label/Desc | Known images / Media types |
| 121 | Heading (vacío) | Selecciona un juego para ver su ficha |

### GameImageControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 77, 92, 140 | Formato | {0} Kb |
| 139-145 | Label | Dimensions / File size / Extension / Region / Quality / Duration |

### GameImagesDashboardControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 78, 87 | Header | Layout / Video quality |
| 79-80 | Opción | Hor. view / Ver. view |
| 88-92 | Opción | 240p (LD) / 360p (SD) / 480p (ED) / 720p (HD) / 1080p |
| 96, 103, 110 | AppBarButton | Strings / Delete / Settings |
| 110 | ToolTip | View / change pre-selection and processing settings |
| 190 | Estado | Importing image... |
| 259, 272 | Heading | PROCESS & PREVIOUS / PROCESS & NEXT |
| 289, 293 | ToolTip | Process this game and go to the previous/next one |

### GameImagesRegionDashboardControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 118, 125 | Header | Layout / Video quality |
| 119-120 | Opción | Hor. view / Ver. view |
| 126-130 | Opción | 240p (LD) / 360p (SD) / 480p (ED) / 720p (HD) / 1080p |
| 134, 140, 147, 154 | AppBarButton | Strings / Delete / Process region / Settings |
| 147 | ToolTip | Process the selected region (keep one, delete the rest) without changing game |
| 154 | ToolTip | View / change pre-selection and processing settings |
| 245 | Estado | Importing image... |
| 298, 310 | Heading | PROCESS & PREVIOUS / PROCESS & NEXT |
| 323, 327 | ToolTip | Process all regions of this game and go to the previous/next one |

### GameListControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 42 | Placeholder | Filter games |
| 47-49 | Toggle | Missing / 1 media file / > 1 media file |
| 77 | Formato | Showing {0} of {1} |

### GamesAuditControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 24-26 | AppBarButton | In collection / In LB Db / Not in LB Db |
| 29-31 | AppBarButton | No matches / One match / >1 match |
| 48-51 | Header | Title / Launchbox ID / Rom / Version |
| 57-59 | Header | Title / Launchbox ID / Matched Images |
| 67 | Formato | Showing {0} of {1} |

### GeneralSettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 17, 26 | Heading | Toolbar button groups / Image cache size (MB) |
| 35-38 | Toggle | Show widget header bar / Always show activity log in footer / Confirm before deleting media / Log exceptions to file (MM4LB.log) |

### ImageAuditControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 27-29 | AppBarButton | In use / Shared / Orphan |
| 31 | AppBarButton + ToolTip | Dimensions · "Retrieves the dimensions of the images (takes time)" |
| 36 | AppBarButton | Orphan |
| 38-40 | Header/Opción | View / List view / Grid view |
| 58-68 | Header | File name / Size (Kb) / Quality / Dimensions / Duration / Extension / Region / # games / Game(s) |
| 88, 92 | Heading/TeachingTip | Media set characteristics - Media type (selected) |
| 89 | ToolTip | Help |
| 92 | TeachingTip subtitle | Composition of the selected media type's files by different criteria. Each bar stacks the most common value plus 'Others'… |
| 109 | Formato | Showing {0} of {1} |

### ImageCollectionImportControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 23, 25 | AppBarButton | Folder / Import |
| 31, 36 | AppBarButton | Media / Games |

### ImageGridControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 26, 29 | Header | Aspect ratio / Resolution |
| 32 | AppBarButton | Delete |
| 91 | Label/Desc | Coverage (favs.) / Game / Platform |

### ImageTypeControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 40-42 | Toggle | With images / **Wihout images** (typo) / Favourites |

### MediaTypesSettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 14 | Heading | Select up to 10 favourite media types (shown as quick buttons in the media type band). |

### OrphanToolControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 26-33 | Header/AppBarButton | View / Table view / Grid view / Selected type / Delete all |
| 50-52 | Header | Type / File / Region |
| 60 | Estado | No orphan media found for this platform. |
| 68 | Formato | Showing {0} of {1} |
| 80-83 | Header/Desc | Orphans / Size / Types (con subdescripciones) |

### PlatformDetailsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 84 | Estado | Drag an image or video here |
| 116 | Estado | Importing... |
| 168, 172 | Heading/TeachingTip | Coverage - Platform |
| 169 | ToolTip | Help |
| 172, 192, 204, 216 | Descripción | (descripciones largas de las 4 gráficas de cobertura) |
| 192, 204, 216 | Header | Coverage distribution / Coverage - Media type / Media set - Media type |

### PlatformListControl.xaml, LayoutSelectorControl.xaml, SearchStringsControl.xaml, ToolbarControl.xaml, ToolsControl.xaml, WidgetBaseControl.xaml, WidgetSelectorControl.xaml, WidgetStatCardControl.xaml, LayoutItemControl.xaml, DropTargetOverlayControl.xaml, ExclusiveOptionsControl.xaml
**Sin literales de UI** (todo por binding, iconos o setters de estilo; los textos que muestran vienen de ViewModels — ver §4).

### RegionsSettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 14 | Heading | Select up to 3 favourite regions (used by the Regions dashboard). |

### SettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 38, 74, 86 | Heading (MAYÚS) | THUMBNAILS / SOUND / WIDGETS |
| 45, 53, 61, 69 | Heading | Game gallery / Media audit / Import media / Orphan media |
| 81 | Heading | Video volume |
| 93, 101, 109 | Heading | Corner radius / Gap / Panel margin |

### SharedMediaToolControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 26-31 | Header/AppBarButton | View / Table view / Grid view / Selected type |
| 49-52 | Header | Game / Type / File / Region |
| 60 | Estado | No shared media found for this platform. |
| 68 | Formato | Showing {0} of {1} |
| 80-83 | Header/Desc | Shared / Size / Types / Games (con subdescripciones) |

### StatsGlobalControl.xaml / StatsPlatformControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| Global 23, 29, 35, 41 | Header | Game set - Platform (collection vs LaunchBox) / Game set - Platform / Image set - Platform / Image set size - Platform (+ descripciones) |
| Platform 25-31 | Header/AppBarButton/ToolTip | Scope / Favourites / In platform / Coverage · "Show/hide the coverage panel" |
| Platform 45, 53 | Heading | Coverage: / Average (all platforms): |
| Platform 77, 81 | Heading/TeachingTip | Coverage - Game |
| Platform 96, 108, 120, 142 | Header | Coverage distribution - Games / Coverage - Media type / Media set - Media type / Coverage (+ descripciones) |

### TemplateSlotsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 45 | ToolTip | Built-in template (read-only) |

### ThemeSettingsControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 17, 41, 86 | Heading | Theme / Background overlay / Loading window background |
| 23 | Toggle | Pick a random theme on each startup |
| 44-76 | Heading | Tint opacity / Tint saturation / Tint brightness / Overlay blur / Overlay opacity |
| 87, 89 | Toggle | Tint the background image / Show the neon frame border |

### WebViewControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 27 | ToolTip | Using Google. Click to change to SteamGridDB. |
| 28 | ToolTip | Using SteamGridDB. Click to use Google |
| 29 | ToolTip | Video media type: searching on YouTube |
| 31, 36, 41 | ToolTip | Help / Back / Forward |
| 34 | TeachingTip | How to download media (+ descripción larga) |
| 47 | Texto por defecto | https:// |

### WidgetPanelControl.xaml
| Línea | Tipo | Texto |
|---|---|---|
| 75, 78, 82 | ToolTip | Confirm / Cancel / Restore defaults |

---

## 2. Inventario — Diálogos y ventanas (XAML)

### Controls/Dialogs
| Fichero:Línea | Tipo | Texto |
|---|---|---|
| DeleteConfirmDialog:14 | Toggle | Ask for confirmation before deleting |
| ImportImagesDialog:17 | Heading | Existing game images: |
| ImportImagesDialog:21 | Descripción | Select whether the images will be added to the collection… You can undo this action from the activity log. |
| ImportImagesDialog:26-27 | Toggle | Discard / Keep |
| PlatformImageDropDialog:13 | Heading | Platform image type: |
| PlatformImageDropDialog:15 | Placeholder | Select a type |
| PlatformImageDropDialog:20 | Heading | Existing images of this type: |
| PlatformImageDropDialog:23-24 | Toggle | Discard / Keep |
| SelectRegionDialog:12 | Heading | Import the matched images into which region? |
| DashboardSettingsDialog:20 | ToolTip | Apply this criterion |
| DashboardSettingsDialog:36, 41 | Heading/TeachingTip | Pre-selection (+ descripción) |
| DashboardSettingsDialog:53, 58 | Heading/TeachingTip | Processing (+ descripción) |
| Dialogs/SettingsControl:49 | Heading | The options for this category will appear here. |
| TemplateNameDialog:12 | Heading | Choose a slot (an occupied slot will be overwritten): |
| TemplateNameDialog:15 | Heading | Name for the template: |
| TemplateNameDialog:16 | Placeholder | e.g. My cyber layout |

### Views (ventanas)
| Fichero:Línea | Tipo | Texto |
|---|---|---|
| SetLaunchBoxFoldersWindow:12 | Heading | **Set the LauchBox folder** (typo) |
| SetLaunchBoxFoldersWindow:18, 33, 48 | Heading | LaunchBox folder / LaunchBox data folder / LaunchBox platforms folder |
| SetLaunchBoxFoldersWindow:63, 78 | Heading | LaunchBox platforms.xml path (×2 — la 2ª debería ser "settings.xml", ver §4) |
| SetLaunchBoxFoldersWindow:89-91 | Button | Close / Select folder / **Ok** |
| MainWindow:14 | Título ventana | MM4LB |
| MainWindow:97 | Heading | Platform: |
| MainWindow:117-150 | Título widget | DASHBOARD / REGIONS / GAME STATISTICS / GLOBAL STATISTICS / ACTIVITY LOG / WEB SEARCH / GAME MEDIA GALLERY / GAMES AUDIT / GAME DETAILS / MEDIA AUDIT / IMPORT COLLECTION / TOOLS |
| MainWindow:211 | Heading | Cache usage: |

---

## 3. Inventario — Code-behind (`*.xaml.cs`)

Casi todo el texto vive en XAML; en code-behind solo hay unos pocos literales:

| Fichero:Línea | Tipo | Texto |
|---|---|---|
| ChartTypeSelectorControl.xaml.cs:390-395 | Button (cara del split) | Bars / H-Bars / Line / Area / Pie / Ring |
| ChartTypeSelectorControl.xaml.cs:441 | Button | All / Top {N} |
| ChartTypeSelectorControl.xaml.cs:465-467 | Button | Asc ↑ / Desc ↓ / No sort |
| WebViewControl.xaml.cs:338, 371 | Menú contextual | Add to game images / Add to game videos |
| WebViewControl.xaml.cs:170 | Error UI | The web browser (WebView2) could not be initialized. |
| GameImageControl.xaml.cs:211 | Label | None (placeholder de región sin asignar) |
| SelectRegionDialog.xaml.cs:24 | Combo item | No region (región vacía) |

---

## 4. Inventario — ViewModels y Services (texto generado en código)

### Diálogos (`Services/DialogsService.cs`)
| Línea | Título | Botones |
|---|---|---|
| 44 | Delete media | Delete / Cancel |
| 85 | Import matched images | Import / Cancel |
| 98 | Import region | Import / Cancel |
| 111 | Add platform image | Add / Cancel |
| 131 | Dashboard settings | Save / Cancel |
| 149 | Settings | OK / Cancel / Apply |
| 163 | Save template | Save / Cancel |

Otros diálogos desde ViewModels: `ConsoleViewModel:133` ("Empty backup folder" · Empty/Cancel),
`AuditPanelViewModel:265` ("Media audit" · "Select a platform first." · OK),
`PlatformDetailsViewModel:512` ("Drop" · "Only one file can be dropped at a time." · OK).

### Mensajes de progreso (Services / ViewModels)
Principales (ver detalle por línea en el barrido): carga de plataformas y BBDD
(`PlatformLoadingService:97/273/277/282/401/417` — incluye "Preparing UI...", "Loading Launchbox games database..."),
import/descarga/proceso de media (`ImageLoadingService`: "Importing media file...", "Media file imported",
"Downloading media file...", "Processing game (x/y)", "Processing regions (x/y)", "Media file deleted: …", etc.),
binarios (`ImageBinaryLoadingService:209` "Loading high-resolution binaries"), dimensiones/orphans
(`ImageAuditViewModel`, `OrphanToolViewModel`), y ffmpeg (`MainWindowViewModel:331/349/354`).

### Mensajes de error mostrados en UI
Numerosos "Error <acción>." en los ViewModels (refresh, add dropped media, delete orphan, import…) y en Services
("No platforms found in LaunchBox…", "LaunchBox metadata database not found.", "Could not replace the video", etc.).

### Pills / títulos de estadística (`Services/StatisticsService.cs`)
In my collection / In LaunchBox / Not in LaunchBox / With a region / No region / Matching / Matched images /
Games with a match / Images / Image types / Size / Games / Media set / Media types / Media set size /
"Empty collection" (varios) / etiquetas de tramo de duración "{a}-{b}s".

### Etiquetas varias
- Criterios de dashboard (`GameImagesDashboardViewModel` y `GameImagesRegionDashboardViewModel`): "1st:", "2nd:",
  "Region:", "Suffix:", "File Name:" (**duplicados** en ambos VM).
- Buckets de región (`GameImagesRegionDashboardViewModel:434-435`): "Other regions" / "No region".
- Tools (`ToolsViewModel:18`): "LaunchBox media check" / "Orphan media files" / "Shared media files".
- Search strings (`SearchStringsViewModel`): "Search strings:", "No search strings.", "Game search strings",
  "Game image search strings".
- Settings (`SettingsDialogViewModel`): opciones "Separate buttons" / "Grouped (split button)" / "Auto (by toolbar
  size)"; secciones "General / Regions / Media types / Theme / About"; lista de licencias del About.
- Series de gráfica: "Coverage", "Selected", "Most used", "Others".

---

## 5. Hallazgos consolidados (accionables)

### 5.1 Mezcla de idiomas EN/ES en texto visible
La app es **mayoritariamente inglés**, pero hay bloques en español mostrados al usuario:

| Ubicación | Texto (ES) |
|---|---|
| AboutSettingsControl.xaml:38-39 | "Organiza, empareja y audita la media…" |
| AboutSettingsControl.xaml:78 | "El color de acento sigue al tema activo." |
| AboutSettingsControl.xaml:79-80 | Copyright "© 2026 MM4LB. No afiliado a LaunchBox…" |
| GameDetailsControl.xaml:121 | "Selecciona un juego para ver su ficha" |
| AuditPanelViewModel.cs:43-45 | "Faltan" / "Sobran" / "OK" (estado de fila) |
| MediaAuditService.cs:213 | "El tipo '…' (columna '…') no existe en MediaType; se ignora." |
| YoutubeDownloadService.cs:92,160,287,331,338,352,379 | **7 errores** de descarga/ffmpeg en español |

**Recomendación:** decidir un idioma único de UI (parece ser EN) y traducir estos textos, o externalizarlos.

### 5.2 Typos
| Ubicación | Actual | Correcto |
|---|---|---|
| ImageTypeControl.xaml:41 | Wihout images | Without images |
| SetLaunchBoxFoldersWindow.xaml:12 | Set the **Lauchbox** folder | LaunchBox |
| StatisticsService.cs:823 | Emtpy collection! | Empty collection |

### 5.3 Etiqueta copiada sin actualizar
- **SetLaunchBoxFoldersWindow.xaml:78** — dice "LaunchBox platforms.xml path" pero la fila corresponde a
  `LaunchboxSettingsXmlFile` (validación `IsLaunchBoxSettingsXmlFileValid`); debería ser algo como
  **"LaunchBox settings.xml path"**. La misma etiqueta aparece en :63 y :78.

### 5.4 Inconsistencias de terminología
- **Concepto "sin coincidencias / sin media"** expresado de 3 formas: `Missing` (GameList) · `No matches`
  (GamesAudit) · `Orphan` (ImageAudit).
- **Concepto ">1"**: `> 1 media file` (GameList) · `>1 match` (GamesAudit) · `Shared` (ImageAudit).
- **Concepto "1"**: `1 media file` (GameList) · `One match` (GamesAudit) · `In use` (ImageAudit).
- **"image" vs "media"**: en Settings/Console se estandarizó a "media" ("Confirm before deleting media",
  "Cached media files"), pero varias cabeceras/tooltips siguen con "images" (ImageAudit:31, GamesAudit:59
  "Matched Images", ImageLoadingService "{N} images imported"/"Image import failed").
- **Pertenencia a BBDD**: abreviado `In LB Db` / `Not in LB Db` (GamesAudit) vs completo `In LaunchBox database` /
  `Not in LaunchBox database` (GameDetails).
- **"None" vs "No region"** para región vacía: `GameImageControl.xaml.cs:211` usa "None";
  `SelectRegionDialog.xaml.cs:24`, `StatisticsService:169`, `GameImagesRegionDashboardViewModel:435` usan "No region".
- **Casing "LaunchBox"**: `PlatformLoadingService:401,417` escriben "Launchbox"; el resto "LaunchBox".
  También en XAML: `Launchbox ID` (GamesAudit:49,58).

### 5.5 Duplicados (candidatos a recurso compartido)
- **"Showing {0} of {1}"** — en 6 ficheros (AuditPanel, GameList, GamesAudit, ImageAudit, OrphanTool, SharedMedia).
- **"Importing image..."** — GameImagesDashboardControl:190 y GameImagesRegionDashboardControl:245.
- **Toolbar de vista** (View / Table view / Grid view / Selected type) — OrphanTool y SharedMedia idénticas.
- **Criterios de dashboard** (1st:/2nd:/Region:/Suffix:/File Name:) — duplicados en los dos VM de dashboard.
- **"No region"** — definido suelto en ≥4 sitios.
- **Títulos de gráfica** "Coverage - Media type" y "Media set - Media type" — en PlatformDetails y StatsPlatform.
- **"Help"** (ToolTip) — repetido en muchos controles con TeachingTip.

### 5.6 Inconsistencias de estilo
- **Puntuación de tooltips gemelos** (WebView:27/28): "…SteamGridDB**.**" con punto vs "…Google" sin punto; y verbo
  "Click to **change to**…" vs "Click to **use**…".
- **Elipsis**: "Importing..." / "Preparing UI..." (tres puntos ASCII) vs "0–20% … 80–100%" (carácter "…").
- **Guion**: "Loading high‑resolution binaries" usa guion no separable (U+2011) en vez de "-".
- **Botón "Ok"** (SetLaunchBoxFoldersWindow:91) vs "OK" (DialogsService Settings). Unificar a "OK".
- **Casing de secciones**: SettingsControl usa MAYÚS ("THUMBNAILS/SOUND/WIDGETS") mientras ThemeSettings usa
  Sentence case; títulos de widget de MainWindow todo en MAYÚS.
- **Formato de barra**: "Orphan/Total media files" (sin espacios) vs "Platform / All (average)" (con espacios).

---

## 6. Recomendación general

La mayor parte del texto de UI está **hardcodeado en XAML y en código**. Para el item de mejoras "los textos a un
fichero de recursos para que sea más fácil su edición", este inventario sirve de base: conviene **externalizar a un
`.resw`/recurso** empezando por los duplicados (§5.5) y unificando la terminología (§5.4) y el idioma (§5.1) en el
mismo movimiento. Los typos (§5.2) y la etiqueta mal copiada (§5.3) son correcciones inmediatas de bajo riesgo.
