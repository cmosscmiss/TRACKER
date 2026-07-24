# Decisiones de arquitectura — MM4LB

> Decisiones tomadas de forma deliberada que a primera vista podrían parecer un descuido. Se documentan aquí para que no se "corrijan" por error. Recoge convenciones de la Fase 4 del plan de remediación (`Evaluacion-Codigo.md` §7).

## Service Locator (`App.GetService<T>()`) en constructores de `UserControl`

**Decisión (2026-07):** los `UserControl` de WinUI resuelven sus servicios con `App.GetService<T>()` (Service Locator) en el constructor, **no** por inyección por constructor. Es intencionado, no un accidente.

### Contexto

WinUI/XAML instancia los `UserControl` desde el markup y **exige un constructor sin parámetros**. Un control no puede recibir sus dependencias por el constructor como sí hace un ViewModel o un servicio (que los resuelve el contenedor de DI). Por eso controles como `WidgetPanelControl`, `WidgetBaseControl`, etc. piden lo que necesitan al contenedor dentro de su constructor.

### Consecuencias y límites

- Es la **única** vía admitida de Service Locator en la app. El resto del código (servicios, ViewModels, host services) usa **inyección por constructor**; no se debe imitar el patrón fuera de los `UserControl`.
- El Service Locator sirve solo para **obtener dependencias que el control no puede recibir por inyección**. **No** es excusa para meter lógica de negocio en el code-behind: las reglas de negocio deben vivir en servicios/ViewModels testables (ver `Evaluacion-Codigo.md` §7.1).
- Un `UserControl` que solo tiene estado visual (p. ej. `WidgetStatCardControl`, `LayoutItemControl`) **no necesita ViewModel ni servicios**: no todo code-behind es un problema.

### Contraste correcto

`WebViewControl` y `PlatformDetailsControl` documentan en su propio code-behind por qué delegan en el ViewModel y dejan en la vista solo lo que WinUI no permite por binding. Ese es el patrón a seguir cuando un control sí tiene un ViewModel asociado.
