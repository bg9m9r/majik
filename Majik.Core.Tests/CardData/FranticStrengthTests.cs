using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="FranticStrengthFactory"/>.
///
/// Card: Frantic Strength — Enchantment — Aura {2}{G} (Bloomburrow).
///   "Flash"
///   "Enchant creature"
///   "Enchanted creature gets +2/+2 and has trample."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: {2}{G}, Enchantment — Aura.
///   - Flash keyword marker (CR 702.8 — castable at instant speed).
///   - Static +2/+2 boost (CR 613 Layer 7c) + granted Trample (CR 702.19).
///   - Boost is inert while unattached.
///   - "Enchant creature" cast-time target predicate filters non-creatures.
///
/// (Dispatch + well-formedness are asserted automatically for every
/// implemented card by CardFactoryContractTests.)
/// </summary>
[Trait("Color", "G")]
public class FranticStrengthTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FranticStrength_Identity()
    {
        var c = FranticStrengthFactory.Create(_alice);

        c.Name.Should().Be("Frantic Strength");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Flash (CR 702.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void FranticStrength_HasFlashKeyword()
    {
        var c = FranticStrengthFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => string.Equals(k.Keyword, "Flash", System.StringComparison.OrdinalIgnoreCase))
            .Should().BeTrue("Frantic Strength has Flash and can be cast at instant speed");
    }

    // -----------------------------------------------------------------------
    // Static +2/+2 boost + Trample grant (CR 613)
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_Boost_PumpsPlus2Plus2_AndGrantsTrample()
    {
        var effects = new ContinuousEffectsService();
        var aura = FranticStrengthFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 2, "+2/+2 from Frantic Strength");
        chars.Toughness.Should().Be(2 + 2, "+2/+2 from Frantic Strength");
        chars.Keywords.Should().Contain("Trample");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = FranticStrengthFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Trample");
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate (CR 702.5b / 303.4c)
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var aura = FranticStrengthFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Forest");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = FranticStrengthFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
