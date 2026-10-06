using System.IO.Compression;

namespace MacOSify.Win11.Services;

public sealed class SafeArchiveExtractor
{
    public async Task ExtractZipAsync(
        string archivePath,
        string destinationDirectory,
        CancellationToken cancellationToken,
        int maximumEntries = 10_000,
        long maximumExpandedBytes = 1_000_000_000)
    {
        var destinationRoot = Path.GetFullPath(destinationDirectory) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(destinationRoot);

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > maximumEntries)
        {
            throw new InvalidDataException("The archive contains too many entries.");
        }

        long expandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEntryName(entry.FullName);

            expandedBytes = checked(expandedBytes + entry.Length);
            if (expandedBytes > maximumExpandedBytes || entry.Length > maximumExpandedBytes)
            {
                throw new InvalidDataException("The archive exceeds the expanded-size policy.");
            }

            if (entry.CompressedLength > 0 && entry.Length / Math.Max(1, entry.CompressedLength) > 500)
            {
                throw new InvalidDataException("The archive contains an entry with an unsafe compression ratio.");
            }

            var unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType == 0xA000)
            {
                throw new InvalidDataException("Symbolic links are not permitted in package archives.");
            }

            var targetPath = Path.GetFullPath(Path.Combine(destinationDirectory, entry.FullName));
            if (!targetPath.StartsWith(destinationRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("The archive attempted to write outside the staging directory.");
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
            await using var input = entry.Open();
            await using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await input.CopyToAsync(output, cancellationToken);
        }
    }

    private static void ValidateEntryName(string entryName)
    {
        if (string.IsNullOrWhiteSpace(entryName) ||
            Path.IsPathRooted(entryName) ||
            entryName.Contains(':') ||
            entryName.Split(['/', '\\']).Any(segment => segment == ".."))
        {
            throw new InvalidDataException($"Unsafe archive entry: {entryName}");
        }
    }
}
