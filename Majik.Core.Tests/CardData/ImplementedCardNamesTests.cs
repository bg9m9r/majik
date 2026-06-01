using System.Reflection;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Coverage for <see cref="ImplementedCardNames"/> — the single source of
/// truth (shared by the runtime <c>EmbeddedCardRepository</c> and the
/// export CLI) for which printed names the engine actually implements.
/// </summary>
public class ImplementedCardNamesTests
{
    [Fact]
    public void All_IncludesFactoryBackedNames()
    {
        ImplementedCardNames.All.Should().Contain("Lightning Bolt",
            "LightningBoltFactory carries [CardName(\"Lightning Bolt\")]");
        ImplementedCardNames.All.Should().Contain("Path to Exile",
            "PathToExileFactory carries [CardName(\"Path to Exile\")]");
    }

    [Fact]
    public void All_IncludesInlineFallbacks()
    {
        foreach (var name in ImplementedCardNames.InlineFallbackNames)
            ImplementedCardNames.All.Should().Contain(name);
    }

    [Fact]
    public void Contains_FactoryBacked_True()
    {
        ImplementedCardNames.Contains("Forest").Should().BeTrue();
        ImplementedCardNames.Contains("Lightning Bolt").Should().BeTrue();
    }

    [Fact]
    public void Contains_UnknownOrEmpty_False()
    {
        ImplementedCardNames.Contains("Definitely Not A Real Card").Should().BeFalse();
        ImplementedCardNames.Contains("").Should().BeFalse();
        ImplementedCardNames.Contains(null!).Should().BeFalse();
    }

    /// <summary>
    /// PLAN 03 Slice 3 — fileless JSON cards. The ~118 deleted wrapper
    /// factories no longer carry a <c>[CardName]</c> attribute; their names now
    /// flow into <see cref="ImplementedCardNames.All"/> via the generator's
    /// <see cref="NamedCardFactory.GeneratedJsonCardNames"/>. Every fileless
    /// JSON name must therefore still report as implemented (zero regression).
    /// </summary>
    [Fact]
    public void All_IncludesEveryFilelessJsonCardName()
    {
        NamedCardFactory.GeneratedJsonCardNames.Should().NotBeEmpty(
            "Slice 3 deleted wrapper factories in favour of generated JSON arms");

        foreach (var name in NamedCardFactory.GeneratedJsonCardNames)
        {
            ImplementedCardNames.All.Should().Contain(name,
                $"the fileless JSON card \"{name}\" must remain implemented after its wrapper was deleted");

            // FactoryBackedNames excludes the inline fallbacks (basic lands +
            // the four vanilla test creatures). A handful of those vanilla
            // creatures (e.g. Centaur Courser, Grizzly Bears) ALSO have a JSON
            // file, so they appear in the generated set yet are — as before —
            // not "factory backed". Only assert factory-backing for the rest.
            if (!ImplementedCardNames.InlineFallbackNames.Contains(name))
            {
                ImplementedCardNames.FactoryBackedNames.Should().Contain(name,
                    $"\"{name}\" is data-driven (not an inline fallback) so it stays factory-backed");
            }
        }
    }

    /// <summary>
    /// The implemented-name set is exactly the union of: the reflected
    /// <c>[CardName]</c> attribute names, the generated fileless JSON names,
    /// and the inline fallbacks — with no double-counting. This pins the
    /// total so a future wrapper deletion that forgets to feed the JSON name
    /// in (or a generator regression) is caught.
    /// </summary>
    [Fact]
    public void All_Equals_AttributeNames_Plus_JsonNames_Plus_Inline()
    {
        var expected = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var type in typeof(CardNameAttribute).Assembly.GetTypes())
        {
            foreach (var attr in type.GetCustomAttributes<CardNameAttribute>(inherit: false))
            {
                if (!string.IsNullOrWhiteSpace(attr.Name)) expected.Add(attr.Name);
            }
        }
        foreach (var n in NamedCardFactory.GeneratedJsonCardNames) expected.Add(n);
        foreach (var n in ImplementedCardNames.InlineFallbackNames) expected.Add(n);

        ImplementedCardNames.All.Should().BeEquivalentTo(expected);
    }
}
