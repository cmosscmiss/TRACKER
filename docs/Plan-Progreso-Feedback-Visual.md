# Plan: progreso e información visual en las acciones que lo requieren

> Documento de trabajo. Acompaña a [Auditoria-IO-Disco-ProgressService.md](Auditoria-IO-Disco-ProgressService.md).
> Estado: **CERRADO**. Fases 1, 3 y 4 hechas y commiteadas. **Fase 2 DESCARTADA** por decisión del usuario (la
> carga de imágenes es casi inmediata → el placeholder/fade apenas se percibe). La Fase 3 puso el progreso de
> descarga/copia a nivel de **servicio** (`ImageLoadingService`), que cubre también las descargas del **WebView**.
> La Fase 4 hizo A (estado de error en rojo, `DangerBrush`, en el ACTIVITY LOG) y C (unificación de mensajes);
> **B descartada** (descarga por bytes determinada). Fuera del alcance final: Fase 2 y Fase 4-B.

## Contexto

La auditoría de I/O de disco reveló dos cosas:

1. Existe un **canal global sólido** de progreso —`ProgressService` → barra 0-100% en `MainWindow.xaml` (3 px) + el widget **ACTIVITY LOG** (`ConsoleControl`) que lista cada `ProgressNotifier` con estado/[%]/[ms]/mensaje— pero varias acciones de disco no pasan por él, y **el "bloqueo" de UI (`SharedDataService.IsUIEnabled`) es solo un flag sin NINGÚN efecto visual** (ni atenuado, ni cursor de espera, ni overlay).
2. **No hay feedback inline por elemento**: las miniaturas se decodifican al hacer scroll y aparecen "de golpe" (sin placeholder ni transición), el drag&drop al dashboard (copia local / descarga web) no muestra nada, y la descarga web baja el buffer entero (`ReadAsByteArrayAsync`) sin progreso.

**Objetivo:** que toda acción que el usuario percibe como "la app está trabajando" tenga feedback —global (barra + ACTIVITY LOG + estado de ocupado) o inline (placeholder/fade/overlay)— **sin** instrumentar lo que es rápido/interno (persistencia config, validación de rutas, máscara del splash, `SetFileSize`): esos quedan fuera por decisión explícita.

**Decisiones tomadas con el usuario:** alcance = **global + inline**; bloqueo = **atenuar + cursor de espera**; cobertura = **drag&drop import**, **alta-res del dashboard** y **miniaturas lazy** (FUERA persistencia/config y validación de rutas).

**Hallazgo que ajusta el alcance:** la carga alta-res del dashboard (`RefreshSelectedGameImagesAsync` → `LoadGameHighResImageBinariesAsync` → `ProcessImagesAsync`, `ImageLoadingService.cs:121,366`) **YA muestra barra + ACTIVITY LOG** ("Loading high‑resolution binaries (i/N)"). Por tanto su parte global está hecha; lo único que falta ahí es lo **inline**, que queda cubierto por la Fase 2 (mismo `GameImageControl`).

---

## Fase 1 — Estado visual de "app ocupada" (global)

Da efecto visual al bloqueo que hoy es invisible. Beneficia de inmediato a las operaciones bloqueantes existentes (borrar huérfanas, arranque, matching), sin tocar ninguna lógica de I/O.

- **Capa de ocupado** ligada a `SharedDataService.IsUIEnabled` (`Services/SharedDataService.cs`):
  - En `Views/MainWindow.xaml`: capa en `Grid.Row="0"` del grid de contenido (queda sobre el contenido y **deja visible** la barra fina 0-100% de la Row 1), atenuado sutil (`AccentDarkBrushOpacity40`) + `ProgressRing` centrado. Arranca `Collapsed`/`Opacity=0`.
  - Aparición/desaparición con fade reutilizando `AnimationService.CreateOpacityAnimation` (estático, ~200 ms) desde `Views/MainWindow.xaml.cs`, suscrito a `SharedDataService.PropertyChanged`.
  - **Cursor de espera**: `Controls/Templates/BusyOverlayPanel.cs` (nuevo) — `Grid` derivado que fija `ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Wait)` en el constructor. Es la única vía limpia en WinUI 3 (`UIElement.ProtectedCursor` es `protected`); como la capa solo es visible/hit-testable durante el bloqueo, el cursor "wait" solo aparece entonces.

**Ficheros:** `Views/MainWindow.xaml`, `Views/MainWindow.xaml.cs`, `Controls/Templates/BusyOverlayPanel.cs` (nuevo). Reutiliza: `AnimationService` (estático), `AccentDarkBrushOpacity40`, patrón `WidgetResizeOverlay`.

---

## Fase 2 — Placeholder + fade-in de imágenes (inline) — DESCARTADA

Cubre **miniaturas lazy al hacer scroll** y la **parte inline de alta-res del dashboard** con un solo cambio, porque ambos usan `GameImageControl`.

