using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace KeyValidation.Infrastructure;
internal static class BackupPaths
{
    public static void NoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new BackupFailure("BACKUP_PATH_UNSAFE");
            var parent = Path.GetDirectoryName(current);
            if (parent == current)
                break;
            current = parent!;
        }
    }

    public static string Child(string root, string name)
    {
        NoLinks(root);
        var absolute = Path.GetFullPath(Path.Combine(root, name));
        if (!absolute.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new BackupFailure("BACKUP_PATH_UNSAFE");
        NoLinks(absolute);
        return absolute;
    }

    public static string CreatePrivateDirectory(string root, string name)
    {
        var path = Child(root, name);
        if (Directory.Exists(path) || File.Exists(path))
            throw new BackupFailure("BACKUP_PATH_CONFLICT");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(path);
        else
            Directory.CreateDirectory(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    public static void DeleteOwnedDirectory(string root, string path)
    {
        var absolute = Child(root, Path.GetRelativePath(root, path));
        if (!Directory.Exists(absolute))
            return;
        // Enumerate manually and never traverse a reparse point.
        foreach (var file in Directory.EnumerateFileSystemEntries(absolute))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.Directory) == 0)
                File.Delete(file);
            else if ((attributes & FileAttributes.ReparsePoint) != 0)
                Directory.Delete(file);
            else
                DeleteOwnedDirectory(absolute, file);
        }

        Directory.Delete(absolute);
    }
}
