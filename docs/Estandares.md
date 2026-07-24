# Estándares de manejo de excepciones — MM4LB

> Política única de manejo de errores de la aplicación. Recoge las convenciones aplicadas en la Fase 2 del plan de remediación (`Evaluacion-Codigo.md` §5). **Todo código nuevo debe seguirla.**

## Principio general

**Ningún error debe ser silencioso.** Todo fallo se registra (log); los que el usuario debe conocer, además se le muestran con el diálogo propio de la app.

## Servicio central: `ExceptionService`

| Método | Tipo | Qué hace | Cuándo usarlo |
|--------|------|----------|---------------|
| `ExceptionService.LogToFile(ex, contexto)` | estático | Solo log a `MM4LB.log`. **Nunca lanza.** | Fondo, cierre, y cualquier sitio sin acceso a DI. |
| `_exceptionService.Handle(ex, mensajeUsuario)` | instancia (DI) | Loguea **y** dispara el diálogo de la app (vía `ExceptionDialogService`). Ignora `OperationCanceledException`. | Acciones que dispara el usuario. |

### Cuándo `Handle` (diálogo) vs `LogToFile` (solo log)

- **`Handle` (diálogo):** comandos y acciones directas del usuario (borrar, importar, drop, descargar). El usuario espera feedback de lo que acaba de hacer.
- **`LogToFile` (solo log):** operaciones de fondo/automáticas (refrescos de estadísticas, carga lazy de imágenes) y el **cierre** de la app (`PersistData`, `Host.StopAsync`), donde un diálogo no se renderiza o molestaría en cada repetición.

Si un ViewModel necesita `Handle` y no tiene `ExceptionService`, se inyecta por constructor (el DI lo resuelve solo; no hay que tocar `App.xaml.cs`).

## Patrones obligatorios

- **`async void`:** nunca dejar que una excepción escape (tumbaría el proceso). Patrón **extract-to-Task**: el `async void` es un wrapper fino y la lógica vive en un `async Task`:
  ```csharp
  private async void OnX(...) 
  {
      try { await OnXCoreAsync(...); }
      catch (Exception ex) { _exceptionService.Handle(ex, "..."); }
  }
  private async Task OnXCoreAsync(...) { /* lógica */ }
  ```
- **Operaciones bloqueantes** (`StartBlockingOperation`): envolver en `try/finally` que garantice `FinishBlockingOperation()`. Si no, una excepción deja la UI congelada (`IsUIEnabled = false`) para siempre.
- **Diálogos:** SIEMPRE el diálogo propio de la app (`DialogsService.AlertAsync` / `ExceptionDialogService`). **NUNCA** un `MessageBox` nativo de Windows.
- **`catch`:** prohibido `catch { }` vacío. Como mínimo `LogToFile`. Capturar el tipo concreto cuando se conozca.
- **Escritura de ficheros críticos** (configuración): **atómica** — escribir a un temporal y `File.Replace`/`Move`, para no dejar el fichero truncado si el proceso muere a mitad.
- **Arranque:** no debe crashear por datos ausentes o corruptos (`Platforms.xml`, tema inexistente, sin plataformas). Degradar (valor por defecto) cuando sea un caso menor, o **mostrar un mensaje y cerrar limpiamente** (`Environment.Exit`) cuando sea un fallo duro. `Window.Close()` sobre el splash hace fail-fast (`0xc000027b`): usar `Environment.Exit`.

## Sumideros globales (`App.xaml.cs`)

Cuatro handlers capturan lo que se escape de todo lo anterior, todos a `MM4LB.log`:

- `App_UnhandledException` — excepciones no controladas del hilo de UI.
- `OnDomainUnhandledException` — cualquier hilo (incluidos los de fondo).
- `OnUnobservedTaskException` — `Task`s cuyo error nunca se observó (marca observado).
- `OnFirstChanceException` — **toda** excepción managed al lanzarse (incluidas las controladas); sirve para diagnosticar los crashes nativos silenciosos de WinUI (`STATUS_STOWED_EXCEPTION` / `0xc000027b`) que no pasan por los otros handlers.

**Decisión de producto (2026-07):** los sumideros **solo registran** (`LogToFile`); **no** muestran diálogo ni recuperan (`e.Handled = false`). Un crash sin mensaje se diagnostica desde el log. Se prefiere que la app se cierre a que siga en un estado indefinido. (Motivo: los crashes observados han sido excepcionales y propios del desarrollo; el log es suficiente para resolverlos.)

## Ubicación del log

`%LocalAppData%\MM4LB\MM4LB.log` — misma carpeta que la configuración (`MM4LB.ini`) y el backup de imágenes. Activable/desactivable con `AppSettings.General.ExceptionLoggingEnabled` (activo por defecto para capturar también fallos previos a la carga de settings).
