# Evaluación Exhaustiva del Código — MM4LB

> **Fecha:** 2026-07-05
> **Alcance:** 131 ficheros `.cs` (~35.100 líneas) + 40 XAML. Toda la solución.
> **Naturaleza:** análisis de solo lectura. No se ha modificado ningún fichero ni comportamiento.
> **Método:** lectura íntegra por capas (9 análisis en paralelo) + build limpio para warnings del compilador + verificación cruzada por `grep` de cada hallazgo antes de calificarlo.
> **Objetivo declarado:** calidad de código, coherencia entre servicios, coherencia entre controls, código muerto, optimización, y dónde añadir control de excepciones — **sin romper la funcionalidad actual**.

Este documento es material de referencia. La sección final ([§11](#11-plan-de-remediación-por-fases)) propone un plan de remediación por fases, ordenado de menor a mayor riesgo.

---

## Índice

1. [Resumen ejecutivo](#1-resumen-ejecutivo)
2. [Arquitectura y stack](#2-arquitectura-y-stack)
3. [Salud del build y métricas](#3-salud-del-build-y-métricas)
4. [Bugs de correctness confirmados](#4-bugs-de-correctness-confirmados)
5. [Manejo de excepciones — mapa de huecos](#5-manejo-de-excepciones--mapa-de-huecos)
6. [Coherencia entre servicios](#6-coherencia-entre-servicios)
7. [Coherencia entre controls (MVVM vs code-behind)](#7-coherencia-entre-controls-mvvm-vs-code-behind)
8. [Calidad de código y complejidad (God classes)](#8-calidad-de-código-y-complejidad-god-classes)
9. [Código muerto / eliminable](#9-código-muerto--eliminable)
10. [Oportunidades de optimización](#10-oportunidades-de-optimización)
11. [Plan de remediación por fases](#11-plan-de-remediación-por-fases)

---

## 1. Resumen ejecutivo

MM4LB es una aplicación **funcionalmente madura** y con partes claramente bien diseñadas: lectura de dimensiones de imagen por cabecera de fichero sin decodificar el bitmap, caché LRU por presupuesto de memoria, paralelismo acotado en la lectura de dimensiones, publicación atómica del binario de ffmpeg, memoización de estadísticas con invalidación por `ReferenceEquals`, y un sistema de widgets/layouts flexible. El equipo **conoce las técnicas correctas** — el problema recurrente es que **no se aplican de forma pareja** en todo el código.

Los cinco ejes de deuda, por orden de urgencia:

1. **Nulabilidad no respetada.** El proyecto declara `<Nullable>enable</Nullable>` pero acumula **~1.900 warnings de nulabilidad** (de 2.162 totales). No es cosmético: es la causa raíz de la mayoría de los `NullReferenceException` latentes que se enumeran en [§4](#4-bugs-de-correctness-confirmados) y [§5](#5-manejo-de-excepciones--mapa-de-huecos).
2. **Excepciones con cobertura desigual.** Existe una infraestructura central sólida (`ExceptionService`/`ExceptionDialogService` + 4 sumideros globales), pero solo **7 puntos** de la app la usan, frente a **36 `async void`** y **13+ `catch` vacíos**. Varias rutas de arranque y de operación bloqueante pueden **dejar la UI congelada de forma permanente** o cerrar la app en silencio.
3. **Bugs de correctness reproducibles.** Al menos 3 confirmados y reproducibles hoy (ordenación que lanza excepción, borrado de imagen por nombre colisionante, regex convertido en no-op), más varios latentes.
4. **Coherencia parcial.** Un único servicio con interfaz de 18, dos ficheros en el namespace global, un god-object de estado global (`SharedDataService`), MVVM aplicado a medias (lógica de negocio en code-behind de varios controles).
5. **God classes.** 8 ficheros por encima de las 800 líneas, con `ImageLoadingService` (1.797) y `WidgetPanelControl` (1.678) mezclando 6–10 responsabilidades cada uno.

### Tabla de hallazgos prioritarios

| # | Hallazgo | Categoría | Severidad | Ubicación |
|---|----------|-----------|-----------|-----------|
| 1 | Ordenar columna "LinkedGames" descendente lanza `InvalidOperationException` (bug reproducible) | Correctness | 🔴 Alta | `ImageAuditViewModel.cs:751` |
| 2 | `RemoveImage` por nombre normalizado borra la variante equivocada (`-01`/`-02`) | Correctness | 🔴 Alta | `PlatformImageSet.cs:172-187` |
| 3 | `String.Replace` con patrones regex literales → no-op silencioso (recorte de paréntesis/espacios) | Correctness | 🔴 Alta | `Utilities.cs:143-170` |
| 4 | `ProcessGameAsync`/`MatchImagesWithGamesAsync` sin try/finally → UI bloqueada permanentemente si algo lanza | Excepciones | 🔴 Alta | `ImageLoadingService.cs:1077, 352` |
| 5 | Carga de `Platforms.xml` fuera de try/catch y sin `File.Exists` → revienta el arranque | Excepciones | 🔴 Alta | `PlatformLoadingService.cs:245-286` |
| 6 | `PlatformImageFolder` sin null-check en `<MediaType>` → NRE tumba la carga de plataforma | Excepciones | 🔴 Alta | `PlatformImageFolder.cs:81-84` |
| 7 | `LaunchBoxService.InitializeAsync` → NRE en primer arranque sin plataformas | Excepciones | 🔴 Alta | `LaunchBoxService.cs:60-76` |
| 8 | `ThemeService.InitializeAsync` indexa `Themes[name]` sin fallback → KeyNotFoundException en cascada | Excepciones | 🔴 Alta | `ThemeService.cs:94` |
| 9 | Sumideros globales solo hacen `LogToFile`, nunca `Handle` → fallos no anticipados invisibles al usuario | Excepciones | 🔴 Alta | `App.xaml.cs:146-203` |
| 10 | `Host.StopAsync` en cierre con `catch {}` vacío → guardado de estado falla en silencio | Excepciones | 🔴 Alta | `LoadingWindow.xaml.cs:173-182` |
| 11 | 3 controles con VM Singleton no desuscriben en `Unloaded` → fuga de memoria | Optimización | 🔴 Alta | `GameListControl`, `ImageAuditControl`, `StatsPlatformControl` |
| 12 | `Enumeration.GetAll<T>()` por reflexión sin caché + `new T()` por campo, en hot paths | Optimización | 🔴 Alta | `Enumeration.cs:39-54` |
| 13 | Matching juego↔imagen O(N×M×S) con `List.IndexOf` en vez de índice hash | Optimización | 🔴 Alta | `ImageLoadingService.cs:330-417`, `GamesAuditInGalleryViewModel.cs:59-85` |
| 14 | `switch` de resolución no cubre `High` (default de todas las galerías) → decodifica a resolución nativa en scroll | Optimización | 🔴 Alta | `ImageLoadingService.cs:188-197` |
| 15 | `OnFirstChanceException`: `ToString()` + I/O síncrono en cada excepción del proceso | Optimización | 🔴 Alta | `App.xaml.cs:181-203` |
| 16 | `PlatformDetailsViewModel` duplica la carga inicial (doble decodificación concurrente al arrancar) | Optimización | 🔴 Alta | `PlatformDetailsViewModel.cs:295-309` |
| 17 | Solo 1 de 18 servicios tiene interfaz; `IStatisticsService`/`StatisticsService` en namespace global | Coherencia | 🟠 Media | `App.xaml.cs:76`, `IStatisticsService.cs` |
| 18 | `SharedDataService` god-object de estado global (40 ficheros, bindings de 3 niveles) | Coherencia | 🟠 Media | `SharedDataService.cs` |
| 19 | `ImageLoadingService` (1.797 líneas) God Class autodeclarado, ~10 responsabilidades | Complejidad | 🟠 Media | `ImageLoadingService.cs` |
| 20 | `WidgetPanelControl` (1.678) = vista + controlador drag&drop + persistencia de `AppSettings` | Complejidad | 🟠 Media | `WidgetPanelControl.xaml.cs` |

Leyenda: 🔴 Alta = corregir pronto (bug real o riesgo de crash/bloqueo/rendimiento medible). 🟠 Media = deuda estructural relevante. 🟡 Baja = pulido.

---

## 2. Arquitectura y stack

**Tipo:** aplicación de escritorio **WinUI 3 / Windows App SDK 1.8**, .NET 8 (`net8.0-windows10.0.19041.0`), sin empaquetar y self-contained.

**Patrón:** MVVM con `CommunityToolkit.Mvvm` 8.4, inyección de dependencias con `Microsoft.Extensions.Hosting`/`DependencyInjection`, host genérico (`IHost`) con un `IHostedService` (`ApplicationHostService`).

**Dependencias notables:** `Microsoft.Data.Sqlite` (base de datos de LaunchBox), `LiveChartsCore.SkiaSharpView.WinUI` (gráficas), `YoutubeExplode` (descarga de vídeo), `Newtonsoft.Json` (persistencia de settings), `Microsoft.Graphics.Win2D` (efectos de imagen/tinte).

**Distribución por capas:**

| Capa | Ficheros | Líneas | Notas |
|------|---------:|-------:|-------|
| `Controls/` (Views + ViewModels + Templates + Dialogs) | 59 | 21.094 | El grueso de la app |
| `Services/` | 18 | 6.947 | Lógica de dominio e infraestructura |
| `Models/` | 18 | 3.403 | Modelos + `AppSettings` (827) |
| `Views/` (ventanas) | 7 | 1.718 | `MainWindow` partido en 5 ficheros |
| `ViewModels/` (ventanas) | 3 | 591 | |
| `Enums/` | 8 | 456 | Mezcla enums planos y smart-enums |
| `Helpers/` (converters) | 15 | 430 | |
| `Contracts/Services/` | 2 | 193 | Solo 2 interfaces en todo el proyecto |

**Flujo de arranque:** `App.OnLaunched` → decide entre `LoadingWindow` (si LaunchBox está configurado) y `SetLaunchBoxFoldersWindow` → `LaunchBoxService.InitializeAsync` → `PlatformLoadingService` (parseo de `Platforms.xml` ~1 MB + XML por plataforma + SQLite `LaunchBox.Metadata.db`) → `MainWindow`.

**Estado compartido:** `SharedDataService` (singleton) centraliza selección de dominio (`SelectedPlatform`/`SelectedGame`/`SelectedImageSet`/`SelectedImage`) y un flag `IsUIEnabled`. Es el punto de acoplamiento más fuerte de la app (ver [§6](#6-coherencia-entre-servicios)).

---

## 3. Salud del build y métricas

El build limpio (`dotnet build -t:Rebuild -c Debug -p:Platform=x64`) **compila correctamente (0 errores)** pero emite **2.162 warnings**:

| Código | Nº | Significado | Implicación |
|--------|---:|-------------|-------------|
| CS8618 | 772 | Campo/propiedad no-nullable sin inicializar al salir del constructor | Nulabilidad no respetada |
| CS8622 | 328 | Nulabilidad de parámetro no coincide (handlers de evento) | Nulabilidad no respetada |
| CS8625 | 224 | No se puede convertir `null` literal a tipo no-nullable | Nulabilidad no respetada |
| CS8600 | 212 | Conversión de posible `null` a tipo no-nullable | Nulabilidad no respetada |
| CS8604 | 204 | Posible argumento `null` | Riesgo de NRE |
| CS8603 | 124 | Posible retorno `null` | Riesgo de NRE |
| CS8601 | 108 | Posible asignación `null` | Riesgo de NRE |
| CS8602 | 104 | Desreferencia de posible `null` | **Riesgo directo de NRE** |
| CS0108 | 24 | Miembro oculta al heredado (falta `new`) | P. ej. `PlatformDetailsViewModel.SharedDataService` |
| MVVMTK0045 | 16 | `[ObservableProperty]` sobre campo (recomendado partial property para AOT/WinRT) | Deuda con el toolkit |
| CS8765 / CS8767 | 20 | Nulabilidad de parámetro no coincide con miembro sobrescrito | `ConvertBack` de los converters |
| CS0659 | 4 | Sobrescribe `Equals` pero no `GetHashCode` | **Posible bug de igualdad** |
| CS0628 | 4 | Miembro `protected` en clase `sealed` | `LaunchBoxService` |
| CS0252 | 4 | Comparación de referencia posiblemente no intencionada | **Posible bug** — revisar |
| CS0414 | 2 | Campo asignado pero nunca usado | Código muerto |

**Lectura:** ~1.876 warnings (87%) son de nulabilidad (CS86xx). El equipo activó `Nullable` pero el código se escribió sin honrarlo. Consecuencias:

- El compilador **ya está señalando** buena parte de los puntos de NRE que se detallan en [§5](#5-manejo-de-excepciones--mapa-de-huecos), pero el ruido (2.162 líneas) hace que esos avisos sean invisibles en la práctica.
- **CS0252** (4) merece revisión manual inmediata: comparaciones `==` sobre objetos que podrían pretender igualdad de valor.
- **CS0659** (4): un tipo sobrescribe `Equals` sin `GetHashCode` — riesgo si se usa en `Dictionary`/`HashSet`.

> **Recomendación transversal:** tratar la reducción de warnings de nulabilidad como un proyecto propio, **fichero a fichero** (no global), empezando por `Services/` y `Models/`. Cada fichero saneado convierte avisos ignorados en garantías del compilador. Ver [Fase 5](#fase-5--reducción-de-nulabilidad-progresivo).

**Ausencia de tests:** no hay ningún proyecto de test ni fichero de test en el repositorio. Esto es el mayor factor de riesgo para "no romper la funcionalidad actual": **cada cambio debe verificarse manualmente ejecutando la app**. Ver la nota de red de seguridad en [§11](#11-plan-de-remediación-por-fases).

---

## 4. Bugs de correctness confirmados

Estos no son riesgos teóricos: son defectos presentes hoy. Se listan primero porque su corrección **no cambia funcionalidad deseada** (solo elimina comportamiento incorrecto) y es de bajo riesgo.

### 4.1 🔴 Ordenar "LinkedGames" descendente lanza excepción — `ImageAuditViewModel.cs:751`
`orderby item.LinkedGames descending` sobre un `List<Game>` que **no implementa `IComparable`**. `OrderByDescending` lanza `InvalidOperationException` en cuanto se enumera (el `new List<GameImage>(...)` que lo envuelve fuerza la enumeración). La rama **ascendente** de la misma columna (línea 748) sí usa `.Count`; la descendente se olvidó. **Reproducible**: basta ordenar esa columna en descendente. Ningún llamador tiene try/catch. **Fix:** añadir `.Count` en la rama descendente.

### 4.2 🔴 `RemoveImage` borra la variante equivocada — `PlatformImageSet.cs:172-187`
Localiza la entrada a borrar con `ImageFilesLowerCase.IndexOf(Utilities.ImageFileNameToGameString(image.File))`, es decir **por contenido normalizado**. `ImageFileNameToGameString` recorta el sufijo `-NN`, así que "Sonic-01.png" y "Sonic-02.png" normalizan al **mismo string**; `IndexOf` devuelve la primera coincidencia. Borrar la variante 2 puede eliminar los datos (ruta, tamaño) de la variante 1. Confirmado con llamadas reales en `ImageLoadingService.cs:1031,1206,1360,1414` y `ImageAuditViewModel.cs:237`. **Fix:** buscar por `_imageFiles.IndexOf(image.File)` (ruta exacta, única).

### 4.3 🔴 Regex convertido en no-op silencioso — `Utilities.cs:143-170`
`ReplaceAllSpecialCharactersWithUnderscores`, `ReplaceSpecialCharactersWithUnderscores` y `RemoveAllSpecialCharacters` usan `param.Replace("\\(.*$", "")` y `Replace("[ ]{2,}", " ")` con **`String.Replace` (literal)**, no `Regex.Replace`. Los patrones nunca aparecen literalmente en un título real → las operaciones son **no-op**. Un título "Sonic (Europe)" no pierde el paréntesis pese a que el comentario dice que sí. Afecta al renombrado de ficheros (`ImageLoadingService.cs:1777`) y a la generación de search strings (`Game.cs:176,180,184`). **Fix:** `Regex.Replace` (con `Regex` estático cacheado, ver [§10](#10-oportunidades-de-optimización)).

### 4.4 🟠 `LaunchBoxFoldersValid` siempre `false` — `AppSettings.cs:230-238, 552-566`
`SetInternalSettings()` calcula `LaunchBoxDataFolder = {root}\Data` y llama a `LaunchBoxPathValidator.Validate(LaunchBoxDataFolder)`, pero `Validate` vuelve a combinar `"Data"` sobre el argumento → comprueba `{root}\Data\Data\...`, que nunca existe. El campo del modelo da **siempre false**. Latente hoy porque nadie lo lee (la validación real la hace bien `SetLaunchBoxFoldersViewModel.cs:121` pasando la raíz), pero es una trampa. **Fix:** pasar `LaunchBoxFolder` o eliminar el campo si es vestigial.

### 4.5 🟠 Throttle de progreso roto — `ImageLoadingService.cs:387`
`if (c0 % 1 == 0)` es siempre verdadero (`x % 1 == 0` para todo entero). El "throttle" no throttla: reporta progreso (con notificación de propiedad y marshaling a UI) en **cada** iteración. Sobre colecciones grandes añade overhead evitable. **Fix:** `if (c0 % Math.Max(1, platform.Games.Count / 100) == 0)`.

### 4.6 🟡 `MediaType` con constructor público — `MediaType.cs:102`
El constructor de 2 argumentos es `public` (en el resto de smart-enums es `private`). Rompe el invariante de "catálogo cerrado": permitiría crear un `MediaType` fuera del catálogo, que no aparecería en `GetAll<MediaType>()` ni se restauraría de JSON (el conversor lanzaría). No se usa hoy. **Fix:** `private`.

### 4.7 Revisar (señalados por el compilador)
- **CS0252** (4 casos): comparaciones `==` sobre objetos posiblemente no intencionadas. Localizar y decidir `Equals` vs referencia.
- **CS0659** (4): tipo con `Equals` sin `GetHashCode`. Revisar si se mete en `HashSet`/`Dictionary`.
- **`ImageAuditViewModel.cs:693-702`**: la columna "Dimensions" ordena por `string` (`"1920x1080"`) → orden **lexicográfico**, no numérico ("1920x1080" < "640x480"). Ordenar por tupla `(Width, Height)`.

---

## 5. Manejo de excepciones — mapa de huecos

Esta es la sección que responde directamente a "en qué puntos deberíamos añadir control de excepciones".

### 5.1 El diseño central es bueno, pero está infrautilizado
`ExceptionService` (log a fichero thread-safe + `Handle` que dispara `ErrorMessageRaised`) y `ExceptionDialogService` (cola en hilo de UI, distingue `OperationCanceledException`) forman un mecanismo correcto. **Pero solo 7 puntos** de la app llaman a `Handle`: `PlatformLoadingService.cs:277,391`, `PersistAndRestoreService.cs:179`, `WebViewViewModel.cs:394,443`, `GameImagesDashboardViewModel.cs:789,864`, `App.xaml.cs:226`. Frente a eso: **36 `async void`** y **13+ `catch` vacíos**.

**Problema estructural (🔴 Alta) — `App.xaml.cs:146-203`:** los 4 sumideros globales (`App_UnhandledException`, `OnDomainUnhandledException`, `OnUnobservedTaskException`, `OnFirstChanceException`) **solo llaman a `LogToFile`, nunca a `Handle`**. La ruta "bonita" (diálogo al usuario) es opt-in: solo se activa si alguien envolvió el código en try/catch. Es decir, los fallos **no anticipados** —justo los más peligrosos— se registran en `MM4LB.log` pero el usuario no ve nada y la app puede quedar en estado indefinido. **Fix:** en `App_UnhandledException` (el único fiable en el hilo de UI antes de cerrar), añadir `exceptionService.Handle(e.Exception)`.

### 5.2 🔴 Operaciones bloqueantes que pueden congelar la UI para siempre
El patrón `StartBlockingOperation()` (pone `IsUIEnabled = false`) sin `try/finally` que garantice `FinishBlockingOperation()`:

- **`ImageLoadingService.ProcessGameAsync` (1077-1159)**: sin try/catch. Si `FileSystemService.RenameFileAsync` (→ `File.Move`) o `GameImage.SetFileName` lanzan (fichero bloqueado, criterio null), `FinishBlockingOperation()` (1156-1158) **nunca se ejecuta** → UI bloqueada hasta reiniciar.
- **`ImageLoadingService.MatchImagesWithGamesAsync` (352-417)**: el `finally` solo libera el semáforo, no llama a `FinishBlockingOperation()`. Un `image` null en 381 → NRE → mismo bloqueo.
- **`PlatformLoadingService.LoadPlatformSetAsync` (245-286)**: `StartBlockingOperation()` y luego, **fuera de todo try**, `LoadXmlDocument(Platforms.xml)`. Si el XML falta/está corrupto/bloqueado, revienta el arranque y deja `IsUIEnabled = false`.

**Fix:** envolver desde `StartBlockingOperation()` hasta el `return` en `try/finally` que garantice `FinishBlockingOperation()`.

### 5.3 🔴 NullReferenceException en rutas de arranque
- **`PlatformImageFolder.cs:81-84`**: `platformImageFolder["MediaType"].InnerText` sin null-check (las dos líneas anteriores sí lo tienen). Un `<PlatformFolder>` sin `<MediaType>` → NRE que tumba la carga completa de plataformas (el bucle en `PlatformLoadingService.cs:190-208` no tiene try/catch por iteración).
- **`LaunchBoxService.cs:60-76`**: `platformSet.Platforms.FirstOrDefault(...)` puede ser null (instalación de LaunchBox sin plataformas); `platform.SetSelectedImageSet(...)` sin comprobar null → NRE. **Rompe el primer arranque de un usuario nuevo** (pasa el gate de `App.xaml.cs` porque `Launchbox.exe` sí existe).
- **`ThemeService.cs:94`**: `Themes[_appSettings.Theme.Name]` (indexador directo) lanza `KeyNotFoundException` si el nombre no existe; deja `_currentTheme = null` → NRE en cascada por toda la UI. El doc-comment **promete un fallback que el código no implementa**; `ApplyTheme` (línea 105) sí usa `TryGetValue`. **Fix:** `TryGetValue` con fallback a tema por defecto.

### 5.4 🔴 `async void` sin protección en rutas transitadas
`async void` no puede ser capturado por el llamador; una excepción no controlada puede tumbar el proceso.

- **`ImageAuditViewModel.cs:213-269` (`OnDeleteOrphanClickedAsync`)** y **`:290-320` (`OnGetImageDimensions`)**: `async void` colgados de un `RelayCommand` (no `AsyncRelayCommand`), sin try/catch, borrando ficheros reales.
- **`PlatformDetailsViewModel.cs:313-345`**: `OnSelectedPlatformChanged`/`OnPlatformImagesChanged` `async void` que llaman a cargas de imagen no protegidas. Se disparan en **cada cambio de plataforma**.
- **`GameImagesDashboardViewModel.cs:1284-1318` (`OnImageAddedToGame`)**: `async void`, carga binario sin try/catch. En cada alta de imagen (drop, descarga, undo).
- **`WidgetPanelControl.xaml.cs:331,554,639,1394,1410` + `ToolbarControl.xaml.cs:98,138`**: 6 handlers `async void` de drag&drop/toolbar sin try/catch (uno crea un `RenderTargetBitmap` que puede fallar). **Cero** usos de `ExceptionService` en los 16 ficheros del subsistema de widgets.
- **`ImageCollectionImportViewModel.cs:106-132,168-197`**: `async void` sobre `RelayCommand`; si lanzan, `RaiseCanExecuteCommands(false)` nunca se revierte → **botones "Import"/"Folder" deshabilitados permanentemente**.
- **`WebViewControl.xaml.cs:146-170` (`OnLoaded`)**: si falla la creación de WebView2 (runtime Evergreen no instalado — escenario real), solo `Debug.WriteLine` → widget mudo para siempre, sin aviso.

**Fix:** convertir a `AsyncRelayCommand` donde sea un comando, o envolver el cuerpo en try/catch con `Handle`. Patrón de referencia: `WebViewViewModel.AddImageFromBrowserAsync`.

### 5.5 🔴/🟠 `catch` vacíos que tragan errores
- **`LoadingWindow.xaml.cs:173-182`**: `await Host.StopAsync()` y `Host.Dispose()` en `catch {}` vacíos. Es la ruta de **guardado de estado más importante** de la sesión (SaveConfig de widgets + `PersistData`) y la única que no pasa por `LogToFile` — contradice la filosofía de los 4 sumideros globales.
- **`BackupService.cs:59-114`**: 5 `catch` sin log; además `SetTotals(0,0)` incondicional tras el intento de borrado → muestra "0 backups" aunque queden ficheros bloqueados.
- **`FileSystemService.cs:87-90,121-124`**: `DeleteImageFileAsync`/`RestoreImageFileAsync` con `catch {}` vacío → si el `File.Copy` de backup tiene éxito pero el `File.Delete` falla, backup huérfano + original intacto + cero rastro.
- **`PersistAndRestoreService.PersistData` (117-140)**: sin try/catch **ni escritura atómica**. Disco lleno/permisos → `.ini` truncado. Contrasta con la publicación atómica de ffmpeg que el mismo repo sí hace bien.
- **`GamesAuditViewModel.cs:161-164`**, **`ImageAuditViewModel.cs:455-457`**, **`StatsPlatformViewModel` (3 sitios)**, **`ImageGridGameViewModel.cs:247-254`**: `catch {}` mudos, varios documentados como "a veces lanza al abrir la vista" — parches sobre un problema no resuelto.

**Fix:** política única — **loguear siempre** vía `ExceptionService.LogToFile` (aunque se decida no mostrar diálogo), capturar el tipo concreto cuando se conozca.

### 5.6 🟠 Otros
- **`ImageLoadingService.cs:204`**: `catch (Exception e) { throw new Exception(..., e); }` reemplaza el tipo real por `Exception` genérico → ningún llamador puede filtrar por tipo.
- **`YoutubeDownloadService.cs:252`**: `HttpClient` sin `Timeout` explícito → un timeout se confunde con cancelación del usuario ("download cancelled" engañoso).
- **`FileSystemService.LoadXmlDocument` (162-167)**: `ReadAllTextAsync` + `LoadXml(string)` ignora la declaración `encoding=` del XML → mojibake/`XmlException` con nombres en Latin-1. Usar `XmlDocument.Load(stream)`.
- **`GameImageControl`/`PlatformDetailsControl`**: ningún reproductor suscribe `MediaFailed` (confirmado: no aparece en ningún `.cs`) → vídeo corrupto/códec no soportado congela el preview sin feedback. `new Uri(path)` sin try/catch en `UpdateVideo`/`UpdatePreviewVideo`.
- **`ExceptionDialogService.cs:164`**: botón "Aceptar" (español) hardcodeado, rompiendo la convención en inglés del resto de diálogos.

---

## 6. Coherencia entre servicios

### 6.1 🟠 Política de interfaces inexistente
De **18 servicios, solo `StatisticsService`** se registra vía interfaz (`App.xaml.cs:76`). Los otros 17 se registran como tipo concreto, y varios son `sealed` (imposibles de sustituir aunque se quisiera). El proyecto tiene 4 interfaces en total, y 2 de ellas (`IAnimationHandle`, `IAppDialogPrimaryGate`) **ni siquiera viven en `Contracts/`**. La carpeta `Contracts/Services` es característica de las plantillas Template Studio de WinUI → estructura heredada sin adoptar la práctica. **No hay regla escrita** de cuándo un servicio merece interfaz. Impacto real: sin tests hoy no duele, pero bloquea la testabilidad futura de ViewModels. **Decisión a tomar:** documentar el criterio ("interfaz solo para X") o extender interfaces a los servicios de dominio.

### 6.2 🟠 `IStatisticsService` y `StatisticsService` en el namespace global
Confirmado: **ningún `namespace`** en ambos ficheros (viven en el global), mientras `IWidgetViewModelBase` sí declara `MM4LB.Contracts.Services`. La única pareja interfaz+implementación del proyecto es la que rompe la convención `MM4LB.*`. Pasa desapercibido porque C# resuelve el namespace global sin `using`. **Fix trivial:** envolver ambos en su namespace.

### 6.3 🟠 `SharedDataService` como god-object de estado global
Referenciado en **40 ficheros**, incluidos 8 `.xaml` con bindings de hasta 3 niveles (`ViewModel.SharedDataService.SelectedPlatform.Name`). Las 15 ViewModels de widget lo reciben **y lo reexponen como propiedad pública** (`WidgetViewModelBase.cs:33`), permitiendo que cualquier vista lo alcance saltándose el VM (violación de Ley de Demeter). Mezcla selección de dominio con un flag de presentación (`IsUIEnabled`). Cualquier cambio de forma obliga a tocar decenas de bindings. **Mitigación incremental (no urgente):** dejar de exponerlo público en `WidgetViewModelBase`; que cada VM exponga propiedades derivadas propias.

### 6.4 🟠 Política de manejo de excepciones sin unificar
Conviven, sin criterio: **loguear y relanzar** (`LoadPlatformSetAsync:277`), **loguear y tragar** (`LoadGamesLbDatabaseAsync:389`), y **tragar en silencio sin `ExceptionService`** (`BackupService`, `FileSystemService`, `PersistData`). Un documento de estándares debería fijar la norma (ver [§5.5](#55--catch-vacíos-que-tragan-errores)).

### 6.5 🟠 Dependencia circular `ProgressService` ↔ `ConsoleViewModel`
`ConsoleViewModel.ClearBackupAsync` resuelve `ProgressService` vía `App.GetService<ProgressService>()` (Service Locator) con un comentario que **admite el ciclo de DI**. Además `ApplicationHostService.StopAsync` (91) también usa Service Locator para `IEnumerable<IWidgetViewModelBase>` en una clase que por lo demás usa DI por constructor. **Fix:** romper el ciclo (extraer "vaciar backup" a un comando de aplicación independiente) o inyectar por constructor donde se pueda.

### 6.6 🟡 Inconsistencias menores pero sistemáticas
- **Naming de fichero:** `ImageBinariesCacheServices.cs` (plural) contiene la clase `ImageBinariesCacheService` (singular).
- **Capitalización:** `LaunchBoxPlatformsFolder` (B) vs `LaunchboxPlatformsXmlFile`/`LaunchboxSettingsXmlFile`/`LaunchboxGamesDbFile` (b), las cuatro en el mismo método.
- **`sealed` sin regla:** unos servicios lo son, otros no, ninguno tiene subclases.
- **Validación de args null en constructores:** `FileSystemService`/`PersistAndRestoreService` validan todo; `LaunchBoxService` solo `appSettings`; el resto nada.
- **`#region`:** unos servicios los usan, otros no.
- **`SelectedImage`** (en `SharedDataService`) es la única de las 4 propiedades "Selected*" sin evento tipado propio → consumidores usan string literal `"SelectedImage"` (`ImageAuditViewModel.cs:544`) vs `nameof` en otros. Añadir `SelectedImageChanged`.
- **`NotifyInitialState()`** invocado desde 3 constructores de VM: reemite eventos a **todos** los suscriptores ya registrados, con refrescos redundantes dependientes del orden de construcción DI. Centralizar en un único punto tras registrar todos los widgets.

---

## 7. Coherencia entre controls (MVVM vs code-behind)

La inconsistencia clave del proyecto. El criterio "cuándo toca ViewModel" no está definido, y —paradójicamente— **la lógica de negocio más compleja es la que se quedó en code-behind**.

### 7.1 🔴 Controles con lógica de negocio sin ViewModel
- **`ChartTypeSelectorControl.xaml.cs` (1.046 líneas)**: selección Top-N, ordenación, interpolación de color, construcción de series LiveCharts (single y multi) — todo en code-behind de un `UserControl` sellado, no testeable. Contiene algoritmos puros (`ApplyTopN`, `ApplySort`, `BuildColumnOrder`, `TruncateLabels`) atrapados como métodos privados.
- **`GameImageControl.xaml.cs` (568)**: clasificación vídeo/imagen y lógica de reproducción en code-behind.
- **`WidgetPanelControl.xaml.cs` (1.678)** y **`WidgetSelectorControl.xaml.cs` (806)**: la **regla de exclusividad de slots** (qué widget ocupa qué slot, expulsión del ocupante previo, normalización al cambiar layout) vive en code-behind, mientras existe un `LayoutSelectorViewModel` que gestiona la sección hermana de la misma configuración.

**Contraste:** `WebViewControl`/`PlatformDetailsControl` documentan explícitamente por qué delegan en el VM y dejan en code-behind solo lo que WinUI no permite por binding. Y `WidgetStatCardControl`/`LayoutItemControl` **correctamente no tienen VM** (solo estado visual) — no todo code-behind es malo.

**Matiz arquitectónico (🟠):** los `UserControl` de WinUI necesitan constructor sin parámetros, así que resuelven servicios con `App.GetService<T>()` (Service Locator) en el constructor (`WidgetPanelControl:256`, `WidgetBaseControl:359`, etc.). Esto explica en parte por qué no tienen VM inyectado. **Conviene documentarlo como decisión** (no como accidente) y aun así extraer las reglas de negocio a servicios/VM testables.

### 7.2 🔴 Persistencia de layout gestionada por la vista — `WidgetPanelControl.xaml.cs:900-1417`
La lectura/escritura de tamaños de layout (`_appSettings.LayoutSelectorControl.LayoutSizes`) se hace directamente desde code-behind, mientras `LayoutSelectorViewModel` gestiona la sección hermana (`Gap`, `CornerRadius`, `PanelMargin`) con `LoadConfig`/`SaveConfig`. La misma clase de settings tiene una parte gobernada por VM y otra por la vista. **Fix:** mover `LayoutSizes` a `LayoutSelectorViewModel`.

### 7.3 🔴 Fugas de memoria por suscripciones no liberadas
Los ViewModels de widget son **Singleton** en DI y sus controles se montan/desmontan dinámicamente en el panel. La desuscripción ocurre solo cuando cambia la instancia de la DP `ViewModel` (que, siendo Singleton, no cambia), **nunca en `Unloaded`**:

- **`GameListControl`**, **`ImageAuditControl`**, **`StatsPlatformControl`**: no desuscriben en `Unloaded` (o no tienen handler `Unloaded`). Cada ciclo montar/desmontar deja la instancia de control anterior enganchada al VM para siempre.
- **`ChartTypeSelectorControl`**: `OnUnloaded` desuscribe `ThemeChanged` pero **no pone `_themeService = null`** → tras remontar, la guarda `if (_themeService == null)` impide resuscribir → "fuga de reactividad": la gráfica deja de reaccionar al tema (se reutiliza embebido en 3 controles).
- **`WidgetPanelControl.SetWidgets`**: engancha `DragStart/Move/End` + `PropertyChanged` por widget que `OnUnloaded` no desuscribe.
- **`FooterEventViewerControl`**: sin `Loaded`/`Unloaded`; el `DispatcherTimer` del marquee solo se libera si cambia la DP.

**Contraste:** `PlatformDetailsControl`, `ImageGridControl`, `ImageTypeControl` **sí** desuscriben simétricamente en `Unloaded`. Riesgo real hoy limitado (instancias mayormente únicas por sesión), pero es la fuga más concreta y accionable. **Fix:** `Unloaded` simétrico en los controles señalados.

### 7.4 🟠 Duplicación entre controles
- **`EnsureConfigurationLoaded()`** (patrón `_configurationLoadedViewModel` + `ReferenceEquals` + `LoadConfig`) copiado literal en **8 controles**. Extraer a helper genérico `ViewModelConfigGate<T>`.
- **Glue-code "Sections + AnimationsSpeed"** del CartesianChart duplicado en `PlatformDetailsControl:208-229` y `StatsPlatformControl:73-90` (incluido el comentario).
- **`GaDataGrid_Sorting`/`IaDataGrid_Sorting`** idénticos (`GamesAuditControl:124-143`, `ImageAuditControl:184-203`).
- **`PlatformListControl.xaml.cs:59-64`**: `OnLoaded` llama `ViewModel.LoadConfig()` **sin comprobar null**, único de los 9 controles con el mismo patrón que no protege → NRE potencial.

### 7.5 🟠 Otros
- **`SettingsControl.xaml`**: único que usa `{Binding}` clásico (reflexión, sin comprobación en compilación) en vez de `{x:Bind}`; conserva el comentario de plantilla de WinUI y `using` sin usar → scaffolding nunca limpiado.
- **`ToggleGroup<T>`/`ToggleGroupItem`** (en `Controls/Templates/`) declaran `namespace MM4LB.Controls.ViewModels` (no coincide con la carpeta) y son conceptualmente ViewModels, no plantillas. Además `ToolbarControl` reimplementa a mano la selección exclusiva que `ToggleGroup` ya ofrece.
- **`FooterSoundControl`** acopla un control de pie de página al `GameImagesDashboardViewModel` (1.380 líneas) solo para leer `IsMuted`/volumen.

---

## 8. Calidad de código y complejidad (God classes)

8 ficheros superan las 800 líneas. Los candidatos a división, con la descomposición propuesta:

### 8.1 🟠 `ImageLoadingService.cs` (1.797) — God Class autodeclarado
El propio comentario dice "ALL image-related operations". ~10 responsabilidades, 6 dependencias inyectadas. Propuesta de división:
`ImageBinaryLoadingService` (decodificación/caché) · `ImageMatchingService` (emparejamiento) · `PlatformAssetService` (assets de plataforma + undo) · `MediaAcquisitionService` (descarga URL/YouTube/data-URI) · `MediaDeletionService` (borrado/undo/procesar) · `ImageDimensionService`. Cada VM dependería solo del servicio que usa (hoy todos dependen de la clase completa).

### 8.2 🟠 `WidgetPanelControl.xaml.cs` (1.678)
6 responsabilidades: aplicación de layout, drag&drop de widgets (ghost visual), drag de handles de fila (máquina de estados propia), splitters/hitboxes, animación de banda superior, y persistencia en `AppSettings`. Extraer: `WidgetDragController`, `RowSplitterController`, `TopBandAnimator`, y mover la persistencia al VM ([§7.2](#72--persistencia-de-layout-gestionada-por-la-vista--widgetpanelcontrolxamlcs900-1417)).

### 8.3 🟠 God-ViewModels y duplicación de gráficas
- **`GameImagesDashboardViewModel.cs` (1.380)**: 5 responsabilidades (layout, reproducción de vídeo, navegación "procesar y siguiente", resolución de drag&drop de 4 fuentes, colección de imágenes). Extraer `VideoPlaybackState` e `ImageDropResolver`.
- **`ImageAuditViewModel.cs` (1.089)**: vista dual grid/list + estadísticas + gráfica LiveCharts completa (~200 líneas) + borrado de huérfanos + ordenación de 8 columnas.
- **`StatsPlatformViewModel.cs` (945)** y **`PlatformDetailsViewModel.cs` (861)**: duplican ~90 líneas de gráficas de cobertura (`BuildCoverageByGameChart`/`BuildCoverageByPlatformChart` casi idénticas). Extraer **`CoverageChartBuilder`** compartido.
- **`StatsGlobalViewModel`/`StatsPlatformViewModel`**: `ApplyChartConfig`/`StoreChartConfig` idénticos al carácter + scaffolding de suscripción. Extraer **`ChartWidgetViewModelBase : WidgetViewModelBase`**.

### 8.4 🟠 `AppSettings.cs` (827) — god-object de configuración
17 clases de settings anidadas + validador con I/O + **catálogo de temas hardcodeado** (~100 líneas de datos) + `BindProperties` cuyo diccionario de claves (`"General"`, `"LaunchBox"`...) **duplica** la lista de propiedades declarada arriba → añadir una sección exige tocar 2 sitios; si se olvida el segundo, esa sección **deja de restaurarse en silencio**. Partir por sección, mover temas a recurso/JSON, y sustituir el diccionario por reflexión/`nameof`.

### 8.5 🟠 Otros grandes
- **`StatisticsService.cs` (889)**: estadísticas de juego + imagen + cobertura + formato en una clase; `IStatisticsService` con 24 métodos (ISP violado). Segmentar.
- **`FileSystemService.cs` (408)**: >50% es un lector binario de cabeceras de imagen (PNG/JPEG/GIF/BMP/WebP) sin relación con el resto. Extraer `ImageHeaderReader`.
- **`AnimationService.cs` (686)**: interfaz + 3 implementaciones de handle (una muerta) en un fichero. Separar.
- **`Utilities.cs`**: cajón de sastre (regex, URLs de búsqueda, scraping HTML, y lógica de dominio de nomenclatura). Extraer la lógica de negocio a un servicio.
- **`MainWindow.GameList.cs` (609)**: coreografía de animación que es un controlador de layout independiente; ~50% del código de `MainWindow`.

### 8.6 🟡 Estilo transversal
- **Generadores del toolkit infrautilizados:** los VMs grandes implementan a mano `_campo ??= new RelayCommand(...)` y `SetProperty`, mientras `SetLaunchBoxFoldersViewModel` y `PlatformListViewModel` sí usan `[ObservableProperty]`/`[RelayCommand]`. Migrar reduciría boilerplate y unificaría estilo (ojo al warning MVVMTK0045: preferir partial properties).
- **Magic numbers** repetidos sin constante (ratios de layout `0.5`/`0.01`/`0.99`/`0.12`/`0.88`, tamaños de panel, offsets de radio).
- **Comentarios XML huérfanos/rotos** por reordenación de métodos (`ChartTypeSelector:735`, `ImageAudit:166`, `WidgetBaseControl:300` con `cref` a miembro inexistente).

---

## 9. Código muerto / eliminable

**Confirmado** (verificado por `grep` global; eliminable con bajo riesgo tras una última comprobación con "Find References"):

| Elemento | Ubicación | Nota |
|----------|-----------|------|
| `ImageBinariesCacheService.ClearAll()` | `ImageBinariesCacheServices.cs:120-130` | Sin llamadas. Además arrastra un `GC.Collect()` (antipatrón) |
| `FileSystemService.DeleteFileAsync` | `FileSystemService.cs:37-51` | Sin llamadas |
| `FileSystemService.IsFileAnImage` | `FileSystemService.cs:155` | Sin llamadas |
| `AnimationService.CompositionOffsetAnimationHandle` + `CreateOffsetAnimation` | `AnimationService.cs:330-422,675` | La **única** animación por GPU (la más eficiente) está muerta |
| `ThemeService.ApplyTheme(string)` | `ThemeService.cs:103-137` | Único método de cambio de tema en caliente; nada lo dispara (infra de refresco de iconos inerte) |
| `SharedDataService.Images` | `SharedDataService.cs:72` | Sin uso; casi homónimo de `GameImages` (trampa de mantenimiento) |
| `ProgressService._statisticsService` + rama stats | `ProgressService.cs:14,81-91` | Inyectado y nunca leído; `if` con cuerpo vacío |
| `ConsoleViewModel.IsOperationInExecution` | `ConsoleViewModel.cs:36` | Sin consumidor; la plomería de 8 sitios en `ProgressService` notifica **sobre el objeto equivocado** |
| `BooleanToColorInvertedConverter` | `Helpers/` | Registrado en DI, 0 referencias XAML |
| `NullToVisibilityInvertedConverter` | `Helpers/` | Declarado en `App.xaml`, 0 referencias |
| `TruncateTextConverter` | `Helpers/` | Declarado en `App.xaml`, 0 referencias (el de implementación más elaborada) |
| `LayoutItemControl.LayoutTypeProperty` (DP) + `LayoutType` | `LayoutItemControl.xaml.cs:18,35` | DP "fantasma" desconectada; `LayoutType` es write-only |
| Línea comentada `//SelectedImageSet = ...` | `Platform.cs:270` | Limpieza trivial |
| `//image.SetDefaultImage();` | `ImageLoadingService.cs:173` | Invoca método inexistente |

> **20% de los converters (3 de 15) no tienen consumidor.** Refuerza la propuesta de [§9-bis] de unificar los pares Normal/Inverted.

**Posible — verificar con "Find References" antes de tocar:**
- `Enumeration.AbsoluteDifference` y `IComparable/CompareTo` (`Enumeration.cs:76,94`) — sin uso aparente.
- `Models/LocalFile.StorageFile` (`:62`), `Game(Game game)` copy ctor (`:126`), `Game.RemoveImage` (`:216`, nombre ambiguo).
- `ProgressNotifier` ctor sin parámetros (`:232`) — inalcanzable por resolución de sobrecarga.
- `ImageTypeViewModel` `SlotIndex = 0` (`:111`) — vestigial, se sobrescribe en restauración de layout.
- `App.xaml.cs:48` `AppInstance.GetCurrent()` con resultado descartado — ¿instancia única a medio implementar? Si no, la app permite múltiples instancias compitiendo por el mismo `.ini`/`.log`.
- `ImageGridViewModel._isLoadingInProgress` (`:52`) — inerte en esta clase (útil solo en la subclase Import).
- **CS0414** (2 warnings): campos asignados y nunca usados — el compilador los señala.

**Scaffolding sin limpiar:** `SettingsControl.xaml.cs` (namespace clásico, comentario de plantilla WinUI, 5 `using` sin usar).

---

## 10. Oportunidades de optimización

Ordenadas por impacto. Las 🔴 escalan con el tamaño real de la colección del usuario (bibliotecas LaunchBox de miles de juegos / cientos de plataformas).

### 10.1 🔴 `Enumeration.GetAll<T>()` — reflexión sin caché en hot paths — `Enumeration.cs:39-54`
Recorre por reflexión los campos estáticos y crea un `new T()` **por cada campo** (el argumento de `GetValue` se ignora en campos estáticos), sin caché. Se invoca:
- Una vez por cada `GameImage` construido (`GameImage.cs:77`, `GetAll<ImageRegion>()`, 28 campos) → decenas de miles de veces por plataforma.
- Una vez por cada `<PlatformFolder>` (`PlatformImageFolder.cs:81`, `GetAll<MediaType>()`, ~60 campos) × cientos de plataformas.
- Indirectamente vía `FromKey`/`FromValue` en persistencia y varios VMs.

**Fix:** cachear `GetAll<T>()` en un diccionario estático por tipo y eliminar el `new T()`. **El hallazgo de rendimiento más impactante del análisis.**

### 10.2 🔴 Matching juego↔imagen O(N×M×S) — `ImageLoadingService.cs:330-417`, `GamesAuditInGalleryViewModel.cs:59-85`, `PlatformLoadingService.cs:352-355`
Para cada fichero y cada juego se hace `game.SearchStrings.IndexOf(...)` (búsqueda lineal sobre `List<string>`). En `ImageLoadingService` se agrava con miles de `Task.Run` redundantes (uno por juego) y el throttle roto ([§4.5](#45--throttle-de-progreso-roto--imageloadingservicecs387)). En `LoadGamesLbDatabaseAsync` (arranque), un doble `Find` lineal por cada fila de la tabla `Games`. **Fix:** construir un índice invertido (`Dictionary<string, List<Game>>`) una vez — técnica que el propio `StatisticsService` ya usa. `GamesAuditInGalleryViewModel.MatchImages` es además **síncrono** (bloquea UI); moverlo a background.

### 10.3 🔴 `switch` de resolución no cubre `High` — `ImageLoadingService.cs:188-197`
Solo cubre `Low` (200px) y `Medium` (350px); no hay `case` para `High`, que es el **default de todas las galerías** (`AppSettings.cs:314,328,344`). Resultado: `DecodePixelWidth = 0` → cada imagen que entra en pantalla al hacer scroll se **decodifica a resolución nativa completa**, anulando el propósito de la caché LRU. **Fix:** añadir el caso `High` con un ancho de decodificación acotado, o replantear el default.

### 10.4 🔴 `OnFirstChanceException` costoso por diseño — `App.xaml.cs:181-203`
Se dispara en **cada** excepción managed del proceso (incluidas las controladas), ejecutando `ex.ToString()` (stack completo) + escritura síncrona a disco bajo `lock`, en el hilo que lanzó. Con `LoggingEnabled = true` por defecto en el arranque, cualquier ruta que use excepciones para control de flujo paga formateo + I/O. **Fix:** loguear en background con buffer, o throttling/dedupe por tipo+stack; considerar desactivarlo salvo diagnóstico.

### 10.5 🔴 `PlatformDetailsViewModel` carga doble al arrancar — `PlatformDetailsViewModel.cs:295-309`
El constructor refresca manualmente **y** llama a `NotifyInitialState()`, que reemite `SelectedPlatformChanged` → `OnSelectedPlatformChanged` repite el trabajo. Con una plataforma ya seleccionada (lo habitual), logo/fanart/imágenes/vídeo se **decodifican dos veces concurrentemente** en el arranque. **Fix:** eliminar uno de los dos caminos.

### 10.6 🟠 Otros
- **`ImageBinariesCacheService.AddImage` (79-101)**: `if (image is not GameImage) return;` → los iconos/logos/fanart de plataforma (que son `ImageAsset`, no `GameImage`) **nunca entran en el presupuesto de memoria** ni se desalojan → RAM no acotada e invisible en `CacheUsage`. Con 100+ plataformas, reserva significativa.
- **Regex recompilados en cada llamada** (`Utilities.cs:140-171,59-83`): usar `static readonly Regex`. Se invoca por fichero en el procesado por lote.
- **`ConsoleViewModel.LogEntries` sin límite**: crece indefinidamente en sesiones largas; `IsOperationInExecution` hace `LogEntries.Any(...)` O(n). Acotar (buffer circular).
- **Converters crean `SolidColorBrush` nuevo por evaluación** (`ImagesCountToColor`, `ImageTypeCountToColor`, `BooleanToColor`, `LogEntrySeverity`): en listas grandes con refresco frecuente, presión de GC. Cachear brushes por clave, invalidar al cambiar tema.
- **`LoadGameHighResImageBinariesAsync` (676-706)**: decodifica secuencialmente; el mismo fichero ya usa `Parallel.For` acotado en `LoadImageDimensionsAsync`.
- **Animaciones por `DispatcherTimer`** (16ms, hilo de UI, polling de `DateTime.Now`): para efectos de alta frecuencia (hover en grids) considerar Composition API (la implementación eficiente existe pero está muerta, [§9](#9-código-muerto--eliminable)).
- **`ChartTypeSelectorControl`**: 12 DPs de datos, cada `set` dispara `Rebuild()` completo → fijar varias seguidas reconstruye N veces. Agregar con dirty-flag + refresco único.
- **`SortCollection`** en `ImageAudit`/`GamesAudit` reordena **toda** la colección (LINQ + `new List`) por cada alta individual y re-filtra aunque solo cambie el orden.
- **`PersistData` síncrono** (`File.WriteAllText`) desde flujos async → bloquea el hilo (posiblemente UI). Usar `WriteAllTextAsync`.

---

## 11. Plan de remediación por fases

Principios: **incremental, verificable, sin romper funcionalidad**. Las fases están ordenadas de **menor a mayor riesgo**. Cada fase es independiente y entregable por separado.

> ### ⚠️ Red de seguridad (previa a cualquier cambio)
> **No hay tests automatizados en el repo.** Antes de empezar, acordar un **checklist de humo manual** que se ejecuta tras cada fase: arranque con LaunchBox configurado, arranque sin configurar (SetLaunchBoxFolders), selección de plataforma/juego, cambio de tipo de imagen, scroll por galerías, import de colección, borrado con undo, descarga de vídeo (YouTube + ffmpeg), reproducción de vídeo, cambio de layout de widgets, drag&drop de widgets, y cierre (que persiste settings). Cada cambio debe pasar este checklist. Considerar introducir un proyecto de tests unitarios al menos para `Services/` y `Models/` (la lógica pura de matching, `Utilities`, `Statistics` es fácilmente testeable y es justo donde están los bugs de correctness).

### Fase 1 — Bugs de correctness (riesgo casi nulo, solo eliminan comportamiento incorrecto)
Corrige [§4](#4-bugs-de-correctness-confirmados). Cambios pequeños y localizados:
1. `.Count` en la rama descendente de "LinkedGames" — `ImageAuditViewModel.cs:751`.
2. `IndexOf(image.File)` (ruta exacta) en `PlatformImageSet.RemoveImage`.
3. `Regex.Replace` (cacheado) en los 3 métodos de `Utilities.cs`.
4. Ordenación numérica de la columna "Dimensions".
5. Constructor `private` en `MediaType`.
6. Revisar y corregir los 4 CS0252 y 4 CS0659.
7. `LaunchBoxPathValidator` (corregir o eliminar el campo).

**Verificación:** ordenar cada columna del Image Audit; borrar variantes `-01`/`-02`; renombrar un juego con paréntesis y comprobar el nombre de fichero resultante.

### Fase 2 — Blindaje de excepciones (alto impacto en estabilidad)
Corrige [§5](#5-manejo-de-excepciones--mapa-de-huecos). Orden sugerido:
1. **`try/finally` en operaciones bloqueantes** ([§5.2](#52--operaciones-bloqueantes-que-pueden-congelar-la-ui-para-siempre)) — garantiza que `FinishBlockingOperation()` siempre corre. **Máxima prioridad** (evita UI congelada).
2. **Guardas de null en arranque** ([§5.3](#53--nullreferenceexception-en-rutas-de-arranque)): `PlatformImageFolder`, `LaunchBoxService`, `ThemeService` (con fallback real).
3. **`async void` → `AsyncRelayCommand` o try/catch** ([§5.4](#54--async-void-sin-protección-en-rutas-transitadas)).
4. **`catch` vacíos → `LogToFile`** ([§5.5](#55--catch-vacíos-que-tragan-errores)); escritura atómica en `PersistData`.
5. **`App_UnhandledException` → `Handle`** ([§5.1](#51-el-diseño-central-es-bueno-pero-está-infrautilizado)).
6. **Definir y documentar la política única** de excepciones (un `docs/Estandares.md`).
7. `MediaFailed` en reproductores; `Uri` con try/catch; `XmlDocument.Load(stream)` para encoding.

**Verificación:** simular fallos (renombrar `Platforms.xml` temporalmente, tema inexistente en settings, carpeta sin plataformas, fichero de imagen bloqueado) y comprobar que la app degrada con mensaje en vez de colgarse/cerrarse.

### Fase 3 — Código muerto (limpieza, bajo riesgo)
Elimina lo **confirmado** de [§9](#9-código-muerto--eliminable) (previa comprobación "Find References" en el IDE). Verifica lo marcado "posible". Decide qué hacer con lo que está muerto pero es útil (`ThemeService.ApplyTheme` + refresco de iconos por tema: ¿cablear un selector de tema en Settings, o eliminar?). Los 3 converters muertos + unificación de pares Normal/Inverted con parámetro `Invert`.

**Verificación:** build limpio (los CS0414 deberían desaparecer) + checklist de humo.

### Fase 4 — Coherencia (naming, namespaces, patrones)
Corrige [§6](#6-coherencia-entre-servicios) y parte de [§7](#7-coherencia-entre-controls-mvvm-vs-code-behind):
1. Namespaces: `IStatisticsService`/`StatisticsService`, `WebViewViewModel`, `ToggleGroup`/`ToggleGroupItem`.
2. Renombrar `ImageBinariesCacheServices.cs` → singular; capitalización `LaunchBox`.
3. `Unloaded` simétrico en los 3 controles con fuga ([§7.3](#73--fugas-de-memoria-por-suscripciones-no-liberadas)) + `_themeService = null` en `ChartTypeSelector`.
4. Guarda null en `PlatformListControl.OnLoaded`.
5. `SelectedImageChanged` tipado; centralizar `NotifyInitialState`.
6. **Decisión documentada** sobre política de interfaces y sobre el Service Locator en `UserControl`.
7. Unificar `ConvertBack` en `NotSupportedException`.
8. Extraer `ViewModelConfigGate<T>` (dedup de `EnsureConfigurationLoaded` ×8).

**Verificación:** build + checklist; los CS0108/CS0628 deberían reducirse.

### Fase 5 — Optimización (medible, con cuidado)
Aplica [§10](#10-oportunidades-de-optimización) con medición antes/después donde sea posible:
1. Caché en `Enumeration.GetAll<T>()` (mayor ROI, bajo riesgo).
2. Índices hash en el matching (`ImageLoadingService`, `GamesAuditInGalleryViewModel`, `LoadGamesLbDatabaseAsync`).
3. `case High` en el switch de resolución.
4. Regex estáticos; caché de brushes en converters; `LogEntries` acotado.
5. Doble carga de `PlatformDetailsViewModel`.
6. `FirstChanceException` en background/throttled.
7. `ImageBinariesCache` contabilizando también assets de plataforma (o documentar por qué no).

**Verificación:** cronometrar arranque y carga de una plataforma grande antes/después; comprobar uso de memoria en scroll de galerías.

### Fase 5b — Reducción de nulabilidad (progresivo)
Atacar los ~1.900 warnings CS86xx **fichero a fichero**, empezando por `Services/` y `Models/`. No es un cambio masivo de una vez: cada fichero saneado (anotaciones `?`, guardas, `required`) convierte avisos ignorados en garantías. Muchos NRE de la Fase 2 quedan cerrados de raíz aquí. Objetivo intermedio: `<WarningLevel>` por carpeta o `#nullable` disciplinado.

### Fase 6 — Refactors estructurales (mayor esfuerzo/riesgo — al final, incremental)
Trocear las God classes de [§8](#8-calidad-de-código-y-complejidad-god-classes), **una a la vez**, con el checklist de humo tras cada extracción:
1. `CoverageChartBuilder` + `ChartWidgetViewModelBase` (elimina la mayor duplicación, riesgo medio).
2. Extraer algoritmos puros de `ChartTypeSelectorControl` a `ChartDataProcessor` testable.
3. Dividir `ImageLoadingService` en servicios por responsabilidad.
4. Extraer controladores de `WidgetPanelControl` (`WidgetDragController`, `RowSplitterController`) + mover `LayoutSizes` al VM.
5. Partir `AppSettings` por sección + `BindProperties` por reflexión.
6. Extraer `ImageHeaderReader` de `FileSystemService`; segmentar `StatisticsService`/`IStatisticsService`.

Estos refactors se benefician enormemente de tener primero la Fase 5b (nulabilidad) y, idealmente, tests de los `Services/` afectados.

---

### Resumen del plan

| Fase | Contenido | Riesgo | Esfuerzo | Prioridad |
|------|-----------|--------|----------|-----------|
| 1 | Bugs de correctness | Muy bajo | Bajo | 🔴 Alta |
| 2 | Blindaje de excepciones | Bajo | Medio | 🔴 Alta |
| 3 | Código muerto | Bajo | Bajo | 🟠 Media |
| 4 | Coherencia (naming/patrones) | Bajo | Medio | 🟠 Media |
| 5 | Optimización | Medio | Medio | 🟠 Media |
| 5b | Reducción de nulabilidad | Bajo (por fichero) | Alto (progresivo) | 🟠 Media |
| 6 | Refactors estructurales | Medio-Alto | Alto | 🟡 Baja (continua) |

**Recomendación de arranque:** Fases 1 y 2 dan la mejor relación valor/riesgo (corrigen bugs reales y estabilizan la app sin cambiar comportamiento deseado). El resto puede intercalarse según capacidad, dejando los refactors de la Fase 6 como trabajo de fondo continuo.
