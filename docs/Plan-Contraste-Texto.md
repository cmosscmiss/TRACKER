# Plan: color de texto por contraste sobre fondos de color

Estado: **fases 1 a 4 hechas** el 2026-09-06 (núcleo de contraste, toggle, adopción en el XAML y los foregrounds que
se resuelven por código). **Queda la fase 5: verificación en runtime con los cuatro temas.** Documento escrito el
2026-08-30 y simplificado el 2026-09-06 (versión con toggle global).

## 1. El problema

El color del texto es SIEMPRE `TextBrush` (el `TextColor` del tema, blanco en los cuatro temas actuales). Cuando ese
texto cae sobre una superficie de color claro —el acento verde de *LoL*, el `WarningColor` amarillo, un
`AccentLightColor` claro— el contraste es insuficiente y el texto se lee mal.

Objetivo: que el color del texto se elija en función del fondo sobre el que se pinta, usando el claro o un oscuro
según cuál contraste mejor.

## 2. Decisiones tomadas

- **Un toggle** (`UseContrastText`, en el pie del editor de colores) manda sobre todo el mecanismo: si está a `true` el
  texto sobre fondos de color se calcula por contraste; si está a `false` se usa el `TextColor` del tema, como antes.
- **Dos niveles, con precedencia** (`ThemeService.UseContrastText` los resuelve):
  1. Con el TEMA PURO decide el propio tema: `ThemeDefinition.UseContrastText`, `[JsonIgnore]` como el resto de la
     definición (los temas viven en el código, no en el .ini). Está a `false` en todos menos en **LoL**, cuyo acento
     verde claro se lee mal con el blanco encima.
  2. Con los COLORES PERSONALIZADOS activos manda el ajuste general (`ThemeSettings.UseContrastText`), el del editor
     de colores: si el usuario ha cambiado los colores, el tema ya no sabe si su texto contrasta. Encaja con la UI,
     porque el editor solo se abre con los colores personalizados activos.
  - Consecuencia: activar o desactivar *Usar colores personalizados* cambia quién decide, así que `Apply` llama a
    `RefreshThemeResources()` aunque no haya ningún override que aplicar o revertir.
- **Automático**: con el toggle activo el color se calcula por contraste, no se fija a mano tema por tema. **No hay
  override manual por tema** (era la fase 2 del plan original: descartada).
- El candidato oscuro es una **constante** (`#101010`), no una propiedad nueva de `ThemeDefinition`.
- **Auditoría completa**: se revisan todos los usos de brushes de color como fondo, no solo los botones. El toggle es
  la red de seguridad: adoptar `TextOn<Name>Brush` en un sitio no puede empeorar nada mientras el toggle esté
  apagado, porque entonces ese recurso vale lo mismo que `TextBrush`.

### Por qué el toggle simplifica el plan

| Plan original | Con toggle |
|---|---|
| Fase 2: `TextDarkColor` en `ThemeDefinition` + `TextOnAccentColor` opcional por tema + entrada en el editor de colores y en la persistencia de overrides | Eliminada: una constante y un `bool` |
| Fase 3 arriesgada: cada sustitución podía empeorar un sitio y solo se veía probando | Reversible en un clic; con el toggle OFF el XAML nuevo se comporta igual que el viejo |
| Fase 4: cada punto de código decidiendo por su cuenta | El mismo método público, que además consulta el toggle (un solo sitio con la política) |
| Fase 5: comparación a ojo entre temas | Comparación A/B del mismo tema con el toggle on/off |

## 3. Cómo funciona hoy el sistema de temas (lo que hay que saber)

Todo vive en `Services/ThemeService.cs` (~620 líneas):

- `AddThemeColorResources(dict, resourceName, color)` es la pieza central: por cada nombre base publica
  `<Name>Color`, `<Name>Brush` y las variantes `<Name>ColorOpacity{20,40,60,80}` / `<Name>BrushOpacity{...}`.
  Hoy es `private static`.
