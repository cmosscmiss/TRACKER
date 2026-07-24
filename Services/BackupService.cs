using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.IO;
using System.Threading.Tasks;

namespace MM4LB.Services;

/// <summary>
/// Rastrea la carpeta de BACKUP de imágenes (subcarpeta "BACKUP" de donde vive la configuración,
/// <c>%LocalAppData%\MM4LB\BACKUP</c>): número de ficheros y tamaño en disco. Permite vaciarla. Expone
/// propiedades observables para una pastilla del ACTIVITY LOG y notifica (<see cref="Cleared"/>) cuando se
/// vacía, para invalidar los undos que dependían del backup.
/// </summary>
public class BackupService : ObservableObject
{
    private int _imagesCount;
    private long _sizeBytes;

    /// <summary>Carpeta de backup: subcarpeta "BACKUP" de la carpeta de configuración.</summary>
    public string BackupFolder => Path.Combine(PersistAndRestoreService.SettingsFolderPath, "BACKUP");

    /// <summary>Número de imágenes respaldadas.</summary>
    public int ImagesCount
    {
        get => _imagesCount;
        private set
        {
            if (SetProperty(ref _imagesCount, value))
                OnPropertyChanged(nameof(HasBackups));
        }
    }

    /// <summary>Tamaño total en disco de los backups, en MB.</summary>
    public double SizeMb => Math.Round(_sizeBytes / (1024.0 * 1024.0), 2);

    /// <summary>True si hay al menos un backup (gobierna el botón de vaciar).</summary>
    public bool HasBackups => _imagesCount > 0;

    /// <summary>Se dispara tras vaciar la carpeta de backup.</summary>
    public event Action? Cleared;

    /// <summary>
    /// Escanea la carpeta de backup (recursivo) y actualiza contador y tamaño. Pensado para el arranque.
    /// El escaneo se hace en un hilo de fondo; la asignación vuelve al contexto del llamador.
    /// </summary>
    public async Task RefreshAsync()
    {
        (int count, long bytes) = await Task.Run(Scan);
        SetTotals(count, bytes);
    }

    /// <summary>Registra un backup recién creado (suma al contador/tamaño sin re-escanear).</summary>
    public void RegisterBackup(long sizeBytes)
    {
        SetTotals(_imagesCount + 1, _sizeBytes + sizeBytes);
    }

    /// <summary>Vacía la carpeta de backup, resetea los contadores y notifica con <see cref="Cleared"/>.</summary>
    public async Task ClearAsync()
    {
        (int count, long bytes) = await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(BackupFolder))
                {
                    foreach (string file in Directory.EnumerateFiles(BackupFolder, "*", SearchOption.AllDirectories))
                    {
                        try { File.Delete(file); } catch { }
                    }
                    foreach (string dir in Directory.EnumerateDirectories(BackupFolder))
                    {
                        try { Directory.Delete(dir, recursive: true); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                ExceptionService.LogToFile(ex, "Error clearing the backup folder.");
            }

            // Re-escanea lo que REALMENTE queda en vez de asumir 0: si algún fichero estaba bloqueado y no se
            // pudo borrar, la pastilla del ACTIVITY LOG reflejará el estado real (antes mostraba 0 aunque quedaran).
            return Scan();
        });

        SetTotals(count, bytes);
        Cleared?.Invoke();
    }

    private void SetTotals(int count, long bytes)
    {
        _sizeBytes = bytes;
        ImagesCount = count;            // dispara HasBackups
        OnPropertyChanged(nameof(SizeMb));
    }

    private (int count, long bytes) Scan()
    {
        try
        {
            if (!Directory.Exists(BackupFolder))
                return (0, 0);

            int count = 0;
            long bytes = 0;
            foreach (string file in Directory.EnumerateFiles(BackupFolder, "*", SearchOption.AllDirectories))
            {
                try
                {
                    bytes += new FileInfo(file).Length;
                    count++;
                }
                catch { }
            }
            return (count, bytes);
        }
        catch
        {
            return (0, 0);
        }
    }
}
