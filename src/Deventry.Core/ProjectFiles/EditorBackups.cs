using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Deventry.Core.Files;
using Deventry.Core.Settings;

namespace Deventry.Core.ProjectFiles;

/// <summary>An open file's unsaved text, kept so quitting, a crash or a restart never loses it.</summary>
/// <param name="Path">The file's full path.</param>
/// <param name="Text">What the editor held, not saved.</param>
/// <param name="Base">A hash of the file as the editor read it (<see cref="EditorBackups.Hash"/>), to tell whether it changed on disk since.</param>
public sealed record EditorBackup(string Path, string Text, string Base, DateTimeOffset SavedAt);

/// <summary>
/// The Project Files tabs' unsaved edits on disk (DESIGN.md §18, "Project files", "Saving"): a file each in
/// <see cref="FolderName"/> in the data folder, named by a hash of the file's path, written whole or not at all. Saving
/// the file, or <b>Don't save</b>, deletes its backup. Reading and deleting one wait out something else that has it open
/// for a moment, as writing it does (<see cref="FileRetry"/>): on Windows a delete fails while a scanner, or a read of
/// it, has it open, and edits thrown away would come back at launch. Nothing here throws: one that can't be read reads
/// as none.
/// </summary>
public sealed class EditorBackups(string directory, TimeProvider time)
{
    public const string FolderName = "editor-backups";

    /// <summary>A backup no open file has used for this long is deleted at launch.</summary>
    public static readonly TimeSpan KeepUnused = TimeSpan.FromDays(30);

    private static readonly JsonSerializerOptions Options = JsonFileStore<AppState>.Options;

    public string Directory => directory;

    /// <summary>A hash of a file's text as read, or of its being missing.</summary>
    public static string Hash(string? text) =>
        text is null ? "missing" : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Keeps <paramref name="text"/> as <paramref name="path"/>'s unsaved edits. False when it couldn't be written.</summary>
    public bool Save(string path, string text, string baseHash)
    {
        try
        {
            System.IO.Directory.CreateDirectory(directory);
            var json = JsonSerializer.Serialize(new EditorBackup(path, text, baseHash, time.GetUtcNow()), Options);
            AtomicFile.WriteAllText(FileFor(path), json);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary><paramref name="path"/>'s unsaved edits, or null when it has none.</summary>
    public EditorBackup? Load(string path)
    {
        var backup = Read(FileFor(path));
        return backup is not null && PathsEqual(backup.Path, path) ? backup : null;
    }

    /// <summary>Forgets <paramref name="path"/>'s unsaved edits: they were saved, or thrown away.</summary>
    public void Delete(string path)
    {
        try
        {
            var file = FileFor(path);
            FileRetry.Run(() => File.Delete(file));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the backups of files none of <paramref name="openPaths"/> is that are older than <see cref="KeepUnused"/>:
    /// a Project Files tab whose folder went away, say.
    /// </summary>
    public void DeleteUnused(IEnumerable<string> openPaths)
    {
        var open = openPaths.Select(FileFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (!System.IO.Directory.Exists(directory))
            {
                return;
            }
            foreach (var file in System.IO.Directory.EnumerateFiles(directory, "*.json"))
            {
                // By when it was saved, on the injected clock; one that can't be read is a leftover too.
                if (!open.Contains(file) && (Read(file) is not { } backup || time.GetUtcNow() - backup.SavedAt > KeepUnused))
                {
                    File.Delete(file);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string FileFor(string path)
    {
        var key = OperatingSystem.IsLinux() ? path : path.ToUpperInvariant();
        return System.IO.Path.Combine(directory, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32] + ".json");
    }

    private static EditorBackup? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<EditorBackup>(FileRetry.Run(() => File.ReadAllText(file)), Options) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(a, b, OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
}
