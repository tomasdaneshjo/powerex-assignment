using System.Text.Json;
using PowerexScraper.Entsoe;

namespace PowerexScraper.Tests;

public class EnvelopeValidatorTests
{
    [Fact]
    public void Valid_envelope_passes()
    {
        using var doc = JsonDocument.Parse("""{"uuAppErrorMap":{},"instanceList":[],"metaData":[]}""");
        EnvelopeValidator.Validate(doc); // must not throw
    }

    [Fact]
    public void Non_empty_uuAppErrorMap_throws_with_error_code_in_message()
    {
        using var doc = JsonDocument.Parse(
            """{"uuAppErrorMap":{"uu-app-server/internalServerError":{"type":"error"}},"instanceList":[],"metaData":[]}""");
        var ex = Assert.Throws<EntsoeContractException>(() => EnvelopeValidator.Validate(doc));
        Assert.Contains("uu-app-server/internalServerError", ex.Message);
    }

    [Fact]
    public void Missing_instanceList_throws()
    {
        using var doc = JsonDocument.Parse("""{"uuAppErrorMap":{},"metaData":[]}""");
        var ex = Assert.Throws<EntsoeContractException>(() => EnvelopeValidator.Validate(doc));
        Assert.Contains("instanceList", ex.Message);
    }

    [Fact]
    public void Missing_metaData_throws()
    {
        using var doc = JsonDocument.Parse("""{"uuAppErrorMap":{},"instanceList":[]}""");
        var ex = Assert.Throws<EntsoeContractException>(() => EnvelopeValidator.Validate(doc));
        Assert.Contains("metaData", ex.Message);
    }

    [Fact]
    public void Body_excerpt_is_truncated_to_2000_chars()
    {
        var bigError = new string('x', 5000);
        using var doc = JsonDocument.Parse(
            $$$"""{"uuAppErrorMap":{"code":{"detail":"{{{bigError}}}"}},"instanceList":[],"metaData":[]}""");
        var ex = Assert.Throws<EntsoeContractException>(() => EnvelopeValidator.Validate(doc));
        Assert.NotNull(ex.BodyExcerpt);
        Assert.Equal(2000, ex.BodyExcerpt!.Length);
    }

    [Fact]
    public void Non_object_root_throws_typed_contract_exception()
    {
        using var doc = JsonDocument.Parse("[1,2,3]");
        var ex = Assert.Throws<EntsoeContractException>(() => EnvelopeValidator.Validate(doc));
        Assert.Contains("root", ex.Message);
    }
}
