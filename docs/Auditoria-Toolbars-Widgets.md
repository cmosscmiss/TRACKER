# Auditoría — Barras de herramientas por widget

Listado de las toolbars de cada widget: número de botones y, para los botones
**agrupados** (excluyentes: un click activa uno y desactiva el resto del grupo, como
la calidad de vídeo), el número de botones por grupo y qué hacen. El resto son
toggles **independientes** (multi-selección) o botones de acción **sueltos**.

Fecha: 2026-07-11.

## Widgets del panel

### DASHBOARD — GameImagesDashboard · 10 botones
- **Grupo Layout (2, excluyente):** Hor. view / Ver. view — vista horizontal vs vertical.
- **Grupo Calidad de vídeo (5, excluyente; solo visible en tipos de vídeo):** 240p /
  360p / 480p / 720p / 1080p — resolución de descarga de vídeo de YouTube.
- Strings (toggle suelto) — muestra/oculta el panel de search-strings.
- Delete (suelto) — borra el medio seleccionado.
- Settings (suelto) — abre el diálogo de criterios de preselección/proceso.

### REGIONS — GameImagesRegionDashboard · 11 botones
- **Grupo Layout (2, excluyente):** Hor. / Ver.
- **Grupo Calidad de vídeo (5, excluyente; solo vídeo):** 240 / 360 / 480 / 720 / 1080.
- Strings (toggle suelto) · Delete (suelto) · Process region (suelto — procesa la
  región activa sin cambiar de juego) · Settings (suelto).
- Nota: el selector de regiones NO es toolbar; es un `ItemsRepeater` aparte.

### GAME STATISTICS — StatsPlatform · 3 botones (toolbar principal)
- **Grupo Ámbito (2, excluyente):** Favourites / In platform — tipos del eje X.
- Coverage (toggle suelto) — muestra/oculta el panel de resumen de cobertura.
- Además, cada gráfica del FlipView lleva su propia toolbar `ChartTypeSelector` (ver abajo).

### GLOBAL STATISTICS — StatsGlobal · sin toolbar de AppBar
- 4 gráficas, cada una con su toolbar `ChartTypeSelector` (ver abajo).

### Toolbar de gráfica — ChartTypeSelectorControl (en StatsPlatform y StatsGlobal)
Tres `ToggleSplitButton`, cada uno **un grupo excluyente** (menú de radio-items):
- **Chart type (6):** Bars / H-Bars / Line / Area / Pie / Ring.
- **Top N (6):** Top 5 / 10 / 20 / 50 / 100 / All.
- **Sort (3):** No sort / Asc / Desc.

### WEB SEARCH — WebView · 4 botones + barra
- Motor de búsqueda (1 toggle) — alterna **Google ⟷ SteamGridDB** (se muestra un icono
  u otro según el motor activo).
- Indicador YouTube — informativo, NO clicable (aparece en tipos de vídeo).
- Help (ⓘ) · Back · Forward · barra de direcciones (TextBox).

### GAME MEDIA GALLERY — ImageGrid · 9 botones
- **Grupo Aspect ratio (5, excluyente):** 1:1 / 9:16 / 3:4 / 16:9 / 4:3 — relación de
  aspecto de las miniaturas.
- **Grupo Resolución (3, excluyente):** Low / Medium / High — resolución de decodificación.
- Delete (suelto, condicional — solo en galerías que lo permiten).

### GAMES AUDIT — GamesAudit · 6 filtros independientes (multi-selección)
- Presencia: In collection / In LB Db / Not in LB Db.
- Nº de matches: No matches / One match / >1 match.

### MEDIA AUDIT — ImageAudit · 7 botones
- Filtros **independientes (3):** In use / Shared / Orphan.
- Dimensions (suelto) — recupera dimensiones de las imágenes.
- Delete Orphan (suelto) — borra las huérfanas.
- **Grupo Vista (2, excluyente):** List view / Grid view.
- (La página 2 del FlipView es un chart fijo, sin toolbar de tipo.)

### IMPORT COLLECTION — ImageCollectionImport · 4 botones
- Folder (suelto) — elegir carpeta · Import (suelto) — importar.
- **Grupo Vista (2, excluyente):** Media / Games.

### TOOLS — 2 sub-herramientas, cada una con su toolbar
- **Media Audit (AuditPanel) · 3:** Check media (suelto) · Only discrep. (toggle
  indep.) · Selected media type (toggle indep.).
- **Orphan media (OrphanTool) · 4:** **Grupo Vista (2, excluyente):** Table view /
  Grid view · Selected type (toggle indep.) · Delete all (suelto).

## Notas

- **Banda superior de tipos (ImageTypeControl)** no es un widget del panel; es la banda
  fija con los botones de **tipos favoritos** (grupo) + combo de tipo.
- **ACTIVITY LOG (Console)** no tiene toolbar superior; solo un botón **Backup** en su pie
  (limpia la carpeta de backup).

## Resumen de "grupos" (botones excluyentes)

| Widget | Grupo | Nº | Opciones |
|---|---|---|---|
| Dashboard / Regions | Layout | 2 | Hor / Ver |
| Dashboard / Regions | Calidad de vídeo | 5 | 240 / 360 / 480 / 720 / 1080 |
| StatsPlatform | Ámbito | 2 | Favourites / In platform |
| ChartTypeSelector | Chart type | 6 | Bars / H-Bars / Line / Area / Pie / Ring |
| ChartTypeSelector | Top N | 6 | 5 / 10 / 20 / 50 / 100 / All |
| ChartTypeSelector | Sort | 3 | None / Asc / Desc |
| WebView | Motor de búsqueda | 2* | Google ⟷ SteamGridDB (*un solo toggle) |
| ImageGrid | Aspect ratio | 5 | 1:1 / 9:16 / 3:4 / 16:9 / 4:3 |
| ImageGrid | Resolución | 3 | Low / Medium / High |
| ImageAudit | Vista | 2 | List / Grid |
| ImportCollection | Vista | 2 | Media / Games |
| OrphanTool | Vista | 2 | Table / Grid |
