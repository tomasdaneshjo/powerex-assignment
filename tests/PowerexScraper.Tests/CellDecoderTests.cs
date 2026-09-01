using System.Text.Json;
using PowerexScraper.Flattening;

namespace PowerexScraper.Tests;

public class CellDecoderTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    [Theory]
    [InlineData("\"2665.90\"", "2665.90")]      // number-as-string: verbatim, never reformatted
    [InlineData("\"0.00\"", "0.00")]
    [InlineData("{\"alt\":\"N/A\"}", "")]        // not available
    [InlineData("{\"alt\":\"-\"}", "")]          // missing
    [InlineData("{\"alt\":\"n/e\"}", "")]        // not expected yet (future timestamps)
    [InlineData("null", "")]                     // attribute not applicable
    [InlineData("{\"unexpected\":1}", "")]       // unknown object → empty, never throw
    [InlineData("true", "")]                     // unknown kind → empty
    public void Decode_maps_all_observed_encodings(string json, string expected)
        => Assert.Equal(expected, CellDecoder.Decode(Parse(json)));

    [Fact]
    public void Decode_raw_number_uses_invariant_culture()
        => Assert.Equal("42.5", CellDecoder.Decode(Parse("42.5")));
}
