namespace PowerexScraper.Storage;

public interface IObjectStore
{
    Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default);
}
