using System;
using System.Collections.Generic;
using System.Linq;
using MM4LB.Enums;
using MM4LB.Models;

namespace MM4LB.Controls.ViewModels;

/// <summary>
/// Preselección del medio principal de un conjunto de imágenes según criterios en cascada (dimensiones, tamaño o
/// extensión). Extraído para reutilizarlo tanto en el <see cref="GameImagesDashboardViewModel"/> (todas las
/// imágenes del juego) como en el <see cref="GameImagesRegionDashboardViewModel"/> (las de la región activa).
/// </summary>
public static class GameImagePreselection
{
    /// <summary>
    /// Aplica los <paramref name="criteria"/> activos en cascada sobre <paramref name="images"/>: cada criterio
    /// filtra la lista actual; si deja 0 candidatos se ignora (filtrado progresivo tolerante). Devuelve el primer
    /// candidato resultante, o el primero de la lista, o una <see cref="GameImage"/> vacía si no hay imágenes.
    /// </summary>
    public static GameImage Preselect(IEnumerable<GameImage> images, IEnumerable<GameImageCriterion> criteria)
    {
        List<GameImage> candidates = images.Where(image => image != null).ToList();

        if (candidates.Count == 0)
        {
            return new();
        }

        foreach (GameImageCriterion criterion in criteria)
        {
            if (!criterion.IsActive || candidates.Count == 0 || string.IsNullOrWhiteSpace(criterion.Name))
            {
                continue;
            }

            List<GameImage> filteredCandidates;

            if (criterion.Name == ImageSettings.FileDimensions.Value)
            {
                long maxDimensions = candidates.Max(image => (long)image.Width * image.Height);
                filteredCandidates = candidates.Where(image => (long)image.Width * image.Height == maxDimensions).ToList();
            }
            else if (criterion.Name == ImageSettings.FileSize.Value)
            {
                long maxFileSize = candidates.Max(image => image.FileSize);
                filteredCandidates = candidates.Where(image => image.FileSize == maxFileSize).ToList();
            }
            else
            {
                string expectedExtension = $".{criterion.Name}";
                filteredCandidates = candidates
                    .Where(image => !string.IsNullOrWhiteSpace(image.FileExtension)
                        && string.Equals(image.FileExtension, expectedExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (filteredCandidates.Count > 0)
            {
                candidates = filteredCandidates;
            }
        }

        return candidates.FirstOrDefault() ?? new();
    }
}
