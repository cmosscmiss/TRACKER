using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MM4LB.Controls.Dialogs;

/// <summary>
/// Contenido del diálogo de grabar template: un componente de 3 slots (imagen + nombre del template guardado, o
/// placeholder si está vacío) para elegir dónde grabar, más el nombre. El botón primario (OK) se habilita en cuanto
/// hay un SLOT seleccionado (el nombre es opcional; si se deja vacío se usa uno por defecto). Un slot ocupado se
/// SOBREESCRIBE al grabar.
/// </summary>
public sealed partial class TemplateNameDialog : Page, IAppDialogPrimaryGate
{
    public TemplateNameDialog()
    {
        InitializeComponent();
        Loaded += (_, _) => NameBox.Focus(FocusState.Programmatic);
    }

    /// <summary>Slot elegido (1..N), o -1 si ninguno.</summary>
    public int SelectedSlot => Slots.SelectedSlot;

    /// <summary>Nombre introducido (recortado).</summary>
    public string TemplateName => NameBox.Text?.Trim() ?? string.Empty;

    /// <inheritdoc/>
    public bool IsPrimaryEnabled => Slots.SelectedSlot >= 1;   // basta con elegir slot; el nombre es opcional

    /// <inheritdoc/>
    public event EventHandler? PrimaryEnabledChanged;

    private void OnTextChanged(object sender, TextChangedEventArgs e) => PrimaryEnabledChanged?.Invoke(this, EventArgs.Empty);

    private void OnSlotSelectionChanged(object? sender, EventArgs e)
    {
        // Al elegir un slot ocupado, si aún no se ha escrito nombre, se prellenar con el del template existente
        // (facilita el sobreescribir conservando el nombre).
        if (string.IsNullOrWhiteSpace(NameBox.Text) && !string.IsNullOrEmpty(Slots.SelectedSlotName))
            NameBox.Text = Slots.SelectedSlotName;

        PrimaryEnabledChanged?.Invoke(this, EventArgs.Empty);
    }
}
