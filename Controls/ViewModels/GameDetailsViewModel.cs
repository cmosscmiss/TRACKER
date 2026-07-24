using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using MM4LB.Services;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// ViewModel del widget <see cref="Views.GameDetailsControl"/>. Expone la ficha del juego seleccionado
/// (<see cref="GameDetails.Groups"/>) para pintarla agrupada, combinando dos fuentes: los grupos del XML de
/// colección (ya en <see cref="Game.Details"/>, se muestran de inmediato) y los de la base de datos de metadatos
/// de LaunchBox (se leen bajo demanda con <see cref="GameMetadataService"/> y se añaden al llegar). Reacciona a
/// los cambios de <see cref="SharedDataService.SelectedGame"/>. No persiste estado propio.
/// </summary>
public class GameDetailsViewModel : WidgetViewModelBase
{
    private static readonly IReadOnlyList<GameDetails.Group> EmptyGroups = Array.Empty<GameDetails.Group>();

    private readonly GameMetadataService _gameMetadataService;

    /// <summary>
    /// Generación de la selección actual: se incrementa en cada cambio de juego para descartar los resultados de
    /// la lectura asíncrona de la base de datos que lleguen tarde (el juego ya cambió mientras se consultaba).
    /// </summary>
    private int _selectionGeneration;

    private IReadOnlyList<GameDetails.Group> _groups = EmptyGroups;
    private bool _isInLaunchBoxDb;

    /// <summary>Grupos etiqueta/valor de la ficha del juego seleccionado (vacío si no hay juego).</summary>
    public IReadOnlyList<GameDetails.Group> Groups
    {
        get => _groups;
        private set
        {
            if (SetProperty(ref _groups, value))
                OnPropertyChanged(nameof(HasDetails));
        }
    }

    /// <summary>True cuando hay ficha que mostrar (dirige el estado vacío del control).</summary>
    public bool HasDetails => _groups.Count > 0;

    /// <summary>
    /// True si el juego seleccionado existe en la base de datos de metadatos de LaunchBox. Dirige el flag visual
    /// del control y decide si se muestran los grupos de la BBDD o el fallback de la caché de colección.
    /// </summary>
    public bool IsInLaunchBoxDb
    {
        get => _isInLaunchBoxDb;
        private set => SetProperty(ref _isInLaunchBoxDb, value);
    }

    private int _knownImagesTotal;
    private int _knownImageTypeCount;

    /// <summary>Nº total de imágenes conocidas en la BBDD para el juego (pill inferior).</summary>
    public int KnownImagesTotal
    {
        get => _knownImagesTotal;
        private set
        {
            if (SetProperty(ref _knownImagesTotal, value))
                OnPropertyChanged(nameof(HasKnownImages));
        }
    }

    /// <summary>Nº de tipos de media distintos con imágenes conocidas en la BBDD (pill inferior).</summary>
    public int KnownImageTypeCount
    {
        get => _knownImageTypeCount;
        private set => SetProperty(ref _knownImageTypeCount, value);
    }

    /// <summary>True si hay imágenes conocidas en la BBDD (dirige la visibilidad de las pills inferiores).</summary>
    public bool HasKnownImages => _knownImagesTotal > 0;

    #region Constructor
    public GameDetailsViewModel(SharedDataService sharedDataService, GameMetadataService gameMetadataService, IOptions<AppSettings> appSettings)
        : base(sharedDataService, appSettings)
    {
        _gameMetadataService = gameMetadataService;
        SharedDataService.PropertyChanged += SharedDataService_PropertyChanged;

        // Un juego puede estar ya seleccionado al crear el widget (arranque): poblar la ficha de inicio.
        UpdateGroups();
    }
    #endregion

