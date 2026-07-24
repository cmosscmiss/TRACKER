# Auditoría — Campos obtenibles de un juego en LaunchBox

Inventario de todos los campos que se pueden leer de un juego a partir de los
datos de LaunchBox, verificado contra una instalación real
(`D:\Users\blazqvi\LaunchBox`).

Fecha: 2026-07-11.

## Las dos fuentes

Un juego tiene datos en **dos** sitios distintos (no confundirlos):

| Fuente | Archivo | Qué es | Escala |
|---|---|---|---|
| **Colección del usuario** | `Data\Platforms\{Plataforma}.xml` → nodo `<Game>` | Los juegos concretos del usuario (rutas locales, estadísticas de juego, ajustes por juego). 100 campos por juego | los juegos del usuario |
| **BBDD de metadatos** | `Metadata\LaunchBox.Metadata.db` (SQLite) | Catálogo de referencia de *todos* los juegos que existen | 182.834 juegos, 1.29 M imágenes |

La clave que une ambas es **`DatabaseID`** (el `<DatabaseID>` del XML = `Games.DatabaseID` de la BBDD).

## Lo que la app lee HOY

Extracto mínimo (ver `Models/Platform.cs` → `SetGames`, y
`Services/PlatformLoadingService.cs` → `LoadGamesLbDatabaseAsync`):

- **Del XML de colección:** `DatabaseID`, `ApplicationPath` (→ `Rom`), `Title`, `Version`.
- **De la BBDD de metadatos:** `DatabaseID`, `Name`, `Platform` (tabla `Games`).

Es decir, se usan **4 de 100** campos del XML y **3 de 26** columnas de la BBDD.

---

## Fuente 1 — XML de colección: los 100 campos, agrupados

### Identidad / catálogo
`Title` · `SortTitle` · `Series` · `Region` · `Version` · `DatabaseID` · `ID` (GUID interno LB) · `CloneOf` · `Status` · `Source` · `Portable`

### Metadatos descriptivos (cacheados del catálogo)
`Developer` · `Publisher` · `ReleaseDate` · `ReleaseType` · `Genre` · `MaxPlayers` · `PlayMode` · `Rating` (ESRB) · `Notes` (sinopsis) · `WikipediaURL` · `VideoUrl`

### Valoraciones
`StarRating` / `StarRatingFloat` (nota del **usuario**) · `CommunityStarRating` · `CommunityStarRatingTotalVotes`

### Estado de juego / uso (lo más jugoso para estadísticas)
`Favorite` · `Completed` · `Hide` · `Broken` · `PlayCount` · `PlayTime` · `Progress` · `DateAdded` · `DateModified` · `RetroAchievementsBeatenHardcore` · `RetroAchievementsBeatenSoftcore` · `HasCloudSynced`

### Rutas de archivo / lanzamiento
`ApplicationPath` · `RootFolder` · `CommandLine` · `ConfigurationPath` · `ConfigurationCommandLine` · `ManualPath` · `MusicPath` · `VideoPath` · `ThemeVideoPath` · `Emulator` (GUID)

### Flags "falta media" (12)
LaunchBox ya indica qué media falta sin escanear disco:
`MissingVideo` · `MissingBoxFrontImage` · `MissingScreenshotImage` · `MissingMarqueeImage` · `MissingClearLogoImage` · `MissingBackgroundImage` · `MissingBox3dImage` · `MissingCartImage` · `MissingCart3dImage` · `MissingBannerImage` · `MissingManual` · `MissingMusic`

### IDs de tiendas externas
`GogAppId` · `OriginAppId` · `OriginInstallPath`

### DOSBox / ScummVM (8)
`UseDosBox` · `DosBoxConfigurationPath` · `CustomDosBoxVersionPath` · `UseScummVM` · `ScummVMAspectCorrection` · `ScummVMFullscreen` · `ScummVMGameDataFolderPath` · `ScummVMGameType`

