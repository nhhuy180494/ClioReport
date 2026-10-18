using Xunit;

namespace ClioReport.Tests;

public class Code128Tests
{
    private static string Bits(string text) =>
        new(Code128.Encode(text).Select(b => b ? '1' : '0').ToArray());

    // Expected values are identical to the output of the python-barcode reference implementation.
    [Fact]
    public void EvenDigits_UseSetC() =>
        Assert.Equal("1101001110010110011100100010110001110001011011000010100100011101101100011101011", Bits("12345678"));

    [Fact]
    public void Text_UsesSetB() =>
        Assert.Equal("110100100001100010100010110010000110010100001100101000010001111010110010100001100011101011", Bits("Hello"));

    [Theory]
    [InlineData("1234", 57)]
    [InlineData("12", 57)]   // too short for set C
    [InlineData("123", 68)]  // odd length stays in set B
    [InlineData("A", 46)]
    public void Length_IsStartPlusSymbolsPlusChecksumPlusStop(string text, int modules) =>
        Assert.Equal(modules, Code128.Encode(text).Length);

    [Fact]
    public void StartsWithStartB_AndEndsWithStop()
    {
        var bits = Bits("A");

        Assert.Equal("11010010000", bits[..11]);
        Assert.Equal("1100011101011", bits[^13..]);
    }

    [Fact]
    public void UnsupportedCharacter_Throws() =>
        Assert.Throws<ArgumentException>(() => Code128.Encode("Việt"));

    [Fact]
    public void EmptyText_Throws() =>
        Assert.Throws<ArgumentException>(() => Code128.Encode(""));
}
