using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="SurgeAlternativeCost"/> (CR 702.115).
/// Shape mirror of <see cref="EvokeAlternativeCostTests"/>; focuses on
/// the per-turn legality gate (<see cref="SurgeAlternativeCost.IsLegalInContext"/>)
/// and the <see cref="Card.WasCastForSurge"/> resolve-stamp.
/// </summary>
public class SurgeAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CanCastFor_CardInHand_OwnedBySelf_True()
    {
        var ts = new TurnState();
        var bushwhacker = MakeCreatureInHand(_alice, "Reckless Bushwhacker", "{2}{R}", 2, 1);

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.CanCastFor(bushwhacker, _alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("R"));
    }

    [Fact]
    public void CanCastFor_CardNotInHand_False()
    {
        var ts = new TurnState();
        var bushwhacker = new Creature("Reckless Bushwhacker", "{2}{R}", 2, 1) { Owner = _alice };
        bushwhacker.SetZone(ZoneType.Battlefield);

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.CanCastFor(bushwhacker, _alice).Should().BeFalse();
    }

    [Fact]
    public void CanCastFor_CardOwnedByOpponent_False()
    {
        var ts = new TurnState();
        var bushwhacker = MakeCreatureInHand(_bob, "Reckless Bushwhacker", "{2}{R}", 2, 1);

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.CanCastFor(bushwhacker, _alice).Should().BeFalse();
    }

    [Fact]
    public void IsLegalInContext_NoPriorSpellCast_False()
    {
        // CR 702.115a — surge requires the caster (or teammate) to have
        // cast another spell this turn. Empty TurnState → no spells cast
        // yet → gate refuses.
        var ts = new TurnState();
        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.IsLegalInContext(_alice).Should().BeFalse();
    }

    [Fact]
    public void IsLegalInContext_AfterAlicCastsASpell_True()
    {
        // Alice already cast one spell this turn (any colour). Surge gate
        // unlocks for the next spell Alice announces.
        var ts = new TurnState();
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.IsLegalInContext(_alice).Should().BeTrue();
    }

    [Fact]
    public void IsLegalInContext_OnlyOpponentCastASpell_False_NoTeammatesInV1()
    {
        // CR 702.115a — strictly "you or a teammate". v1 has no team
        // modelling, so an opponent's prior cast does NOT unlock the
        // caster's surge cost.
        var ts = new TurnState();
        ts.RecordSpellCast(_bob, new HashSet<ManaColor> { ManaColor.Blue });

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.IsLegalInContext(_alice).Should().BeFalse();
    }

    [Fact]
    public void IsLegalInContext_NullCaster_False()
    {
        var ts = new TurnState();
        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.IsLegalInContext(null!).Should().BeFalse();
    }

    [Fact]
    public void OnResolved_StampsWasCastForSurgeOnCard()
    {
        var ts = new TurnState();
        var bushwhacker = MakeCreatureInHand(_alice, "Reckless Bushwhacker", "{2}{R}", 2, 1);

        bushwhacker.WasCastForSurge.Should().BeFalse();

        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);
        cost.OnResolved(bushwhacker, _alice);

        bushwhacker.WasCastForSurge.Should().BeTrue();
    }

    [Fact]
    public void Description_FormatsSurgeAndCost()
    {
        var ts = new TurnState();
        var cost = new SurgeAlternativeCost(ManaCost.Parse("R"), ts);

        cost.Description.Should().Contain("Surge");
        cost.Description.Should().Contain("R");
    }

    [Fact]
    public void Constructor_NullCost_Throws()
    {
        var ts = new TurnState();
        var act = () => new SurgeAlternativeCost(null!, ts);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullTurnState_Throws()
    {
        var act = () => new SurgeAlternativeCost(ManaCost.Parse("R"), null!);
        act.Should().Throw<ArgumentNullException>();
    }

    private static Creature MakeCreatureInHand(Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}
