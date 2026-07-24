using MM4LB.Enums;
using System.Linq;
using System.Xml;

namespace MM4LB.Models;

/// <summary>
/// Represents a single image folder definition for a platform,
/// as declared in LaunchBox's Platforms.xml.
///
/// Each PlatformImageFolder corresponds to one entry like:
///   <PlatformFolder>
///       <Platform>Nintendo NES</Platform>
///       <FolderPath>Images\Box - Front</FolderPath>
///       <MediaType>BoxFront</MediaType>
///   </PlatformFolder>
///
/// This class is a pure DTO:
/// - It contains no logic for loading files.
/// - It contains no matching logic.
/// - It contains no filesystem access.
/// - It contains no async operations.
///
/// LaunchBoxService uses these objects to create PlatformImageSet instances.
/// </summary>
public class PlatformImageFolder
{
    /// <summary>
    /// The type of media stored in this folder (e.g., BoxFront, ClearLogo, Screenshot).
    /// Parsed from the <MediaType> node in Platforms.xml.
    /// </summary>
    public MediaType? ImageType
    {
        get; set;
    }

    /// <summary>
    /// The folder path where the images are stored.
    /// This path may be relative (e.g., "Images\Box - Front") or absolute,
    /// depending on how LaunchBoxService normalizes it.
    /// </summary>
    public string FolderPath
    {
        get; set;
    }

    /// <summary>
    /// The name of the platform this folder belongs to (e.g., "Nintendo NES").
    /// Parsed from the <Platform> node in Platforms.xml.
    /// </summary>
    public string Platform
    {
        get; set;
    }

    /// <summary>
    /// Creates a PlatformImageFolder from a <PlatformFolder> XML node.
    ///
    /// Responsibilities:
    /// - Extract FolderPath
    /// - Extract Platform name
    /// - Resolve MediaType from its string value
    ///
    /// This constructor does NOT:
    /// - Normalize paths (LaunchBoxService does that)
    /// - Filter out unwanted folders (Platform.ImageFolderStrings does that)
    /// - Load image files (LaunchBoxService does that)
    /// </summary>
    public PlatformImageFolder(XmlNode platformImageFolder)
    {
        FolderPath = platformImageFolder["FolderPath"]?.InnerText ?? "";

        Platform = platformImageFolder["Platform"]?.InnerText ?? "";

        // Resolve the MediaType enum from the string value in the XML.
        // Example: "BoxFront" → MediaType.BoxFront
        // Defensivo: un <PlatformFolder> sin nodo <MediaType> (Platforms.xml editado a mano/corrupto, o un
        // esquema futuro de LaunchBox) reventaría aquí con NullReferenceException y abortaría la carga completa
        // de la plataforma. Un ImageType null ya se maneja aguas abajo (Platform.ImageFolderStrings ignora los
        // tipos desconocidos), igual que las dos comprobaciones de arriba.
        ImageType = Enumeration
            .GetAll<MediaType>()
            .ToList()
            .Find(x => x.Value == platformImageFolder["MediaType"]?.InnerText);
    }
}