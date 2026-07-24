using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Options;
using Microsoft.UI.Xaml;
using MM4LB.Models;
using Windows.Storage;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace MM4LB.ViewModels;

/// <summary>
/// ViewModel encargado de gestionar la selección y validación de las carpetas de LaunchBox.
/// Expone propiedades derivadas, comandos y validaciones asociadas a la ruta seleccionada.
/// </summary>
public partial class SetLaunchBoxFoldersViewModel : ObservableObject
{
    #region Attributes
    private readonly AppSettings _appSettings;
    #endregion

    #region Events
    /// <summary>
    /// Evento que notifica a la vista que debe mostrar la ventana de carga.
    /// </summary>
    public event EventHandler? ShowLoadingRequested;
    #endregion

    #region Properties
    public Window? Window { get; set; }

    public string LaunchBoxFolderPath => _appSettings.LaunchBox.LaunchBoxFolder ?? "<No folder selected>";
    public string LaunchBoxDataFolderPath => string.IsNullOrEmpty(_appSettings.LaunchBox.LaunchBoxDataFolder) ? "<No folder selected>" : _appSettings.LaunchBox.LaunchBoxDataFolder;
    public string LaunchBoxPlatformsFolder => string.IsNullOrEmpty(_appSettings.LaunchBox.LaunchBoxPlatformsFolder) ? "<No folder selected>" : _appSettings.LaunchBox.LaunchBoxPlatformsFolder;
    public string LaunchboxPlatformsXmlFile => string.IsNullOrEmpty(_appSettings.LaunchBox.LaunchboxPlatformsXmlFile) ? "<No file>" : _appSettings.LaunchBox.LaunchboxPlatformsXmlFile;
    public string LaunchboxSettingsXmlFile => string.IsNullOrEmpty(_appSettings.LaunchBox.LaunchboxSettingsXmlFile) ? "<No file>" : _appSettings.LaunchBox.LaunchboxSettingsXmlFile;

    [ObservableProperty] private StorageFolder? launchBoxFolder;
    [ObservableProperty] private bool launchBoxFoldersValid;
    [ObservableProperty] private bool isLaunchBoxFolderPathValid;
    [ObservableProperty] private bool isLaunchBoxDataFolderPathValid;
    [ObservableProperty] private bool isLaunchBoxPlatformsFolderValid;
    [ObservableProperty] private bool isLaunchBoxPlatformsXmlFileValid;
    [ObservableProperty] private bool isLaunchBoxSettingsXmlFileValid;
    #endregion

    #region Commands
    public IRelayCommand CloseCommand { get; }
    public IRelayCommand SaveCommand { get; }
    public IRelayCommand SelectFolderCommand { get; }

    /// <summary>
    /// Lógica ejecutada al cerrar la ventana.
    /// </summary>
    private void OnClose() => Window?.Close();

    /// <summary>
    /// Lógica ejecutada al guardar la configuración de carpetas.
    /// </summary>
    private void OnSave()
    {
        if (LaunchBoxFolder is not null)
        {
            _appSettings.LaunchBox.LaunchBoxFolder = LaunchBoxFolder.Path;
            ShowLoadingRequested?.Invoke(this, EventArgs.Empty);
            Window?.Close();
        }
    }

    /// <summary>
    /// Lógica ejecutada al seleccionar una carpeta mediante el FolderPicker.
    /// </summary>
    private async void OnSelectFolder()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");

        IntPtr hwnd = WindowNative.GetWindowHandle(Window);
        InitializeWithWindow.Initialize(picker, hwnd);

        var folder = await picker.PickSingleFolderAsync();
        if (folder != null)
        {
            LaunchBoxFolder = folder;
        }
    }
    #endregion

    #region Constructor
    /// <summary>
    /// Inicializa el ViewModel con la configuración de la aplicación y crea los comandos asociados.
    /// </summary>
    /// <param name="appSettings">Configuración de la aplicación inyectada mediante IOptions.</param>
    public SetLaunchBoxFoldersViewModel(IOptions<AppSettings> appSettings)
    {
        _appSettings = appSettings?.Value ?? throw new ArgumentNullException(nameof(appSettings));

        CloseCommand = new RelayCommand(OnClose);
        SaveCommand = new RelayCommand(OnSave);
        SelectFolderCommand = new RelayCommand(OnSelectFolder);
    }
    #endregion

    #region Subscribed Events
    /// <summary>
    /// Evento parcial ejecutado cuando cambia la carpeta seleccionada.
    /// Actualiza rutas derivadas y valida la estructura de carpetas de LaunchBox.
    /// </summary>
    /// <param name="value">Nueva carpeta seleccionada.</param>
    partial void OnLaunchBoxFolderChanged(StorageFolder? value)
    {
        if (value is null)
        {
            LaunchBoxFoldersValid = false;
            return;
        }

        _appSettings.LaunchBox.LaunchBoxFolder = value.Path;

        var result = AppSettings.LaunchBoxPathValidator.Validate(_appSettings.LaunchBox.LaunchBoxFolder);

        IsLaunchBoxFolderPathValid = result.IsLaunchBoxFolderPathValid;
        IsLaunchBoxDataFolderPathValid = result.IsLaunchBoxDataFolderPathValid;
        IsLaunchBoxPlatformsFolderValid = result.IsLaunchBoxPlatformsFolderValid;
        IsLaunchBoxPlatformsXmlFileValid = result.IsLaunchBoxPlatformsXmlFileValid;
        IsLaunchBoxSettingsXmlFileValid = result.IsLaunchBoxSettingsXmlFileValid;

        LaunchBoxFoldersValid = result.LaunchBoxFoldersValid;

        OnPropertyChanged(nameof(LaunchBoxFolderPath));
        OnPropertyChanged(nameof(LaunchBoxDataFolderPath));
        OnPropertyChanged(nameof(LaunchBoxPlatformsFolder));
        OnPropertyChanged(nameof(LaunchboxPlatformsXmlFile));
        OnPropertyChanged(nameof(LaunchboxSettingsXmlFile));
    }
    #endregion
}