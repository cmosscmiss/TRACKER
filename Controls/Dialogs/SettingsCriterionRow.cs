using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Fila editable del diálogo de settings del dashboard: envuelve un <see cref="GameImageCriterion"/> (una copia,
/// para poder cancelar) y expone su valor como una <see cref="Enumeration"/> del catálogo de su
/// <see cref="SettingsType"/> (Dimensions/Size/… para selección; Keep/Discard, Suffix/No suffix, nombre… para
/// proceso) más su flag de activo.
/// </summary>
public sealed class SettingsCriterionRow : ObservableObject
{
    private readonly GameImageCriterion _criterion;

    /// <summary>Etiqueta de la fila (el <c>CriteriaName</c> del criterio: "1st:", "Region:", …).</summary>
    public string Label { get; }

    /// <summary>Valores posibles del criterio (catálogo del <see cref="SettingsType"/>).</summary>
    public IReadOnlyList<Enumeration> Options { get; }

    public SettingsCriterionRow(GameImageCriterion criterion, IReadOnlyList<Enumeration> options)
    {
        _criterion = criterion;
        Label = criterion.CriteriaName;
        Options = options;
        _selectedOption = options.FirstOrDefault(o => o.Key == criterion.ID);
    }

    private Enumeration? _selectedOption;
    /// <summary>Valor elegido; al cambiarlo se vuelca su Key en el <see cref="GameImageCriterion.ID"/> del criterio.</summary>
    public Enumeration? SelectedOption
    {
        get => _selectedOption;
        set
        {
            if (SetProperty(ref _selectedOption, value) && value != null)
            {
                _criterion.ID = value.Key;
            }
        }
    }

    /// <summary>Si el criterio se aplica.</summary>
    public bool IsActive
    {
        get => _criterion.IsActive;
        set
        {
            if (value != _criterion.IsActive)
            {
                _criterion.IsActive = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>El criterio editado (ID/IsActive ya aplicados sobre la copia).</summary>
    public GameImageCriterion Criterion => _criterion;
}
