using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Contenido del diálogo "Settings" de los dashboards: ver/editar los criterios de PRESELECCIÓN (qué media es la
/// imagen principal) y de PROCESO (cómo se renombra/mueve el conservado). Trabaja sobre COPIAS de los criterios,
/// de modo que cancelar no altera nada; al confirmar, el dashboard toma <see cref="GetSelectionCriteria"/> /
/// <see cref="GetProcessingCriteria"/>.
/// </summary>
public sealed partial class DashboardSettingsDialog : Page
{
    private readonly ObservableCollection<SettingsCriterionRow> _selectionRows = new();
    private readonly ObservableCollection<SettingsCriterionRow> _processingRows = new();

    // Criterios de proceso NO mostrados (p. ej. "Region" en el dashboard de regiones, donde keep-region es fijo):
    // se conservan tal cual y se devuelven en GetProcessingCriteria, para no perderlos al guardar.
    private readonly List<GameImageCriterion> _hiddenProcessingCriteria = new();

    #region Constructors
    /// <param name="hideRegionCriterion">Oculta la fila del criterio "Region" (para el dashboard de regiones,
    /// donde la región se conserva siempre); el criterio se preserva sin cambios.</param>
    public DashboardSettingsDialog(IEnumerable<GameImageCriterion> selection, IEnumerable<GameImageCriterion> processing, bool hideRegionCriterion = false)
    {
        InitializeComponent();

        foreach (GameImageCriterion criterion in selection)
        {
            GameImageCriterion clone = Clone(criterion);
            _selectionRows.Add(new SettingsCriterionRow(clone, OptionsFor(clone.Type)));
        }

        foreach (GameImageCriterion criterion in processing)
        {
            GameImageCriterion clone = Clone(criterion);

            if (hideRegionCriterion && clone.Type == SettingsType.Region)
            {
                _hiddenProcessingCriteria.Add(clone);
                continue;
            }

            _processingRows.Add(new SettingsCriterionRow(clone, OptionsFor(clone.Type)));
        }

        icSelection.ItemsSource = _selectionRows;
        icProcessing.ItemsSource = _processingRows;
    }
    #endregion

    #region Methods (public)
    /// <summary>Criterios de preselección editados.</summary>
    public List<GameImageCriterion> GetSelectionCriteria() => _selectionRows.Select(r => r.Criterion).ToList();

    /// <summary>Criterios de proceso editados (los mostrados) más los preservados sin mostrar (p. ej. Region).</summary>
    public List<GameImageCriterion> GetProcessingCriteria() =>
        _processingRows.Select(r => r.Criterion).Concat(_hiddenProcessingCriteria).ToList();
    #endregion

    #region Subscribed events
    /// <summary>
    /// Fija el valor mostrado del ComboBox cuando ya tiene su ItemsSource. Se hace aquí (y no por x:Bind
    /// SelectedItem) porque, dentro de un DataTemplate, el x:Bind de SelectedItem se aplica antes que el
    /// ItemsSource y el combo se queda VACÍO aunque el valor esté seleccionado. La escritura de vuelta la hace
    /// <see cref="CriterionCombo_SelectionChanged"/>.
    /// </summary>
    private void CriterionCombo_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ComboBox combo && combo.DataContext is SettingsCriterionRow row)
        {
            combo.SelectedItem = row.SelectedOption;
        }
    }

    /// <summary>Vuelca al criterio el valor elegido en el ComboBox.</summary>
    private void CriterionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox combo && combo.DataContext is SettingsCriterionRow row && combo.SelectedItem is Enumeration option)
        {
            row.SelectedOption = option;
        }
    }

    /// <summary>Abre el TeachingTip de ayuda de la preselección.</summary>
    private void PreselectionHelp_Click(object sender, RoutedEventArgs e) => PreselectionHelpTip.IsOpen = true;

    /// <summary>Abre el TeachingTip de ayuda del proceso.</summary>
    private void ProcessingHelp_Click(object sender, RoutedEventArgs e) => ProcessingHelpTip.IsOpen = true;
    #endregion

    #region Methods (private)
    private static GameImageCriterion Clone(GameImageCriterion c) => new()
    {
        ID = c.ID,
        IsActive = c.IsActive,
        Type = c.Type,
        CriteriaName = c.CriteriaName,
    };

    /// <summary>Catálogo de valores posibles para el <see cref="SettingsType"/> del criterio.</summary>
    private static IReadOnlyList<Enumeration> OptionsFor(SettingsType type)
    {
        if (type == SettingsType.Image) { return Enumeration.GetAll<ImageSettings>().Cast<Enumeration>().ToList(); }
        if (type == SettingsType.Region) { return Enumeration.GetAll<RegionSettings>().Cast<Enumeration>().ToList(); }
        if (type == SettingsType.FileNameSuffix) { return Enumeration.GetAll<FileNameSuffixSettings>().Cast<Enumeration>().ToList(); }
        if (type == SettingsType.FileName) { return Enumeration.GetAll<FileNameSettings>().Cast<Enumeration>().ToList(); }
        return new List<Enumeration>();
    }
    #endregion
}