- `AddAllThemeColors(dict)` la llama para cada nombre base: `Accent`, `AccentLight`, `AccentDark`, `Background`,
  `BackgroundLight`, `CardBackground`, `CardBackgroundLight`, `Text`, `TextSecondary`, `Danger`, `Success`,
  `Warning`, `ExtraColor1..4`.
- `ApplyThemeResources()` mantiene UN diccionario persistente en `Application.Resources.MergedDictionaries` y lo
  repuebla; **no lo reemplaza**.
- `UpsertBrush()` es la clave del cambio en caliente: si el brush ya existe, MUTA su `Color` en vez de crear otro, de
  forma que todo lo que ya lo referencia se actualiza en vivo. Los recursos de tipo `Color` (no `Brush`) sí se
  reemplazan y NO se propagan a elementos ya cargados.
- `ApplyColorInternal(baseName, color)` es el camino del editor de colores (override manual): llama a
  `AddThemeColorResources` solo para ese nombre base, refresca y notifica.
- `RegisterExternalResources()` / `SyncExternalDictionaries()`: los diálogos (`AppDialog`) tienen su propia copia de
  `Theme.xaml` porque el contenido de un `Popup` no alcanza los diccionarios mergeados de la app; el servicio los
  sincroniza con `ApplyColorsToDictionary()`.

**Consecuencia importante para el plan**: si los nuevos recursos se generan DENTRO de `AddThemeColorResources`, se
recalculan solos al cambiar de tema, al aplicar un override manual del editor de colores y en los diccionarios de los
diálogos. No hace falta fontanería nueva; y como todos pasan por `UpsertBrush`, alternar el toggle repinta en vivo.

## 4. Fases

### Fase 1 — Cálculo de contraste y recursos nuevos (núcleo) — HECHA

1. Nuevo `Helpers/ContrastHelper.cs`:
   - `double RelativeLuminance(Color c)` — fórmula WCAG: canal / 255, linealización
     (`c <= 0.03928 ? c/12.92 : pow((c+0.055)/1.055, 2.4)`), y `0.2126 R + 0.7152 G + 0.0722 B`.
   - `double ContrastRatio(Color a, Color b)` — `(L1 + 0.05) / (L2 + 0.05)` con `L1` el más claro.
   - `Color BestForeground(Color background, Color light, Color dark)` — devuelve el de mayor ratio.
   - `DarkText` = `#101010`, el candidato oscuro. Aquí y en ningún otro sitio.
2. En `AddThemeColorResources`, además de lo actual, publicar `TextOn<Name>Color` y `TextOn<Name>Brush` (mismo
   `UpsertBrush`, para que la actualización en caliente funcione igual):
   - toggle ON  → `BestForeground(color, TextColor, ContrastHelper.DarkText)`
   - toggle OFF → `TextColor` del tema (es decir, idéntico a `TextBrush`: cero cambio visual)
   - La función es `static` y necesita el `TextColor` del tema y el flag: convertirla en método de instancia o pasarle
     ambos. Ojo: se la llama desde `AddAllThemeColors` y desde `ApplyColorInternal`.
3. Las variantes de opacidad NO llevan `TextOn*`: el fondo efectivo ahí es la mezcla con lo que haya debajo, no el
   color puro (ver gotchas).
4. Método público en `ThemeService`, punto único de decisión para el código:

   ```csharp
   public Color TextColorOn(Color background)   // respeta el toggle; con OFF devuelve TextColor
   ```

### Fase 2 — El toggle — HECHA

- `ThemeSettings.UseContrastText` (`bool`, por defecto `true`) en `Models/AppSettings.cs`, junto a `RandomTheme`. (Es
  un ajuste del tema; `UseCustomColors`, pese a editarse en la misma pestaña, vive en `GeneralSettings` por historia.)