- En `Controls/Views/GameImageControl.xaml`, en los dos modos (Default `:44` e StandAlone `:82`), detrás de cada `<Image Source="{x:Bind GameImage.Binary}">`:
  - **Skeleton placeholder** visible mientras `!HasBinary`: `Visibility="{x:Bind GameImage.HasBinary, Converter={StaticResource HiddenIfTrueConverter}, Mode=OneWay}"` (funciona en OneWay porque el setter de `Binary` emite `OnPropertyChanged(nameof(HasBinary))`, `Models/ImageAsset.cs:34`). Contenido: rectángulo `CardBackgroundLightBrush` + `ProgressRing` pequeño.
  - **Fade-in** de la imagen al llegar el binario: en `GameImageControl.xaml.cs`, manejar `Image.ImageOpened` y lanzar `AnimationService.CreateOpacityAnimation(0→1)` (≈200 ms). Alternativa más barata: `EntranceThemeTransition` (ya usado en `:51`).

**Verificar en implementación:** el `DataTemplate` del `GridView` en `Controls/Views/ImageGridControl.xaml` envuelve un `GameImageControl` (el item es `Models.GameImage`, confirmado en `ImageGridControl.xaml.cs:158`). Si no fuera así, replicar el placeholder en esa plantilla.

**Ficheros:** `Controls/Views/GameImageControl.xaml`, `Controls/Views/GameImageControl.xaml.cs`. Reutiliza: `HiddenIfTrueConverter`, `AnimationService`.

---

## Fase 3 — Drag&drop import del dashboard (global + inline)

- **Inyectar `ProgressService`** en `GameImagesDashboardViewModel` (hoy solo tiene `ImageLoadingService` + `ExceptionService`, `:42-43`). Verificar el registro DI en `App.xaml.cs`.
- **Soporte de progreso indeterminado** (infra que consume la descarga): añadir `bool IsIndeterminate` a `Models/ProgressNotifier.cs`, exponer `ProgressIsIndeterminate` en `Services/ProgressService.cs` (patrón de `ProgressValue`/`ProgressVisibility`) y bindear `ProgressBar.IsIndeterminate` en `Views/MainWindow.xaml:151` y `Views/LoadingWindow.xaml:35`.
- **Envolver con progreso** la cadena de drop (`HandleImageDropAsync:513` → `ResolveDroppedTokenAsync:614` → `ResolveLocalImageAsync:636` / `DownloadWebImageAsync:663`), con `StartOperation()` (no bloqueante: el drop es async y la galería debe seguir viva):
  - Copia local: `"{game.Title} | Importing image…"` durante, `"{game.Title} | {n} image(s) imported"` al fin.
  - Descarga web: `"{host} | Downloading image…"` en modo **indeterminado**, cierre `"{host} | Image downloaded"`.
  - Errores siguen yendo por `_exceptionService.Handle` (diálogo).
- **Overlay inline** sobre el área de drop del dashboard ("Importando…" / "Descargando…" + `ProgressRing`) mientras se resuelve el drop, en `Controls/Views/GameImagesDashboardControl.xaml(.cs)` (mismo patrón de overlay de la Fase 1; gobernado por un `bool` del VM tipo `IsImportingDrop`).

**Ficheros:** `Controls/ViewModels/GameImagesDashboardViewModel.cs`, `Controls/Views/GameImagesDashboardControl.xaml(.cs)`, `Models/ProgressNotifier.cs`, `Services/ProgressService.cs`, `Views/MainWindow.xaml`, `Views/LoadingWindow.xaml`, posible ajuste DI en `App.xaml.cs`.

---

## Fase 4 — Pulido (de cierre) — HECHA (A + C); B descartada

- **Descarga por bytes (determinada):** sustituir `ReadAsByteArrayAsync` por lectura en stream con `Content-Length` en `ImageLoadingService.CreateImageFromUrlAsync:392` para reportar 0-100% real. Solo si se quiere precisión; el indeterminado de la Fase 3 ya cubre el UX.
- **Estado de error en ACTIVITY LOG:** `ProgressNotifier.IsException` existe pero `ConsoleControl.xaml:20-51` solo distingue `IsOperationFinished`. Añadir converter/estilo para pintar en rojo las entradas con excepción.
- Pasada de consistencia de mensajes (separador `  |  `, mayúsculas, tiempos).

---

## Verificación

Build (desde `MM4LB.csproj`):
```
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" MM4LB.csproj /t:Build /p:Configuration=Debug /p:Platform=x64 /p:RuntimeIdentifier=win-x64
```
Caveats: si el XamlCompiler casca "en silencio", borrar `obj/`+`bin/` (obj rancio); el filtro de errores debe incluir `MSB30xx`; el fallo de copia del `.exe` (MSB3027) con la app abierta NO es error de código.

Comprobaciones en runtime (interacción de ratón la valida el usuario):
- **Fase 1:** lanzar una operación bloqueante (p. ej. borrar imágenes huérfanas en IMAGES AUDIT) → atenuado + `ProgressRing` + cursor de espera; al terminar, se desvanece y el cursor vuelve. La barra 0-100% sigue visible debajo.
- **Fase 2:** scroll rápido en una galería grande → cada miniatura muestra placeholder y aparece con fade. Cambiar de juego en el dashboard → la imagen grande muestra placeholder hasta cargar el alta-res.
- **Fase 3:** arrastrar un fichero al dashboard → overlay "Importando…" + entrada en ACTIVITY LOG. Arrastrar una URL → overlay "Descargando…" + barra indeterminada + entrada en el log.
- **Fase 4 (si se hace):** descarga con 0-100% real; error de descarga en rojo en el ACTIVITY LOG.
