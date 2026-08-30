# Plan: color de texto por contraste sobre fondos de color

Estado: **pendiente de implementar**. Documento escrito el 2026-08-30 para poder retomar el trabajo sin volver a
investigar el sistema de temas.

## 1. El problema

El color del texto es SIEMPRE `TextBrush` (el `TextColor` del tema, blanco en los cuatro temas actuales). Cuando ese
texto cae sobre una superficie de color claro —el acento verde de *LoL*, el `WarningColor` amarillo, un
`AccentLightColor` claro— el contraste es insuficiente y el texto se lee mal.

Objetivo: que el color del texto se elija en función del fondo sobre el que se pinta, usando el claro o un oscuro
según cuál contraste mejor.

## 2. Decisiones ya tomadas

- **Automático**: el color se calcula por contraste, no se fija a mano tema por tema. (Se deja la puerta abierta a un
  override manual por tema, pero no es el modo principal; ver fase 2.)
- **Auditoría completa** en la fase 3: se revisan TODOS los usos de brushes de color como fondo, no solo los botones.

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
diálogos. No hace falta fontanería nueva.

## 4. Fases

### Fase 1 — Cálculo de contraste y recursos nuevos (núcleo)

1. Nuevo `Helpers/ContrastHelper.cs`:
   - `double RelativeLuminance(Color c)` — fórmula WCAG: canal / 255, linealización
     (`c <= 0.03928 ? c/12.92 : pow((c+0.055)/1.055, 2.4)`), y `0.2126 R + 0.7152 G + 0.0722 B`.
   - `double ContrastRatio(Color a, Color b)` — `(L1 + 0.05) / (L2 + 0.05)` con `L1` el más claro.
   - `Color BestForeground(Color background, Color light, Color dark)` — devuelve el de mayor ratio.
2. En `AddThemeColorResources`, además de lo actual, publicar `TextOn<Name>Color` y `TextOn<Name>Brush` (mismo
   `UpsertBrush`, para que la actualización en caliente funcione igual).
   - La función es `static` y necesitará los dos candidatos (claro y oscuro): convertirla en método de instancia o
     pasarle los dos colores. Ojo: se la llama desde `AddAllThemeColors` y desde `ApplyColorInternal`.
   - Nombre elegido: `TextOn<Name>Brush` (coherente con `TextBrush` / `TextSecondaryBrush` del repo), no `On<Name>`.
3. Las variantes de opacidad NO llevan `TextOn*`: el fondo efectivo ahí es la mezcla con lo que haya debajo, no el
   color puro (ver gotchas).

### Fase 2 — Modelo de color (dónde sale el oscuro)

- Añadir a `AppSettings.ThemeDefinition` un `TextDarkColor` (por defecto `#101010`) como candidato oscuro del cálculo.
  Los cuatro temas lo heredan sin tocarlos.
- Opcional (dejar preparado, no obligatorio): `TextOnAccentColor` por tema que, si viene informado, gana sobre el
  cálculo automático.
- Ambos entran solos en el editor de colores y en la persistencia de overrides (`General.CustomColors`), que trabajan
  por nombre base vía reflexión sobre `<baseName>Color`.

### Fase 3 — Adopción en el XAML (auditoría completa)

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

- `Resources/Buttons.xaml` — `Button_Theme`: `Foreground` = `TextBrush` sobre `AccentBrush` (Normal) y
  `AccentLightBrush` (PointerOver / Pressed). Es el caso más visible.
- `Resources/GenericControls.xaml:162` — estado `Selected` de un item: `RootGrid.Background` = `AccentBrush`
  (y `AccentLightBrush` en `PointerOverSelected`) con el contenido encima.
- `Controls/Views/TemplateSlotsControl.xaml` — banda del nombre del template sobre `AccentDarkBrushOpacity60`
  (caso de opacidad: revisar el fondo real, ver gotchas).
- `Controls/Views/WidgetStatCardControl.xaml` — las pills: repasar cada `ColorVariant`, aunque muchos de sus 20 usos
  son la `LeftAccentBar` (sin texto).

### Fase 4 — Los foregrounds que se resuelven por código

Sitios que fijan brushes desde C# y que no ven los `{ThemeResource}`:

- `Controls/ViewModels/PriceChartViewModel.cs:229` `ResolveBrush(key)`, y su uso en `PrimeBrush` /
  `PrimeBackgroundBrush` (líneas ~478).
- `Helpers/ThemeBrushConverter.cs`, `LogEntrySeverityToBrushConverter.cs`, `PriceHighlightToBrushConverter.cs`,
  `PurchasedToTextBrushConverter.cs`.
- `Controls/Views/WidgetPanelControl.xaml.cs:259-260` y `:1076` (brushes de slot desde `ThemeService`).

Nota: `WidgetStatCardControl` NO entra aquí: su `ColorVariant` se aplica por `VisualState` en el XAML —
`UpdateColorVariantState` solo dispara el estado—, así que es fase 3.

Para estos, exponer en `ThemeService` un método público:

```csharp
public Color TextColorOn(Color background)   // usa ContrastHelper con TextColor y TextDarkColor del tema
```

### Fase 5 — Verificación

- Recorrer los cuatro temas (Cyber City, Dead Space, Doom, LoL) mirando cada superficie de color con texto.
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

## 6. Orden sugerido y coste

- **Fases 1 + 2**: un día corto. No cambian nada visualmente hasta que se usen los recursos nuevos, así que se pueden
  commitear por separado y sin riesgo.
- **Fase 3**: es donde se va el tiempo (revisión sitio por sitio, no automatizable con un `sed`) y donde se ve el
  cambio.
- **Fase 4**: pequeña, depende de la 1.
- **Fase 5**: prueba manual con los cuatro temas.

## 7. Alternativa descartada

Un `ContrastForegroundConverter` aplicado en cada binding de `Foreground`: más verboso en el XAML, no encaja con
`{ThemeResource}` (que es lo que da la actualización en caliente) y se complica dentro de los `Popup` de los
diálogos. Los recursos precalculados por el `ThemeService` son mejor encaje con lo que ya existe.
