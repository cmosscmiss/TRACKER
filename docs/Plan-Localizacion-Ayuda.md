# Plan — Localización (i18n) + Tooltips/Ayuda con toggle

> Fecha: 2026-07-19
> Objetivo: externalizar TODO el texto de UI a recursos para soportar múltiples idiomas, con **cambio de idioma en
> caliente**; y añadir **label + tooltip por control** más un **toggle en el footer** que activa/desactiva la ayuda
> (tooltips y paneles de ayuda / TeachingTips).
> Base de trabajo: [Auditoría de textos de UI](Auditoria-Textos-UI.md) como checklist de migración.

## Decisiones fijadas
- **Cambio de idioma:** en caliente (sin reiniciar), coherente con el hot-apply del tema.
- **Formato de recursos:** `.resx` (.NET, tooling en VS, comprobación en compilación).
- **Idiomas iniciales:** Inglés (base) + Español.
- **Toggle de ayuda:** gobierna **tooltips + paneles de ayuda** (los iconos "Help"/TeachingTips existentes).
- **No** se usa `x:Uid` (impediría el cambio en caliente); se usa una markup extension propia.

## Arquitectura

### 1. Localización
- **Recursos:** `Strings/Resources.resx` (EN, neutral por defecto) + `Strings/Resources.es.resx` (ES). Genera satellite
  assembly `es/MM4LB.resources.dll` (se copia al output; app sin empaquetar lo soporta).
- **`LocalizationService`** (DI, singleton):
  - `CultureInfo Current`; `SetLanguage(string code)` → fija `Current`, persiste en `AppSettings`, notifica.
  - Indexador `string this[string key] => _rm.GetString(key, Current) ?? key;` con `INotifyPropertyChanged`
    disparando `Item[]` al cambiar de idioma → **todos los bindings de texto se actualizan solos**.
  - `Format(key, args...)` para cadenas con placeholders (`"Showing {0} of {1}"`, `"{N} media files"`).
  - Evento `LanguageChanged` para el texto fijado en código que persiste (ver más abajo).
- **Markup extension `{loc:Str Key=...}`**: `ProvideValue` devuelve un `Binding { Source=LocalizationService,
  Path="[Key]", Mode=OneWay }`. Uso en XAML: `Text="{loc:Str Key=Dashboard.ProcessRegion.Label}"`.
- **API en código:** `Loc.Get(key)` / `Loc.Format(key, args)`.
- **Claves:** convención `{Scope}_{Element}_{Role}` donde **Scope = el control/vista/servicio dueño** (nombre
  reconocible, sin el sufijo `Control`) para poder identificar quién la usa leyéndola; `Common_*` para lo
  compartido/deduplicado. **Sin puntos** (romperían el binding por indexador `[clave]`). Roles: `Label`, `Tooltip`,
  `Header`, `Placeholder`, `Title`, `Description`, `Empty`, `Format`, `Progress`, `Error`. Ej.:
  `GameImagesRegionDashboard_ProcessRegion_Label` / `_Tooltip`, `Common_ShowingXofY_Format`. Se genera una clase de
  constantes `LocKeys` + un **validador dev-time** que comprueba que toda clave exista en el `.resx`. **[Hecho en F0.]**
- **Texto fijado en código que persiste** (no transitorio): contenido de los split-buttons de
  [ChartTypeSelectorControl.xaml.cs](../Controls/Views/ChartTypeSelectorControl.xaml.cs), nombres de sección de
  Settings, títulos de tools, etiquetas de criterio de los dashboards → se **re-aplican** suscribiéndose a
  `LanguageChanged`. El transitorio (mensajes de progreso, títulos de diálogos creados al abrir) se resuelve al vuelo
  con `Loc`, sin re-aplicar.

### 2. Tooltips + ayuda con toggle
- **Setting** `General.HelpTooltipsEnabled` (bool, default `true`) en `AppSettings`.
- **Estado en `SharedDataService`** (NO se crea un `HelpService`: el bool + notificación caben en el hub observable
  que ya existe): propiedad observable `HelpTooltipsEnabled` que espeja el setting (el valor vivo ES el persistido).
