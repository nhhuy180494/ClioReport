using Xunit;

namespace ClioReport.Tests;

public class QrCodeTests
{
    [Theory]
    [InlineData("A", 21)]
    [InlineData("abcdefghijklmnopqrstuvwxyz", 25)]   // 26 bytes still fit version 2-M
    [InlineData("abcdefghijklmnopqrstuvwxyz0", 29)]  // 27 bytes need version 3-M
    public void Version_IsTheSmallestThatFits(string text, int size)
    {
        var modules = QrCode.Encode(text);

        Assert.Equal(size, modules.GetLength(0));
        Assert.Equal(size, modules.GetLength(1));
    }

    [Fact]
    public void FinderPatterns_AreInThreeCorners()
    {
        var m = QrCode.Encode("Hello");
        var n = m.GetLength(0);

        foreach (var (row, col) in new[] { (0, 0), (0, n - 7), (n - 7, 0) })
        {
            Assert.True(m[row, col] && m[row + 6, col + 6] && m[row + 3, col + 3]);
            Assert.False(m[row + 1, col + 1]);
        }
    }

    [Fact]
    public void SameText_GivesSameModules()
    {
        Assert.Equal(
            QrCode.Encode("PXK-1").Cast<bool>().ToArray(),
            QrCode.Encode("PXK-1").Cast<bool>().ToArray());
    }

    [Fact]
    public void Utf8Text_IsAccepted() =>
        Assert.True(QrCode.Encode("Tiếng Việt có dấu").GetLength(0) >= 21);

    [Fact]
    public void LargeText_UsesHigherVersions() =>
        Assert.Equal(32 * 4 + 17, QrCode.Encode(new string('z', 1500)).GetLength(0));

    [Fact]
    public void TooLongText_Throws() =>
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('z', 3000)));
}
