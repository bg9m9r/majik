using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DarksteelIngotFactory"/>.
///
/// Darksteel Ingot — Artifact {3}.
///   "Indestructible (Effects that say \"destroy\" don't destroy this artifact.)
///    {T}: Add one mana of any color."
///
/// Covers:
/// - Card identity (Artifact, mana cost {3}).
/// - NamedCardFactory dispatch.
/// - Indestructible marker (CR 702.12) read via CombatAbilities.HasIndestructible.
/// - "Add one mana of any color" modeled as five ManaAbility instances
///   (one per WUBRG) — same shape as Mox Opal / City of Brass.
/// </summary>
public class DarksteelIngotTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void DarksteelIngot_IsArtifact_ThreeCost()
    {
        var ingot = DarksteelIngotFactory.Create(_alice);

        ingot.Name.Should().Be("Darksteel Ingot");
        ingot.HasType(CardType.Artifact).Should().BeTrue();
        ingot.ManaCost.Should().Be("{3}");
        ingot.Owner.Should().BeSameAs(_alice);
        ingot.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_DarksteelIngot()
    {
        var card = NamedCardFactory.Create("Darksteel Ingot", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Darksteel Ingot");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{3}");
    }

    // --------------------------------------------------------------
    // Indestructible (CR 702.12)
    // --------------------------------------------------------------

    [Fact]
    public void DarksteelIngot_HasIndestructibleMarker()
    {
        var ingot = DarksteelIngotFactory.Create(_alice);

        ingot.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Indestructible");
    }

    [Fact]
    public void DarksteelIngot_SurvivesDestroyEffect()
    {
        var ingot = DarksteelIngotFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ingot);
        ingot.SetZone(ZoneType.Battlefield);

        // CR 702.12b — a "destroy" effect can't destroy an indestructible
        // permanent. The destroy gate in OracleSpellBinder.MoveToGraveyard
        // reads the printed "Indestructible" KeywordAbility marker for
        // non-creature permanents (Fx.MoveToGraveyard with Destroy reason).
        Fx.MoveToGraveyard(ingot, ZoneMoveReason.Destroy);

        ingot.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(ingot);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(ingot);
    }

    // --------------------------------------------------------------
    // {T}: Add one mana of any color
    // --------------------------------------------------------------

    [Fact]
    public void DarksteelIngot_HasFiveManaAbilities_OnePerColor()
    {
        var ingot = DarksteelIngotFactory.Create(_alice);

        var manaAbilities = ingot.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5);

        // Each produces exactly one mana, covering W/U/B/R/G.
        manaAbilities.Should().OnlyContain(ma => ma.ManaGenerated.TotalValue == 1);

        var colors = new[]
        {
            ManaCost.Parse("W"),
            ManaCost.Parse("U"),
            ManaCost.Parse("B"),
            ManaCost.Parse("R"),
            ManaCost.Parse("G"),
        };

        foreach (var expected in colors)
        {
            manaAbilities.Should().Contain(
                ma => ma.ManaGenerated.ToString() == expected.ToString(),
                $"Darksteel Ingot can produce {expected}");
        }
    }
}
