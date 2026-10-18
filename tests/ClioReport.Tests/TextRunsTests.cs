using ClioReport.Pdf;
using Xunit;

namespace ClioReport.Tests;

public class TextRunsTests
{
    // "Latin" font has ASCII and Vietnamese letters; "Hangul" font has only Hangul syllables.
    private static bool Latin(int c) => c < 0x250 || (c >= 0x1EA0 && c <= 0x1EF9);
    private static bool Hangul(int c) => c >= 0xAC00 && c <= 0xD7A3;

    [Fact]
    public void WithoutFallback_IsOneRun()
    {
        var runs = TextRuns.Split("abc 한", Latin, null);

        Assert.Equal(new[] { ("abc 한", false) }, runs);
    }

    [Fact]
    public void FallbackOnlyForCharactersTheMainFontLacks()
    {
        var runs = TextRuns.Split("Tên: 홈온 (ế)", Latin, Hangul);

        Assert.Equal(new[] { ("Tên: ", false), ("홈온 ", true), ("(ế)", false) }, runs);
    }

    [Fact]
    public void SpacesStayWithTheRunBeforeThem()
    {
        var runs = TextRuns.Split("가 나 다", Latin, Hangul);

        Assert.Equal(new[] { ("가 나 다", true) }, runs);
    }

    [Fact]
    public void CharacterInNeitherFont_StaysInTheMainRun()
    {
        var runs = TextRuns.Split("a\u4E2Db", Latin, Hangul);

        Assert.Equal(new[] { ("a\u4E2Db", false) }, runs);
    }

    [Fact]
    public void EmptyText_HasNoRuns() =>
        Assert.Empty(TextRuns.Split("", Latin, Hangul));

    private static string? FontFile(string name)
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), name);
        return File.Exists(path) ? path : null;
    }

    [Fact]
    public void Coverage_ReadsRealFonts()
    {
        var arial = FontFile("arial.ttf");
        if (arial is null) return;

        var coverage = TrueTypeCoverage.Load(arial);

        Assert.NotNull(coverage);
        Assert.True(coverage!.Has('A'));
        Assert.True(coverage.Has('ế'));
        Assert.False(coverage.Has('한'));
    }

    [Fact]
    public void Coverage_MalgunHasHangulButNotAllVietnamese()
    {
        var malgun = FontFile("malgun.ttf");
        if (malgun is null) return;

        var coverage = TrueTypeCoverage.Load(malgun);

        Assert.NotNull(coverage);
        Assert.True(coverage!.Has('한'));
        Assert.False(coverage.Has('ế'));
    }

    [Fact]
    public void Coverage_UnreadableFile_IsNull() =>
        Assert.Null(TrueTypeCoverage.Load(Path.Combine(Path.GetTempPath(), "no-such-font-" + Guid.NewGuid().ToString("N") + ".ttf")));
}
