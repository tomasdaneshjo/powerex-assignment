using Amazon.S3;
using Amazon.S3.Model;
using NSubstitute;
using PowerexScraper.Storage;

namespace PowerexScraper.Tests;

public class StorageTests
{
    [Fact]
    public void CsvKey_uses_data_date_and_strips_area_type_prefix()
        => Assert.Equal("data/generation-forecast-dayahead/area=10YSK-SEPS-----K/2026-09-01.csv",
            KeyBuilder.CsvKey("generation-forecast-dayahead", "CTA|10YSK-SEPS-----K", new DateOnly(2026, 9, 1)));

    [Fact]
    public void CsvKey_without_pipe_uses_token_as_is()
        => Assert.Equal("data/x/area=RAW/2026-01-02.csv",
            KeyBuilder.CsvKey("x", "RAW", new DateOnly(2026, 1, 2)));

    [Fact]
    public void RawKey_has_no_area_segment()
        => Assert.Equal("raw/generation-actual-perunit/2026-08-30.json",
            KeyBuilder.RawKey("generation-actual-perunit", new DateOnly(2026, 8, 30)));

    [Fact]
    public async Task S3ObjectStore_puts_bucket_key_content_type_and_bytes()
    {
        var s3 = Substitute.For<IAmazonS3>();
        PutObjectRequest? captured = null;
        await s3.PutObjectAsync(Arg.Do<PutObjectRequest>(r => captured = r), Arg.Any<CancellationToken>());

        var store = new S3ObjectStore(s3, "my-bucket");
        await store.PutAsync("data/x/area=Y/2026-01-01.csv", "a,b\r\n"u8.ToArray(), "text/csv");

        Assert.NotNull(captured);
        Assert.Equal("my-bucket", captured!.BucketName);
        Assert.Equal("data/x/area=Y/2026-01-01.csv", captured.Key);
        Assert.Equal("text/csv", captured.ContentType);
        using var ms = new MemoryStream();
        await captured.InputStream.CopyToAsync(ms);
        Assert.Equal("a,b\r\n"u8.ToArray(), ms.ToArray());
    }

    [Fact]
    public async Task FileSystemObjectStore_writes_key_as_relative_path()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var store = new FileSystemObjectStore(root);
        await store.PutAsync("data/x/area=Y/2026-01-01.csv", "hello"u8.ToArray(), "text/csv");

        var written = Path.Combine(root, "data", "x", "area=Y", "2026-01-01.csv");
        Assert.True(File.Exists(written));
        Assert.Equal("hello", File.ReadAllText(written));
        Directory.Delete(root, recursive: true);
    }
}
