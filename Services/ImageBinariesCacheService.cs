using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Options;
using MM4LB.Models;
using System;
using System.Collections.Generic;

namespace MM4LB.Services;

/// <summary>
/// Service that keeps track of all images for which binaries have been decoded. It bounds the amount of
/// decoded-bitmap memory held in RAM: when the cached binaries exceed <see cref="_cacheSize"/> megabytes,
/// it clears the binary of the oldest loaded image(s) to make room for the newest one.
/// </summary>
public class ImageBinariesCacheService : ObservableObject
{
    #region Attributes
    private double _cachedImagesSize;
    private int _cachedImagesCount;

    private double _cacheUsage;

    private readonly AppSettings _appSettings;
    #endregion


    #region Constructors
    /// <summary>
    /// Creates the cache service. The cache budget is read lazily from the application settings (see
    /// <see cref="CacheSize"/>) instead of being captured here, because this singleton may be constructed
    /// before the persisted settings are restored from disk (which would otherwise freeze the default).
    /// </summary>
    public ImageBinariesCacheService(IOptions<AppSettings> appSettings)
    {
        _appSettings = appSettings.Value;
    }
    #endregion


    #region Properties (private)
    /// <summary>Maximum decoded-bitmap memory to keep cached, in megabytes. Loaded from configuration.</summary>
    private double CacheSize => _appSettings.General.CacheSize;
    #endregion


    #region Properties
    public List<ImageAsset> CachedImages { get; } = new();

    public int CachedImagesCount
    {
        get => _cachedImagesCount;
        set => SetProperty(ref _cachedImagesCount, value);
    }

    public double CachedImagesSize
    {
        get => _cachedImagesSize;
        set
        {
            SetProperty(ref _cachedImagesSize, Math.Round(value, 2));
            CacheUsage = Math.Round(_cachedImagesSize / CacheSize * 100, 2);
        }
    }

    public double CacheUsage
    {
        get => _cacheUsage;
        set => SetProperty(ref _cacheUsage, value);
    }
    #endregion


    #region Methods
    /// <summary>
    /// Adds a newly decoded image to the cache, evicting the oldest binaries while the total decoded
    /// memory exceeds the cache budget. The memory charged is <see cref="ImageAsset.DecodedSizeMb"/>, which
    /// the image computes from the resolution its binary was decoded at: a low-res decode occupies far less
    /// than the native size, so charging native dimensions would over-count every low-res image alike.
    /// </summary>
    public void AddImage(ImageAsset image)
    {
        if (image is not GameImage)
            return;

        // Refund any previous charge so re-adding the same image cannot double count. Safe because the
        // loader always removes an image before re-decoding it, so DecodedSizeMb still holds the value
        // that was charged while it was in the cache.
        RemoveImage(image);

        CachedImages.Add(image);
        CachedImagesSize += image.DecodedSizeMb;

        while (CachedImagesSize > CacheSize && CachedImages.Count > 0)
        {
            ImageAsset oldest = CachedImages[0];
            CachedImages.RemoveAt(0);
            CachedImagesSize -= oldest.DecodedSizeMb;
            oldest.ClearBinary();
        }

        CachedImagesCount = CachedImages.Count;
    }

    /// <summary>
    /// Removes an image from the cache (normally called before the binary is re-decoded), refunding the
    /// memory it was charged. <see cref="ImageAsset.DecodedSizeMb"/> is kept across <c>ClearBinary</c> and
    /// only changes on a new decode, which is always preceded by a removal, so it matches the charge.
    /// </summary>
    public void RemoveImage(ImageAsset image)
    {
        if (CachedImages.Remove(image))
        {
            CachedImagesSize -= image.DecodedSizeMb;
            CachedImagesCount = CachedImages.Count;
        }
    }
    #endregion
}