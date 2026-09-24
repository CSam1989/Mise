using System.Collections;
using System.Globalization;
using System.Resources;
using Mise.UI.Components;

namespace Mise.Client.UnitTests.Localization;

public class UiStringsResourceTests
{
    private static readonly ResourceManager Resources = new("Mise.UI.Components.Resources.UiStrings", typeof(UiStrings).Assembly);

    private static Dictionary<string, string> Read(CultureInfo culture) =>
        Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)!
            .Cast<DictionaryEntry>()
            .ToDictionary(e => (string)e.Key, e => (string)e.Value!);

    [Fact]
    public void NeutralAndDutch_HaveExactlyTheSameKeys()
    {
        var neutral = Read(CultureInfo.InvariantCulture);
        var dutch = Read(CultureInfo.GetCultureInfo("nl-BE"));

        dutch.Keys.Should().BeEquivalentTo(neutral.Keys,
            because: "nl-BE is the default culture — a key missing from it silently falls back to English mid-screen.");
    }

    [Fact]
    public void EveryValue_IsNonEmpty()
    {
        var all = Read(CultureInfo.InvariantCulture).Concat(Read(CultureInfo.GetCultureInfo("nl-BE")));

        all.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Value));
    }

    [Fact]
    public void FormatPlaceholders_MatchAcrossCultures()
    {
        var neutral = Read(CultureInfo.InvariantCulture);
        var dutch = Read(CultureInfo.GetCultureInfo("nl-BE"));

        foreach (var (key, value) in neutral)
        {
            var expected = System.Text.RegularExpressions.Regex.Matches(value, @"\{\d+\}").Select(m => m.Value).Order();
            var actual = System.Text.RegularExpressions.Regex.Matches(dutch[key], @"\{\d+\}").Select(m => m.Value).Order();
            actual.Should().Equal(expected, because: $"'{key}' must take the same format arguments in every culture or string.Format throws at runtime.");
        }
    }
}