### Pantallas startup/pause + AutoHotkey (18)
Config avanzada de emulación, poco interés para gestión de media:
`UseStartupScreen` · `StartupLoadDelay` · `HideAllNonExclusiveFullscreenWindows` · `HideMouseCursorInGame` · `DisableShutdownScreen` · `AggressiveWindowHiding` · `OverrideDefaultStartupScreenSettings` · `UsePauseScreen` · `OverrideDefaultPauseScreenSettings` · `SuspendProcessOnPause` · `ForcefulPauseScreenActivation` · `PauseAutoHotkeyScript` · `ResumeAutoHotkeyScript` · `LoadStateAutoHotkeyScript` · `SaveStateAutoHotkeyScript` · `ResetAutoHotkeyScript` · `SwapDiscsAutoHotkeyScript`

### Rutas de imágenes Android (12)
Caché de LaunchBox para Android (`Android*Path` / `*ThumbPath`), normalmente
irrelevante en escritorio:
`AndroidBackgroundPath` · `AndroidBackgroundThumbPath` · `AndroidBoxFrontFullPath` · `AndroidBoxFrontThumbPath` · `AndroidClearLogoFullPath` · `AndroidClearLogoThumbPath` · `AndroidGameTitleScreenshotPath` · `AndroidGameTitleScreenshotThumbPath` · `AndroidGameplayScreenshotPath` · `AndroidGameplayScreenshotThumbPath` · `AndroidVideoPath`

---

## Fuente 2 — BBDD de metadatos SQLite

### Tabla `Games` (26 columnas) — catálogo canónico por `DatabaseID`
`DatabaseID` · `Name` · `CompareName` · `ReleaseDate` · `ReleaseYear` · `Overview` · `MaxPlayers` · `ReleaseType` · `Cooperative` · `VideoURL` · `CommunityRating` · `CommunityRatingCount` · `SteamAppId` · `WikipediaURL` · `Platform` · `ESRB` · `Genres` · `Developer` · `Publisher` · `DOS` · `StartupFile` · `StartupMD5` · `StartupParameters` · `SetupFile` · `SetupMD5` · `SetupParameters`

### Tabla `GameImages` (1.29 M filas) — imágenes conocidas de cada juego
`DatabaseId` · `FileName` · `Type` (BoxFront, ClearLogo, Screenshot…) · `Region` · `CRC32`

### Tabla `GameAlternateTitles` (67.691 filas) — títulos alternativos / regionales
`DatabaseID` · `AlternateName` · `Region` · `AltNameCompareValue`

### Otras tablas de la BBDD (no específicas de juego)
`Platforms` · `PlatformAlternateNames` · `Emulators` · `EmulatorPlatforms` · `__EFMigrationsHistory` · `__EFMigrationsLock` · `sqlite_sequence`

---

## Observaciones útiles para la app

1. **Los 12 flags `Missing*`** del XML son oro para la auditoría de media:
   LaunchBox ya sabe qué falta, sin tener que emparejar ficheros a mano.
2. **`PlayCount`, `PlayTime`, `Completed`, `Favorite`, `DateAdded`** darían
   estadísticas de uso muy ricas para los widgets de Stats (hoy no se leen).
3. **`GameImages`** de la BBDD permite saber qué imágenes *deberían* existir
   para un juego (por tipo y región) y contrastarlas con lo que hay en disco;
   encaja con la herramienta de auditoría de huérfanos/faltantes.
4. **`Genre`, `Developer`, `Publisher`, `ReleaseDate`** ya están en el propio
   XML de colección: no hace falta abrir la BBDD para tenerlos.

## Cómo se obtuvo este inventario

- XML de colección: enumerando los tags hijos del primer `<Game>` de
  `Data\Platforms\3DO Interactive Multiplayer.xml`.
- BBDD de metadatos: `PRAGMA table_info(...)` sobre `LaunchBox.Metadata.db`
  abierta en solo-lectura con el mismo driver `Microsoft.Data.Sqlite` 9.0.0
  que usa la app.
