using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MoxAmberFactory"/>.
///
/// Mox Amber — Legendary Artifact {0}.
/// "{T}: Add one mana of any color among legendary creatures and
///  planeswalkers you control."
///
/// Covers:
/// - Card identity (Legendary Artifact, mana cost {0}).
/// - NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG).
/// - Inactive when no legendary creatures/planeswalkers are controlled.
/// - Active per-colour by a controlled legendary creature whose printed
///   cost contains that colour; other colours stay gated.
/// - Active by a controlled legendary planeswalker.
/// - Opponent legendaries do not count.
/// - Non-legendary creature of matching colour does not count.
/// - Tap gate after activation.
/// </summary>
public class MoxAmberTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void MoxAmber_IsLegendaryArtifact_ZeroCost()
    {
        var mox = MoxAmberFactory.Create(_alice);

        mox.Name.Should().Be("Mox Amber");
        mox.HasType(CardType.Artifact).Should().BeTrue("Mox Amber is an Artifact");
        mox.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Mox Amber is Legendary");
        mox.ManaCost.Should().Be("{0}");
        mox.Owner.Should().BeSameAs(_alice);
        mox.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MoxAmber()
    {
        var card = NamedCardFactory.Create("Mox Amber", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mox Amber");
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void MoxAmber_HasFiveManaAbilities_OnePerColor()
    {
        var mox = MoxAmberFactory.Create(_alice);
        var mas = mox.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Gate — off
    // --------------------------------------------------------------

    [Fact]
    public void MoxAmber_CannotActivate_WithNoLegendaries()
    {
        var mox = MoxAmberFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // Mox Amber itself is a Legendary Artifact, but the printed
        // ability looks for "legendary creatures and planeswalkers" — an
        // artifact doesn't qualify.
        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "no legendary creature or planeswalker on the controller's battlefield");
        }
    }

    [Fact]
    public void MoxAmber_NonLegendaryCreature_DoesNotCount()
    {
        var mox = MoxAmberFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // Non-legendary red creature — colour matches but it's not legendary.
        var grizzly = new Creature("Random Red Creature", "{R}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(grizzly);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "the creature lacks the Legendary supertype — Mox Amber stays gated");
        }
    }

    // --------------------------------------------------------------
    // Gate — on (per colour)
    // --------------------------------------------------------------

    [Fact]
    public void MoxAmber_LegendaryRedCreature_OnlyRedManaUnlocked()
    {
        var mox = MoxAmberFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // Legendary red creature — cost {R}{R} → colour set { Red }.
        var legend = new Creature(
            "Random Legendary Red",
            "{R}{R}",
            2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(_alice);
        legend.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(legend);

        var mas = mox.Abilities.OfType<ManaAbility>().ToList();
        var red = mas.Single(m => m.ManaGenerated.Red == 1);
        red.CanActivate().Should().BeTrue("a legendary red creature unlocks the red ability");

        // The other four colours stay gated.
        foreach (var ma in mas.Where(m => m.ManaGenerated.Red == 0))
        {
            ma.CanActivate().Should().BeFalse(
                "only the matching colour is unlocked — other ManaAbility instances stay gated");
        }

        // Activate the red one and verify mana + tap.
        var produced = red.Activate();
        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        mox.IsTapped.Should().BeTrue("activating tapped Mox Amber");

        // After tapping, all five abilities are no longer activatable
        // (tap gate, independent of the legendary gate).
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse("Mox Amber is tapped");
        }
    }

    [Fact]
    public void MoxAmber_LegendaryPlaneswalker_UnlocksItsColors()
    {
        var mox = MoxAmberFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // Legendary planeswalker — cost {1}{B}{G} → colour set { Black, Green }.
        // Planeswalker has the Legendary supertype baked into the type-line
        // by Magic convention, but the engine's Planeswalker ctor does NOT
        // assume it (mirrors Grist / Liliana — supertypes passed explicitly).
        var pw = new Planeswalker(
            "Random Legendary PW",
            "{1}{B}{G}",
            3,
            supertypes: new[] { CardSupertype.Legendary });
        pw.SetOwner(_alice);
        pw.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(pw);

        var mas = mox.Abilities.OfType<ManaAbility>().ToList();
        mas.Single(m => m.ManaGenerated.Black == 1).CanActivate().Should().BeTrue(
            "the legendary planeswalker contributes Black");
        mas.Single(m => m.ManaGenerated.Green == 1).CanActivate().Should().BeTrue(
            "the legendary planeswalker contributes Green");

        // Colours not on the planeswalker stay gated.
        mas.Single(m => m.ManaGenerated.White == 1).CanActivate().Should().BeFalse();
        mas.Single(m => m.ManaGenerated.Blue == 1).CanActivate().Should().BeFalse();
        mas.Single(m => m.ManaGenerated.Red == 1).CanActivate().Should().BeFalse();
    }

    // --------------------------------------------------------------
    // Opponent legendaries don't count
    // --------------------------------------------------------------

    [Fact]
    public void MoxAmber_OpponentsLegendariesDoNotCount()
    {
        var mox = MoxAmberFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mox);

        // Bob controls a legendary blue creature — Alice's Mox Amber
        // must not see it.
        var bobLegend = new Creature(
            "Bob's Legendary Blue",
            "{U}{U}",
            2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        bobLegend.SetOwner(_bob);
        bobLegend.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bobLegend);

        foreach (var ma in mox.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "opponent legendaries do not count toward Alice's Mox Amber");
        }
    }
}
