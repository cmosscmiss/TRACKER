using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MM4LB.Models;

/// <summary>
/// Representa el conjunto completo de filtros aplicables a juegos y a imágenes de juegos.
/// Se utiliza en los controles de auditoría, listas de juegos y auditoría de imágenes.
/// </summary>
public class Filters : ObservableObject
{
    #region Attributes
    public GameFilters Game { get; } = new();
    public ImageFilters Images { get; } = new();
    public ImageTypeFilters ImageTypes { get; } = new();
    #endregion

    #region Subclasses
    /// <summary>
    /// Filtros relacionados con el estado del juego dentro de la colección o base de datos.
    /// </summary>
    public class GameFilters : ObservableObject
    {
        private bool _inCollection;
        private bool _inLaunchboxDb;
        private bool _inCollectionNotInLaunchboxDb;

        public bool InCollection
        {
            get => _inCollection;
            set => SetProperty(ref _inCollection, value);
        }
        public bool InLaunchboxDb
        {
            get => _inLaunchboxDb;
            set => SetProperty(ref _inLaunchboxDb, value);
        }
        public bool InCollectionNotInLaunchboxDb
        {
            get => _inCollectionNotInLaunchboxDb;
            set => SetProperty(ref _inCollectionNotInLaunchboxDb, value);
        }
        public bool HasAny => InCollection || InLaunchboxDb || InCollectionNotInLaunchboxDb;
    }

    /// <summary>
    /// Filtros relacionados con el número de imágenes asociadas a un juego o a una imagen.
    /// </summary>
    public class ImageFilters : ObservableObject
    {
        private bool _missing;
        private bool _oneImage;
        private bool _moreThanOneImage;

        public bool Missing
        {
            get => _missing;
            set => SetProperty(ref _missing, value);
        }

        public bool OneImage
        {
            get => _oneImage;
            set => SetProperty(ref _oneImage, value);
        }

        public bool MoreThanOneImage
        {
            get => _moreThanOneImage;
            set => SetProperty(ref _moreThanOneImage, value);
        }

        public bool HasAny => Missing || OneImage || MoreThanOneImage;
    }

    public class ImageTypeFilters : ObservableObject
    {
        private bool _hasImages = true;
        private bool _missingImages;
        private bool _favourites;

        public bool HasImages
        {
            get => _hasImages;
            set
            {
                if (SetProperty(ref _hasImages, value))
                {
                    if (value && _missingImages)
                    {
                        // Desactivar MissingImages sin disparar callback doble
                        _missingImages = false;
                        OnPropertyChanged(nameof(MissingImages));
                    }
                }
            }
        }

        public bool MissingImages
        {
            get => _missingImages;
            set
            {
                if (SetProperty(ref _missingImages, value))
                {
                    if (value && _hasImages)
                    {
                        // Desactivar HasImages sin disparar callback doble
                        _hasImages = false;
                        OnPropertyChanged(nameof(HasImages));
                    }
                }
            }
        }

        /// <summary>
        /// Restringe la lista a los tipos de imagen favoritos. Dimensión INDEPENDIENTE (no excluyente con
        /// HasImages/MissingImages): se interseca con ellos. Etiqueta en la UI: "Favourites".
        /// </summary>
        public bool Favourites
        {
            get => _favourites;
            set => SetProperty(ref _favourites, value);
        }

        public bool HasAny => HasImages || MissingImages || Favourites;
    }
    #endregion

    #region Methods (public)
    /// <summary>
    /// Aplica los filtros globales relacionados con el estado del juego
    /// (colección, base de datos, etc.) sobre una lista de juegos.
    /// </summary>
    public IEnumerable<Game> ApplyGlobalFilters(IEnumerable<Game> source)
    {
        if (!Game.HasAny)
            return source;

        return source.Where(game =>
            (Game.InCollection && game.InCollection) ||
            (Game.InLaunchboxDb && game.InLaunchboxDb) ||
            (Game.InCollectionNotInLaunchboxDb && !game.InLaunchboxDb && game.InCollection)
        );
    }

    /// <summary>
    /// Aplica los filtros basados en el número de imágenes asociadas a cada juego.
    /// </summary>
    public IEnumerable<Game> ApplyImageFilters(IEnumerable<Game> source)
    {
        if (!Images.HasAny)
            return source;

        return source.Where(game =>
            (Images.Missing && game.Images.Count == 0) ||
            (Images.OneImage && game.Images.Count == 1) ||
            (Images.MoreThanOneImage && game.Images.Count > 1)
        );
    }

    /// <summary>
    /// Aplica los filtros basados en el número de juegos vinculados a cada imagen.
    /// </summary>
    public IEnumerable<GameImage> ApplyImageFilters(IEnumerable<GameImage> source)
    {
        if (!Images.HasAny)
            return source;

        return source.Where(image =>
            (Images.Missing && image.LinkedGames.Count == 0) ||
            (Images.OneImage && image.LinkedGames.Count == 1) ||
            (Images.MoreThanOneImage && image.LinkedGames.Count > 1)
        );
    }

    public IEnumerable<PlatformImageSet> ApplyImageTypeFilters(IEnumerable<PlatformImageSet> source, IReadOnlyCollection<int> favouriteTypeKeys)
    {
        // Dimensión de nº de imágenes (con / sin imágenes, excluyentes entre sí).
        if (ImageTypes.HasImages || ImageTypes.MissingImages)
        {
            source = source.Where(set =>
                (ImageTypes.HasImages && set.ImagesCount > 0) ||
                (ImageTypes.MissingImages && set.ImagesCount == 0)
            );
        }

        // Dimensión de favoritos (independiente): se queda solo con los tipos favoritos configurados.
        if (ImageTypes.Favourites)
        {
            source = source.Where(set =>
                set.Type != null && favouriteTypeKeys != null && favouriteTypeKeys.Contains(set.Type.Key));
        }

        return source;
    }
    #endregion
}