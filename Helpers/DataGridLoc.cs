using System;
using CommunityToolkit.WinUI.UI.Controls;
using MM4LB.Services;

namespace MM4LB.Helpers;

/// <summary>
/// Localiza (i18n) las cabeceras de columna de un <see cref="DataGrid"/> del CommunityToolkit. Las columnas NO son
/// <c>FrameworkElement</c> (no están en el árbol visual), así que no aceptan <c>{loc:Str}</c>; se fijan por código.
/// Mapea el <c>Tag</c> de cada columna a una clave de recurso y re-aplica al cambiar de idioma (en caliente).
///
/// Uso (en el constructor del control, tras InitializeComponent):
/// <code>DataGridLoc.Attach(dgOrphans, ("Type", LocKeys.Common_Type_Header), ("Name", LocKeys.Common_File_Header));</code>
/// La escucha del cambio de idioma se ata a Loaded/Unloaded del grid para no fugar.
/// </summary>
public static class DataGridLoc
{
    public static void Attach(DataGrid grid, params (string Tag, string Key)[] map)
    {
        void Apply()
        {
            LocalizationService? loc = LocalizationService.Instance;
            if (loc is null)
                return;

            foreach (DataGridColumn column in grid.Columns)
            {
                if (column.Tag is not string tag)
                    continue;

                foreach ((string t, string k) in map)
                {
                    if (t == tag)
                    {
                        column.Header = loc[k];
                        break;
                    }
                }
            }
        }

        void OnLanguageChanged(object? sender, EventArgs e) => Apply();

        grid.Loaded += (_, _) =>
        {
            if (LocalizationService.Instance is LocalizationService loc)
            {
                loc.LanguageChanged -= OnLanguageChanged;
                loc.LanguageChanged += OnLanguageChanged;
            }
            Apply();
        };
        grid.Unloaded += (_, _) =>
        {
            if (LocalizationService.Instance is LocalizationService loc)
                loc.LanguageChanged -= OnLanguageChanged;
        };

        Apply();
    }
}