- `CheckBox` en el PIE del editor de colores (`Controls/Views/ThemeColorEditorControl.xaml`), no en la pestaña Theme
  de los ajustes. Decisión de Víctor, a sabiendas de que rompe la convención de la app (los ajustes viven en la
  ventana de configuración): el editor de colores se abre con `dimOverlay: false`, así que al marcarlo se ve el efecto
  sobre la app entera al instante, sin un overlay que tape. Su clave de localización es, en consecuencia,
  `ThemeColors_UseContrastText_Label`.
- Se edita EN CALIENTE, con el mismo modelo que los colores de ese diálogo: el check escribe
  `ThemeSettings.UseContrastText` y llama a `ThemeService.RefreshThemeResources()`, que repuebla los diccionarios y
  dispara `ThemeChanged`. Como todo va por `UpsertBrush`, el cambio se ve al momento.
- Confirmación: el OK del diálogo persiste (`PersistThemeOverrides` → `PersistData`) y el Cancelar restaura el valor
  que tenía al abrir, junto a la instantánea de overrides (`DialogsService.ShowThemeColorsAsync`).
- Caso de borde cubierto: si el editor de colores cambia el propio `TextColor`, `ApplyColorInternal` regenera TODOS
  los nombres base, no solo `Text`, porque el `TextOn*` de cada uno se calcula contra él.
- Nueva clave en `Helpers/LocKeys.cs` (`ThemeSettings_UseContrastText_Label`) y su texto en `Strings/Resources.resx`
  y `Strings/Resources.es.resx`.

### Fase 3 — Adopción en el XAML (auditoría completa) — HECHA

Sustituir `Foreground="{ThemeResource TextBrush}"` por el `TextOn<Name>Brush` correspondiente **solo donde el fondo
sea ese color al 100%**.

Inventario de partida (usos de `Accent*`/`Danger`/`Success`/`Warning`/`ExtraColor*` **sólidos**, sin opacidad;
117 usos totales de brushes de acento contando las variantes con opacidad):

| Fichero | Usos sólidos |
|---|---|
| `Controls/Views/WidgetStatCardControl.xaml` | 20 |
| `Resources/GenericControls.xaml` | 15 |
| `Controls/Views/PriceChartControl.xaml` | 15 |
| `Views/MainWindow.xaml` | 13 |
| `Controls/Views/ToolbarControl.xaml` | 7 |
| `Resources/Buttons.xaml` | 4 |
| `Controls/Views/WidgetPanelControl.xaml` | 4 |
| `Controls/Views/SplitFlapClock.xaml` | 4 |
| `Controls/Views/FooterEventViewerControl.xaml` | 4 |
| `Controls/Views/ConsoleControl.xaml` | 4 |
| `Controls/Views/AboutSettingsControl.xaml` | 4 |
| `Controls/Views/WidgetBaseControl.xaml` | 3 |
| `Controls/Views/LayoutItemControl.xaml` | 3 |
| `Controls/Views/WebViewControl.xaml` | 2 |
| `Controls/Views/ChartTypeSelectorControl.xaml` | 2 |
| `Resources/Typography.xaml`, `WidgetSelectorControl.xaml`, `ThemeColorEditorControl.xaml`, `SettingsControl.xaml` | 1 c/u |

Comando para regenerar el inventario:

```sh
grep -rn "ThemeResource \(Accent\|AccentLight\|AccentDark\|Danger\|Success\|Warning\|ExtraColor[1-4]\)Brush}" \
  --include=*.xaml Resources Controls Views | cut -d: -f1 | sort | uniq -c | sort -rn
```

**La mayoría de esos usos NO llevan texto encima** (barras de acento, elipses de estado, líneas, `Fill` de iconos):
esos se descartan en la revisión. Candidatos confirmados con texto sobre color sólido:

#### Resultado de la auditoría (2026-09-06)

