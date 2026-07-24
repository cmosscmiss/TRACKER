using CommunityToolkit.Mvvm.ComponentModel;
using System.IO;
using Windows.Storage;

namespace MM4LB.Models;

/// <summary>
/// Helper abstract class for files in the file system.
/// </summary>
public abstract class LocalFile : ObservableObject
{
    #region Attributes
    private string _name = string.Empty;
    #endregion


    #region Properties (Observable)
    /// <summary>
    /// Name of the file without extension.
    /// </summary>
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, value);
    }
    #endregion


    #region Properties
    /// <summary>
    /// File extension of the local file.
    /// </summary>
    public string FileExtension => Path.GetExtension(File);

    /// <summary>
    /// Leaf subfolder of the path where the file is located (the region of the image, for images).
    /// </summary>
    public string FileLeafFolder
    {
        get; protected set;
    } = string.Empty;

    /// <summary>
    /// Size of the file, calculated on construction if the file exists.
    /// </summary>
    public long FileSize
    {
        get; protected set;
    }

    /// <summary>
    /// Full path to the local file.
    /// </summary>
    public string File
    {
        get; protected set;
    } = string.Empty;

    #endregion

    #region Constructors
    /// <summary>
    /// Default initialisation.
    /// </summary>
    protected LocalFile()
    {
    }

    /// <summary>
    /// Initialisation given a path to a file in the file system. 
    /// </summary>
    /// <param name="localFile">Full path to the local file</param>
    public LocalFile(string localFile)
    {
        File = localFile;
        string? filePath = Path.GetDirectoryName(localFile);
        FileLeafFolder = filePath is null ? string.Empty : filePath[(filePath.LastIndexOf(@"\") + 1)..];
        Name = Path.GetFileNameWithoutExtension(localFile);
        SetFileSize();
    }
    #endregion

    #region Methods (private)
    /// <summary>
    /// Sets the size of the file if it exists.
    /// </summary>
    protected void SetFileSize()
    {
        if (FileSize == 0 && System.IO.File.Exists(File))
        {
            FileInfo fileInfo = new(File);
            FileSize = fileInfo.Length / 1000;
        }
    }
    #endregion
}