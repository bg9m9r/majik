using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// Coverage for the top-level <c>keywords</c> array on the declarative JSON
/// <see cref="CardDefinition"/> schema. CR 702 — a printed keyword line
/// (Haste, Flying, Trample, …) is modelled as a
/// <see cref="KeywordAbility"/> marker stamped on the runtime card.
///
/// <para>The <see cref="CardDef"/> shape + the
/// <see cref="CardDefRuntime"/> materializer already iterate
/// <see cref="CardDef.Keywords"/> and stamp a <see cref="KeywordAbility"/> per
/// entry; the gap this closes is purely on the JSON serialization surface —
/// the JSON <see cref="CardDefinition"/> had no <c>keywords</c> field, so a
/// printed-keyword line could not be expressed declaratively. With the field
/// in place, the whole cluster of "vanilla body + printed keyword line"
/// creatures (Devoid colourless bodies included) becomes pure-JSON.</para>
/// </summary>
public class JsonKeywordsArrayTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Keywords_FromJson_StampKeywordAbilityMarkers()
    {
        const string json = """
        {
          "name": "Test Haster",
          "types": ["Creature"],
          "subtypes": ["Goblin"],
          "manaCost": "{R}",
          "power": 2,
          "toughness": 1,
          "keywords": ["Haste", "Trample"]
        }
        """;

        var definition = CardDefinitionLoader.FromJson(json);
        var card = (Creature)CardDefinitionFactory.Build(definition, _alice);

        var markers = card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();
        markers.Should().BeEquivalentTo(new[] { "Haste", "Trample" });

        // The combat subsystem reads the printed marker directly (CR 702.10 /
        // CR 702.19) — so the keyword line is fully wired, not just metadata.
        CombatAbilities.HasHaste(card).Should().BeTrue();
        CombatAbilities.HasTrample(card).Should().BeTrue();
    }

    [Fact]
    public void Keywords_RoundTripThroughCardDef()
    {
        const string json = """
        {
          "name": "Test Flyer",
          "types": ["Creature"],
          "manaCost": "{1}{U}",
          "power": 1,
          "toughness": 1,
          "keywords": ["Flying"]
        }
        """;

        var def = CardDefinitionLoader.FromJson(json).ToCardDef();

        def.Keywords.Should().ContainSingle().Which.Should().Be("Flying");
    }

    [Fact]
    public void DevoidPlusHaste_ColourlessBodyWithPrintedKeyword()
    {
        // Devoid (CR 702.114) is expressed as a "Devoid" entry in the
        // keywords array; the runtime stamps the colourless flag so the {2}{R}
        // body is colourless despite its red pip. Pairing it with a printed
        // Haste line is exactly the Eldrazi-Obligator residual the deferral
        // named: colourless body + Haste marker.
        const string json = """
        {
          "name": "Test Devoid Haster",
          "types": ["Creature"],
          "subtypes": ["Eldrazi"],
          "manaCost": "{2}{R}",
          "power": 3,
          "toughness": 1,
          "keywords": ["Devoid", "Haste"]
        }
        """;

        var definition = CardDefinitionLoader.FromJson(json);
        var card = (Creature)CardDefinitionFactory.Build(definition, _alice);

        // Devoid: no colour despite the {R} pip (CR 105.2c / CR 702.114a).
        CardColors.GetColors(card).Should().BeEmpty();
        CombatAbilities.HasHaste(card).Should().BeTrue();
    }

    [Fact]
    public void NoKeywords_StampsNoMarkers()
    {
        const string json = """
        {
          "name": "Test Vanilla",
          "types": ["Creature"],
          "manaCost": "{G}",
          "power": 1,
          "toughness": 1
        }
        """;

        var definition = CardDefinitionLoader.FromJson(json);
        var card = (Creature)CardDefinitionFactory.Build(definition, _alice);

        card.Abilities.OfType<KeywordAbility>().Should().BeEmpty();
    }
}
