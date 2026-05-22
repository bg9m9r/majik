using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// Unit tests for <see cref="EvokeAlternativeCost"/> (CR 702.74 + CR 117.11).
/// Exercises both the pure-mana evoke (classic Lorwyn — Mulldrifter "Evoke
/// {3}{U}") and pitch-evoke (MH2 incarnation cycle — Solitude "Evoke—Exile
/// a white card from your hand") shapes.
/// </summary>
public class EvokeAlternativeCostTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Pure-mana evoke (Mulldrifter style) ──────────────────────────────────

    [Fact]
    public void PureMana_CanCastFor_CardInHand_OwnedBySelf_True()
    {
        var mulldrifter = MakeCreatureInHand(_alice, "Mulldrifter", "{4}{U}", power: 2, toughness: 2);
        var cost = new EvokeAlternativeCost(ManaCost.Parse("3U"));

        cost.CanCastFor(mulldrifter, _alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("3U"));
        cost.PitchColor.Should().BeNull();
    }

    [Fact]
    public void PureMana_OnResolved_SetsEvokeWasPaidOnCreature()
    {
        var mulldrifter = MakeCreatureInHand(_alice, "Mulldrifter", "{4}{U}", 2, 2);
        var cost = new EvokeAlternativeCost(ManaCost.Parse("3U"));

        cost.OnResolved(mulldrifter, _alice);

        mulldrifter.EvokeWasPaid.Should().BeTrue();
    }

    // ── Pitch evoke (Solitude style) ─────────────────────────────────────────

    [Fact]
    public void Pitch_CanCastFor_WhiteCardInHand_True()
    {
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        var pitch = MakeCreatureInHand(_alice, "Savannah Lions", "{W}", 2, 1);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);

        cost.CanCastFor(solitude, _alice).Should().BeTrue();
        cost.AlternativeManaCost.Should().Be(ManaCost.Zero);
        cost.PitchColor.Should().Be(ManaColor.White);
    }

    [Fact]
    public void Pitch_CanCastFor_NonWhitePitchCard_False()
    {
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        var blackPitch = MakeCreatureInHand(_alice, "Walking Corpse", "{1}{B}", 2, 2);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, blackPitch);

        cost.CanCastFor(solitude, _alice).Should().BeFalse();
    }

    [Fact]
    public void Pitch_CanCastFor_PitchCardOwnedByOpponent_False()
    {
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        var pitch = MakeCreatureInHand(_bob, "Savannah Lions", "{W}", 2, 1);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);

        cost.CanCastFor(solitude, _alice).Should().BeFalse();
    }

    [Fact]
    public void Pitch_CanCastFor_PitchCardSameAsSpell_False()
    {
        // Sanity: cannot pitch the spell itself to itself.
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, solitude);

        cost.CanCastFor(solitude, _alice).Should().BeFalse();
    }

    [Fact]
    public void Pitch_CanCastFor_SpellNotInHand_False()
    {
        // Evoke must be announced from hand (CR 601.2).
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        // Move spell out of hand to simulate Solitude in graveyard.
        _alice.Zones.Hand.RemoveCard(solitude);
        _alice.Zones.Graveyard.AddCard(solitude);
        solitude.SetZone(ZoneType.Graveyard);

        var pitch = MakeCreatureInHand(_alice, "Savannah Lions", "{W}", 2, 1);
        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);

        cost.CanCastFor(solitude, _alice).Should().BeFalse();
    }

    [Fact]
    public void Pitch_OnResolved_ExilesPitchedCard_AndSetsEvokeWasPaid()
    {
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        var pitch = MakeCreatureInHand(_alice, "Savannah Lions", "{W}", 2, 1);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);
        cost.OnResolved(solitude, _alice);

        solitude.EvokeWasPaid.Should().BeTrue();
        pitch.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(pitch);
        _alice.Zones.Hand.GetCards().Should().NotContain(pitch);
    }

    [Fact]
    public void Pitch_OnResolved_PitchAlreadyMoved_DoesNotThrow()
    {
        var solitude = MakeCreatureInHand(_alice, "Solitude", "{3}{W}{W}", 3, 2);
        var pitch = MakeCreatureInHand(_alice, "Savannah Lions", "{W}", 2, 1);
        // Pre-move the pitched card out of hand (rare interaction).
        _alice.Zones.Hand.RemoveCard(pitch);
        _alice.Zones.Graveyard.AddCard(pitch);
        pitch.SetZone(ZoneType.Graveyard);

        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);
        var act = () => cost.OnResolved(solitude, _alice);

        act.Should().NotThrow();
        solitude.EvokeWasPaid.Should().BeTrue(); // flag still flips
    }

    // ── Description ──────────────────────────────────────────────────────────

    [Fact]
    public void Description_PureMana_MentionsMana()
    {
        var cost = new EvokeAlternativeCost(ManaCost.Parse("3U"));
        cost.Description.Should().Contain("Evoke");
        cost.Description.Should().Contain("3U");
    }

    [Fact]
    public void Description_PitchOnly_MentionsExileColor()
    {
        var pitch = MakeCreatureInHand(_alice, "Savannah Lions", "{W}", 2, 1);
        var cost = new EvokeAlternativeCost(ManaCost.Zero, ManaColor.White, pitch);

        cost.Description.Should().Contain("Evoke");
        cost.Description.Should().Contain("White");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeCreatureInHand(Player owner, string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness) { Owner = owner };
        c.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(c);
        return c;
    }
}