Filtrando el inventario por lo que de verdad es un FONDO de color al 100% (`Background=` / `Fill=` / `Setter` de
`Background`, descartando las variantes con opacidad), quedan muy pocos sitios, y de ellos solo dos llevan texto
encima. Todo lo demás son elipses de estado, barras de acento, líneas de separación y `Foreground` de color sobre el
fondo normal de la app: no aplican.

Comando que deja el inventario ya filtrado a fondos sólidos:

```sh
grep -rn "\(Background\|Fill\)=\"{ThemeResource [^}]*\(Accent\|Danger\|Success\|Warning\|ExtraColor\)[^}]*}\"" \
  --include=*.xaml Resources Controls Views | grep -v Opacity | grep -v BorderBrush
```

- **HECHO** — `Resources/Buttons.xaml`, `Button_Theme`: `Foreground` pasa a `TextOnAccentBrush`, y los estados
  `PointerOver`/`Pressed` (que cambian el fondo a `AccentLightBrush`) fijan `TextOnAccentLightBrush` sobre el
  `ContentPresenter`, que para eso ahora tiene `x:Name="ContentHost"`. Es el botón primario de todos los `AppDialog`.
  - Efecto colateral necesario: `AppDialog.BuildIconContent` enlazaba el `FontIcon` al `Foreground` del BOTÓN, que no
    cambia por estado; ahora lo enlaza al del `TextBlock` que tiene al lado, que sí hereda del `ContentPresenter`. Sin
    esto, en hover el icono se quedaba con el color del estado Normal y el texto no.
- **HECHO** — `Resources/GenericControls.xaml:162` (`ListViewItemDefaultStyle`, usado por la lista de productos): el
  item seleccionado pinta `RootGrid.Background` = `AccentBrush` con el `DataTemplate` encima. Recolorear el
  `ContentPresenter` NO sirve: el contenido fija sus propios foregrounds y no hereda (el título por converter, y los
  seis `FontIcon` con `Foreground` explícito — el `FontIcon` no hereda, su estilo por defecto gana), y además el
  `DataTemplate` no sabe si su item está seleccionado. Solución (decisión de Víctor): que lo sepa.
  - `Product.IsSelected` (observable, no persistida) la mantiene el setter de `SharedDataService.SelectedProduct`, por
    donde pasan TODAS las selecciones (lista, gráfica, alta), no el `SelectionChanged` del `ListView`.
  - `Product` expone el TONO, no un brush: `enum ListTextTone { Normal, Secondary, OnAccent }` con `ListTitleTone`
    (seleccionado → OnAccent; comprado → Secondary; si no → Normal) y `ListIconTone` (seleccionado → OnAccent). Mismo
    patrón que `ListPriceHighlight`: el modelo no conoce brushes.
  - `Helpers/ListTextToneToBrushConverter.cs` (nuevo, `ThemeBrushConverter`) traduce el tono a brush; OnAccent pasa
    por `TextColorOn(AccentColor)`. **Sustituye a `PurchasedToTextBrushConverter`**, que queda borrado: su caso es
    ahora el tono Secondary.
  - Limitación conocida: `PointerOverSelected` pinta el fondo con `AccentLightBrush`, pero el tono sigue siendo el de
    `AccentColor`. Solo afecta al hover sobre la fila ya seleccionada, y los dos acentos suelen tener luminancias
    parecidas; saberlo exigiría que el modelo conociera el puntero.

