# Built-in templates

Templates de **aplicación** (read-only): aparecen en el selector de templates de la toolbar junto a los del usuario,
pero no se pueden editar ni borrar (llevan un candado). El usuario puede cargarlos, no sobreescribirlos.

## Formato

Por cada template, dos ficheros en esta carpeta (`Assets/Templates`):

- `NombreDelTemplate.json` — snapshot de la configuración (`AppSettings`), el mismo formato que un template de usuario.
- `NombreDelTemplate.jpg` — miniatura (opcional). Si no existe, se muestra un placeholder.

El **nombre visible** en el selector es el nombre del fichero (sin extensión). El orden es alfabético.

## Cómo crear uno

1. Configura la app (layout, widgets, toggles…) como quieras el template.
2. Grábalo como template de usuario (botón de guardar template) en un slot.
3. Copia desde `%LocalAppData%\MM4LB\Templates` el `slot{n}.json` y el `slot{n}.jpg` a esta carpeta, renombrándolos
   al nombre que quieras (p. ej. `Cobertura.json` / `Cobertura.jpg`).

> Nota: el **tema** no forma parte del template (se excluye al cargar), así que cargar un built-in no cambia el tema.
