using System.Security.Cryptography;

namespace MacOSify.Win11.Services;

public sealed class SecureDownloadService(HttpClient httpClient)
{
    public async Task<string> DownloadAsync(
        Uri source,
        string destinationPath,
        string expectedSha256,
        long maximumBytes,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        bool requireAuthenticode = false)
    {
        if (!source.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only HTTPS downloads are permitted.");
        }

        var expectedHash = ParseSha256(expectedSha256);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)
            ?? throw new ArgumentException("A destination directory is required.", nameof(destinationPath)));

        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.part";
        try
        {
            using var response = await httpClient.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri?.Scheme != Uri.UriSchemeHttps)
            {
                throw new InvalidOperationException("The download redirected to a non-HTTPS endpoint.");
            }

            var contentLength = response.Content.Headers.ContentLength;
            if (contentLength > maximumBytes)
            {
                throw new InvalidDataException("The server reported an artifact larger than the package policy allows.");
            }

            byte[] actualHash;
            await using (var sourceStream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var destinationStream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81_920,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81_920];
                long total = 0;

                while (true)
                {
                    var read = await sourceStream.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > maximumBytes)
                    {
                        throw new InvalidDataException("The downloaded artifact exceeded the package size limit.");
                    }

                    hasher.AppendData(buffer, 0, read);
                    await destinationStream.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    if (contentLength is > 0)
                    {
                        progress?.Report((double)total / contentLength.Value);
                    }
                }

                await destinationStream.FlushAsync(cancellationToken);
                actualHash = hasher.GetHashAndReset();
            }

            if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
            {
                throw new CryptographicException("The downloaded file failed SHA-256 verification and will not be used.");
            }

            if (requireAuthenticode)
            {
                AuthenticodeVerifier.Verify(temporaryPath);
            }

            File.Move(temporaryPath, destinationPath, overwrite: true);
            return destinationPath;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public static async Task VerifyFileAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        var expectedHash = ParseSha256(expectedSha256);
        await using var stream = File.OpenRead(path);
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
        if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
        {
            throw new CryptographicException($"{Path.GetFileName(path)} failed SHA-256 verification.");
        }
    }

    private static byte[] ParseSha256(string value)
    {
        if (value.Length != 64 || !value.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("A complete 64-character SHA-256 value is required.", nameof(value));
        }

        return Convert.FromHexString(value);
    }
}