- **HECHO** — los controles del sistema que pintan su item resaltado o seleccionado con el acento. No se ven en el
  inventario de arriba porque no usan `{ThemeResource AccentBrush}` directamente, sino los brushes CON NOMBRE que
  espera la plantilla de WinUI (`ComboBoxItemForegroundSelected`, `ListViewItemForegroundSelected`…), definidos en
  `Resources/GenericControls.xaml` y `Resources/Buttons.xaml` y refrescados en caliente desde el mapa de
  `ThemeService.RefreshNamedControlBrushes`. Hay que tocar LOS DOS SITIOS: el mapa (lo que manda en caliente) y la
  definición del XAML (el primer render).
  - `ComboBoxItemForeground` en `Selected`, `Pressed`, `PointerOver` y sus combinaciones → `TextColorOn` del acento
    que lleva cada estado de fondo (`Accent` en Selected, `AccentLight` en el resto). Es el desplegable de elegir
    color del editor, y cualquier otro combo de la app.
  - `ListViewItemForeground*` de la lista de categorías del diálogo de ajustes: sus brushes son LOCALES
    (`ListView.Resources` de `Controls/Dialogs/SettingsControl.xaml`), así que se cambian ahí, a
    `TextOnAccentColor` / `TextOnAccentLightColor`.
  - `ButtonBrushForegroundChecked` / `...CheckedPressed` / `...CheckedDisabled`: un botón marcado se pinta con
    `AccentDark` (o `AccentLight` al pulsar). El `CheckedPointerOver` se queda como estaba: su fondo lleva opacidad.

Descartados con motivo:

- `Controls/Views/TemplateSlotsControl.xaml` — la banda del nombre va sobre `AccentDarkBrushOpacity60`, o sea mezcla
  con el fondo oscuro: el gotcha 1 dice no tocarlo.
- `Controls/Views/WidgetStatCardControl.xaml` — de sus 20 usos, ninguno es texto sobre color: son `Foreground` de
  color (glifo y etiqueta sobre el fondo de tarjeta), la `LeftAccentBar` y fondos con opacidad (`IconHost`,
  `SplitPanelLayer`).
- `MainWindow.xaml`, `ToolbarControl.xaml`, `ConsoleControl.xaml`, `FooterEventViewerControl.xaml`,
  `SplitFlapClock.xaml`, `WidgetBaseControl.xaml`, `LayoutItemControl.xaml`, `Typography.xaml` — separadores, elipses
  de estado, bordes e iconos de color. Sin texto encima.

### Fase 4 — Los foregrounds que se resuelven por código — HECHA

Revisados los sitios que fijan brushes desde C# (no ven los `{ThemeResource}`). Solo uno pinta TEXTO sobre un color
sólido; el resto no aplica, y se deja anotado para no volver a mirarlo:

- **HECHO** — el recuadro de precio de la lista de productos: el fondo sale de `PriceHighlightToBrushConverter`
  (verde/rojo/azul/neutro) y su texto era `Foreground="White"` FIJO. Nuevo
  `Helpers/PriceHighlightToTextBrushConverter.cs` (`ThemeBrushConverter`), pareja del anterior, que devuelve
  `TextColorOn(<color de ese highlight>)`. Con el toggle apagado da el `TextColor` del tema, blanco en los cuatro
  temas: mismo aspecto que el "White" de antes.
- No aplica — `LogEntrySeverityToBrushConverter`: da el color del TEXTO del log (danger/warning/secundario/normal)
  sobre el fondo normal de la app. No hay fondo de color debajo.
- No aplica — `PriceChartViewModel.ResolveBrush` / `PrimeBrush` / `PrimeBackgroundBrush`: el fondo del pill Prime es
  `…BrushOpacity60`, o sea mezcla (gotcha 1). Lo que sí se arregló de paso es una deuda ajena al contraste: las trece
  pastillas de `PriceChartControl.xaml` (Prime, comprado, reserva, alerta, avisos, promo) tenían el texto y el icono
  en `"White"` FIJO; ahora usan `TextBrush`, así que siguen al tema y a los colores personalizados. En los cuatro
  temas actuales `TextColor` es blanco, o sea que hoy se ve igual.
- No aplica — `WidgetPanelControl.xaml.cs:259-260` (brushes de los hitbox de slot) y `:1076` (el `glow` del handle de
  fila): son `Border` sin texto encima.
- `Helpers/ThemeBrushConverter.cs` no necesitaba cambios: su caché por rama ya refresca el color al recibir
  `ThemeChanged`, que es justo lo que dispara el toggle vía `RefreshThemeResources()`.