- **`FooterHelpControl`**: clon del patrón de [FooterSoundControl.xaml](../Controls/Views/FooterSoundControl.xaml)
  (icono + toggle) enlazado a `SharedDataService.HelpTooltipsEnabled`.
- **Attached property `help:Help.Key`** (clave de recurso del tooltip): al asignarse, calcula
  `tooltip = HelpTooltipsEnabled ? Loc[key] : null` y hace `ToolTipService.SetToolTip(...)`; se suscribe al cambio del
  toggle (`SharedDataService.PropertyChanged`) + `LanguageChanged`. Con limpieza en `Unloaded` para no fugar.
- **Paneles de ayuda (TeachingTips):** el toggle **oculta el botón-icono "Help"** que los dispara, vía la attached
  property `help:Help.AffordanceVisible` (la Visibility sigue al toggle); un único interruptor controla toda la ayuda.
- **Convención label↔tooltip por botón:** `X_Label` + `X_Tooltip`. Para iconográficos (`ToolbarButtonIcon`) se
  añade `HelpKey`; para `AppBarButton`, `Label` + `Help.Key`.
- (Opcional, accesibilidad) fijar `AutomationProperties.Name` desde la clave de label.

### 3. Selector de idioma
- **Combo en la pestaña General** del diálogo de Settings in-app (decisión fijada). ComboBox EN/ES →
  `LocalizationService.SetLanguage`. Persistido en `AppSettings`.

## Plan por fases

### F0 — Infraestructura (sin migrar textos)
- `LocalizationService` + `.resx` (EN/ES vacíos o mínimos) + markup extension `{loc:Str}` + `LocKeys` + validador.
- `HelpService` + attached property `Help.Key`/`Help.IsAffordanceVisible` + `FooterHelpControl` + setting.
- Selector de idioma en Settings.
- **Criterio de hecho:** un texto de prueba cambia EN↔ES en caliente; el toggle del footer enciende/apaga un tooltip.

### F1 — Piloto end-to-end
- Migrar por completo **los dos dashboards** (Game + Region): labels, tooltips, botones, estados.
- Valida todo el pipeline (XAML + código + re-aplicar en `LanguageChanged` + toggle) en un caso real y complejo.

### F2 — Migración por áreas (usando la Auditoría como checklist)
- Orden: resto de `Controls/Views` → `Controls/Dialogs` + ventanas → code-behind → `ViewModels`/`Services`
  (progreso, errores, pills, `DialogsService`).
- Se **corrigen a la vez** los hallazgos de la auditoría §5: typos (`Wihout`, `Lauchbox`, `Emtpy`), etiqueta mal
  copiada (settings.xml), unificación de terminología (Missing/No matches/Orphan; image↔media; None↔No region;
  Launchbox↔LaunchBox) y duplicados (una sola clave `Common.*`).
- La parte mecánica se puede paralelizar con subagentes; revisión + build por área.

### F3 — Tooltips/ayuda que faltan
- Rellenar `Label` + `Tooltip` donde no existan, empezando por todos los botones. Revisar cobertura de ayuda.

### F4 — Español completo + validación
- Traducir todas las claves a `Resources.es.resx`, incluidos los textos que hoy están en ES en código
  (errores ffmpeg/YouTube de `YoutubeDownloadService`, `AuditPanelViewModel` "Faltan/Sobran", `MediaAuditService`).
- Pasada final: recorrer la app en ES buscando texto sin traducir o cortado.

## Riesgos / notas técnicas
- Markup extensions soportadas en WinUI 3; `ProvideValue` devolviendo un `Binding` refresca al notificar `Item[]`.
- Placeholders de `string.Format` se conservan en el recurso; el ES puede reordenar → usar índices (`{0}`,`{1}`).
- Satellite assemblies deben copiarse al output (por defecto con `.resx`).
- Los diálogos (`DialogsService`) y mensajes de progreso se crean al vuelo → toman el idioma actual sin re-aplicar.
- Compilar siempre con `-p:Platform=x64`.