    #region Subscribed events
    private void SharedDataService_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SharedDataService.SelectedGame))
            UpdateGroups();
    }
    #endregion

    /// <summary>
    /// Publica de inmediato los grupos del XML de colección del juego seleccionado y, en paralelo, lee la base de
    /// datos de metadatos para añadir sus grupos cuando estén listos (descartando el resultado si la selección
    /// cambió entretanto).
    /// </summary>
    private async void UpdateGroups()
    {
        int generation = ++_selectionGeneration;
        Game? game = SharedDataService.SelectedGame;

        if (game?.Details == null)
        {
            Groups = EmptyGroups;
            IsInLaunchBoxDb = false;
            SetKnownImages(0, 0);
            return;
        }

        // Grupos base (colección, sin MISSING MEDIA oculta) de inmediato; flag sembrado con el valor de la carga.
        Groups = VisibleGroups(game.Details.Groups);
        IsInLaunchBoxDb = game.InLaunchboxDb;
        SetKnownImages(0, 0);

        // Sin ID de BBDD: no está en la BBDD → mostrar el fallback de metadatos de colección de inmediato.
        if (string.IsNullOrEmpty(game.DatabaseId) || game.DatabaseId == "0")
        {
            IsInLaunchBoxDb = false;
            Groups = Combine(game.Details.Groups, game.Details.FallbackGroups);
            return;
        }

        (bool found, IReadOnlyList<GameDetails.Group> databaseGroups, int knownTotal, int knownTypes)
            = await _gameMetadataService.GetMetadataAsync(game.DatabaseId);

        // La selección cambió mientras se leía la base de datos: descartar este resultado.
        if (generation != _selectionGeneration)
            return;

        IsInLaunchBoxDb = found;
        SetKnownImages(knownTotal, knownTypes);
        Groups = found
            ? MergeIdentityGroups(game.Details.Groups, databaseGroups)
            : Combine(game.Details.Groups, game.Details.FallbackGroups);
    }

    private void SetKnownImages(int total, int types)
    {
        KnownImagesTotal = total;
        KnownImageTypeCount = types;
    }

    /// <summary>Grupos de colección visibles (oculta MISSING MEDIA); EmptyGroups si no queda ninguno.</summary>
    private static IReadOnlyList<GameDetails.Group> VisibleGroups(IReadOnlyList<GameDetails.Group> groups)
    {
        var visible = groups.Where(g => g.Header != GameDetails.MissingMediaHeader).ToList();
        return visible.Count > 0 ? visible : EmptyGroups;
    }

    /// <summary>Concatena los grupos base (sin MISSING MEDIA) con los añadidos (fallback), sin mutar los originales.</summary>
    private static IReadOnlyList<GameDetails.Group> Combine(
        IReadOnlyList<GameDetails.Group> baseGroups, IReadOnlyList<GameDetails.Group> extraGroups)
    {
        var combined = baseGroups.Where(g => g.Header != GameDetails.MissingMediaHeader).ToList();
        combined.AddRange(extraGroups);
        return combined.Count > 0 ? combined : EmptyGroups;
    }

    /// <summary>
    /// Combina los grupos de colección con los de la BBDD. Construye un único grupo <c>IDENTITY</c> con los campos
    /// de <see cref="GameDetails.IdentityOrder"/>, tomando cada valor de su fuente (identidad de colección, catálogo
    /// o nombre alternativo de la BBDD) y en ese orden exacto. El resto de grupos se mantienen: primero los de
    /// colección (salvo la identidad), luego los de la BBDD (salvo catálogo y nombres alternativos, ya fusionados).
    /// No muta los grupos originales (crea filas nuevas, preservando el flag de rating).
    /// </summary>
    private static IReadOnlyList<GameDetails.Group> MergeIdentityGroups(
        IReadOnlyList<GameDetails.Group> collectionGroups, IReadOnlyList<GameDetails.Group> databaseGroups)
    {
        GameDetails.Group? identity = collectionGroups.FirstOrDefault(g => g.Header == GameDetails.IdentityHeader);
        GameDetails.Group? catalog = databaseGroups.FirstOrDefault(g => g.Header == GameDetails.DatabaseCatalogHeader);
        GameDetails.Group? altNames = databaseGroups.FirstOrDefault(g => g.Header == GameDetails.DatabaseAlternateNamesHeader);

        // Índice de todos los campos disponibles por etiqueta (las etiquetas son únicas entre las tres fuentes).
        var byLabel = new Dictionary<string, GameDetails.Field>();
        foreach (GameDetails.Group? source in new[] { identity, catalog, altNames })
            if (source != null)
                foreach (GameDetails.Field f in source.Fields)
                    byLabel[f.Label] = f;

        // Emite los campos de IdentityOrder que existan y no estén vacíos, en ese orden.
        var identityFields = new List<GameDetails.Field>();
        foreach (string label in GameDetails.IdentityOrder)
            if (byLabel.TryGetValue(label, out GameDetails.Field? f) && !string.IsNullOrEmpty(f.Value))
                identityFields.Add(new GameDetails.Field(f.Label, f.Value) { Rating = f.Rating });

        var result = new List<GameDetails.Group>();
        if (identityFields.Count > 0)
        {
            for (int i = 0; i < identityFields.Count; i++)
                identityFields[i].ShowSeparator = i < identityFields.Count - 1;
            result.Add(new GameDetails.Group(GameDetails.IdentityHeader, identityFields));
        }

        result.AddRange(collectionGroups.Where(g => !ReferenceEquals(g, identity) && g.Header != GameDetails.MissingMediaHeader));
        result.AddRange(databaseGroups.Where(g => !ReferenceEquals(g, catalog) && !ReferenceEquals(g, altNames)));
        return result.Count > 0 ? result : EmptyGroups;
    }

    #region Config
    public override void LoadConfig() { }

    public override void SaveConfig() { }

    public override void Dispose()
        => SharedDataService.PropertyChanged -= SharedDataService_PropertyChanged;
    #endregion
}