Nota: `WidgetStatCardControl` NO entra aquí: su `ColorVariant` se aplica por `VisualState` en el XAML —
`UpdateColorVariantState` solo dispara el estado—, así que es fase 3 (y ahí quedó descartado: sus fondos llevan
opacidad).

### Fase 5 — Verificación

- Con el toggle ON y con el toggle OFF, recorrer los cuatro temas (Cyber City, Dead Space, Doom, LoL) mirando cada
  superficie de color con texto. Con OFF el resultado tiene que ser IDÉNTICO al de hoy: es la prueba de que la
  adopción del XAML no ha cambiado nada por su cuenta.
- Red de seguridad opcional: volcado en DEBUG al log con el ratio de contraste de cada par fondo/texto, para cazar los
  que queden por debajo de 4.5:1 (umbral WCAG AA para texto normal; 3:1 para texto grande).

## 5. Gotchas (leer antes de tocar nada)

1. **Opacidad ≠ color sólido**: hay muchos usos de `AccentBrushOpacity20/40/60` sobre el fondo oscuro de la app. El
   fondo efectivo es la MEZCLA, no el acento: aplicarles `TextOnAccent` los empeoraría. Regla: `TextOn<Name>` solo
   sobre el color al 100%. Si algún caso con opacidad lo necesita, hay que calcular el color compuesto contra el
   fondo real.
2. **`Popup` de los diálogos**: el contenido de un `Popup` no alcanza los diccionarios mergeados de la app; por eso
   `AppDialog` registra su copia con `RegisterExternalResources`. Si los `TextOn*` se generan dentro de
   `AddThemeColorResources`, llegan solos a esas copias vía `SyncExternalDictionaries`.
3. **Brushes vs Colors**: solo los `Brush` se actualizan en caliente (mutación in situ). Un control que lea un recurso
   de tipo `Color` necesita reconstruirse al recibir `ThemeChanged`.
4. **Editor de colores**: al cambiar el acento a mano, `ApplyColorInternal` regenera solo ese nombre base; el
   `TextOn*` correspondiente se recalcula con él si va dentro de la misma función.
5. El estado `Disabled` de los botones ya tuvo un problema parecido (heredaba el fondo de acento del `Setter` del
   estilo). Ver el comentario en `Resources/Buttons.xaml`, estilo `Button_Default`.
6. **El toggle no es un interruptor de "modo oscuro"**: apagado NO significa "texto oscuro", significa "el de
   siempre". Nada del XAML debe asumir que `TextOn<Name>Brush` es distinto de `TextBrush`.

## 6. Orden sugerido y coste

- **Fases 1 + 2**: medio día. No cambian nada visualmente (todavía nadie usa los recursos nuevos), así que se pueden
  commitear juntas y sin riesgo.
- **Fase 3**: es donde se va el tiempo (revisión sitio por sitio, no automatizable con un `sed`) y donde se ve el
  cambio. Se puede trocear por fichero, porque cada trozo es reversible con el toggle.
- **Fase 4**: pequeña, depende de la 1.
- **Fase 5**: prueba manual con los cuatro temas, comparando A/B con el toggle.

## 7. Alternativas descartadas

- **`ContrastForegroundConverter`** aplicado en cada binding de `Foreground`: más verboso en el XAML, no encaja con
  `{ThemeResource}` (que es lo que da la actualización en caliente) y se complica dentro de los `Popup` de los
  diálogos. Los recursos precalculados por el `ThemeService` son mejor encaje con lo que ya existe.
- **Color de texto sobre acento configurable por tema** (`TextOnAccentColor` en `ThemeDefinition`, con entrada en el
  editor de colores y en la persistencia de overrides): mucha fontanería para un caso que el cálculo automático ya
  resuelve. El toggle global cubre la necesidad real, que es poder desactivarlo si no convence el resultado.
