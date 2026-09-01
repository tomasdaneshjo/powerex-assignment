namespace PowerexScraper.Storage;

/// <summary>Same key layout as S3, on local disk — used by the LocalRunner and tests.</summary>
public sealed class FileSystemObjectStore(string rootDirectory) : IObjectStore
{
    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        var path = Path.Combine([rootDirectory, .. key.Split('/')]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, content, ct);
    }
}