## Estado
- [x] F0 — Infra (localización en caliente validada con `{loc:Str}` + sistema de ayuda con toggle en footer)
- [x] F1 — Piloto: los dos dashboards (XAML: labels, tooltips vía `Help.Key`, estados). Además `ExclusiveOptionsControl`
  hecho localizable (opción `ExclusiveOption.LabelKey` + reconstrucción al cambiar de idioma).
- [x] F2 — Migración por áreas COMPLETA: todo el texto de UI (controles, diálogos, ventanas, títulos de widget) +
  todo el texto de VM/Services (DialogsService, StatisticsService pills, criterios/buckets, Tools, SearchStrings,
  y TODOS los mensajes de progreso/error del ACTIVITY LOG). Helpers reutilizables: `DataGridLoc` (cabeceras de
  DataGrid), `LocFmt` (contador "Showing X of Y"), `ExclusiveOption.LabelKey`. Corregidos de paso los hallazgos de
  la auditoría: typos ("Wihout", "Lauchbox", "Emtpy", "Ok"), etiqueta mal copiada (settings.xml), y toda la mezcla
  EN/ES (About, GameDetails, YouTube/ffmpeg, MediaAudit, AuditPanel "Faltan/Sobran") pasa a base EN + traducción ES.
- [x] F3 — Tooltips/ayuda que faltaban: tooltip localizado en los 11 botones-icono de la toolbar principal;
  gating al toggle de TODOS los iconos de ayuda (ChartTypeSelector por code-behind; WebView/ImportImages/
  DashboardSettings vía `Help.AffordanceVisible`).
- [x] F4 — Cobertura de tooltips + repaso final:
  - Tooltip (más explicativo que el label) en todos los botones con label visible que aún no lo tenían:
    SetLaunchBoxFolders (Cerrar/Seleccionar carpeta/Guardar), AuditPanel (Comprobar media/Solo discrep./Tipo
    seleccionado), ImageCollectionImport (Carpeta/Importar), ImageAudit (Huérfanas), OrphanTool (Borrar todos),
    ImageType (toggle de tipo), WidgetPanel splitters (Confirmar/Cancelar/Restaurar; además localizados y gateados).
  - Barrido de textos hardcodeados: localizado el panel de ajustes rápidos (`Views/SettingsControl.xaml`,
    scope `QuickSettings_`) y el tooltip built-in de TemplateSlots. Verificado que los headers de DataGrid ya se
    localizan por `DataGridLoc` y que no quedan literales en atributos ni en contenido de elementos.
  - Fuera de alcance a propósito: botones de acción de `AppDialog` (label dinámico por diálogo) e ítems de lista
    de selección (buckets de región). Card "Cache usage" de ConsoleControl queda hardcodeada pero está
    `Visibility="Collapsed"` (UI muerta).

## Patrones de migración (para retomar)
- **Texto visible** (FrameworkElement): `Text/Label/Content/Header="{loc:Str Key=...}"` (xmlns `loc:MM4LB.Markup`).
- **Tooltips de botón** (gated por el toggle de ayuda): `help:Help.Key="..."` (xmlns `help:MM4LB.Helpers`).
- **Iconos de ayuda / TeachingTips**: visibilidad con `help:Help.AffordanceVisible="True"`.
- **ExclusiveOptions**: `LabelKey` en las `ExclusiveOption` + `{loc:Str}` en `Header`.
- **Cabeceras de DataGrid** (no aceptan binding): `DataGridLoc.Attach(grid, (Tag, key)...)` en el ctor.
- **Contadores "X de Y"**: `Text="{x:Bind helpers:LocFmt.ShowingXofY(a, b)}"`.
- **Texto en código** (VM/Services): `LocalizationService.Get/Format`; re-aplicar en `LanguageChanged` si persiste.
- Claves: `{Scope}_{Element}_{Role}` (Scope = control dueño; `Common_` para lo compartido); añadir a `LocKeys` + a
  `Resources.resx` (EN) + `Resources.es.resx` (ES). Corregir de paso los typos/terminología de `Auditoria-Textos-UI.md`.
