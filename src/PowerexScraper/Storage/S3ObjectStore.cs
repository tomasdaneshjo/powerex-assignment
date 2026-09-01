using Amazon.S3;
using Amazon.S3.Model;

namespace PowerexScraper.Storage;

public sealed class S3ObjectStore(IAmazonS3 s3, string bucketName) : IObjectStore
{
    public async Task PutAsync(string key, byte[] content, string contentType, CancellationToken ct = default)
    {
        // Intentionally not `using`-disposed: PutObjectRequest.AutoCloseStream (default true)
        // closes it after upload, and disposing it early here breaks capture in tests.
        var stream = new MemoryStream(content);
        await s3.PutObjectAsync(new PutObjectRequest
        {
            BucketName = bucketName,
            Key = key,
            ContentType = contentType,
            InputStream = stream,
        }, ct);
    }
}
