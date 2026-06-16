using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="HellMongrelFactory"/> (Shadows over Innistrad).
///
/// Creature — Nightmare Dog 4/3, mana cost {3}{B}. Oracle text (verified
/// against Scryfall 2026-06-16):
///   "Discard a card: This creature gets +1/+1 until end of turn.
///    Madness {2}{B}"
///
/// Madness is intrinsic (CR 702.35) — handled by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> + the discard funnel — so
/// these tests only cover the card's UNIQUE non-madness body: the
/// "discard a card: +1/+1 until EOT" self-pump (CR 602 / CR 613.1f). The cost
/// is the same <see cref="DiscardACardCost"/> Psychic Frog uses; the pump is
/// the same <see cref="PumpUntilEndOfTurnEffect"/> shape as Blazing Rootwalla.
/// </summary>
[Trait("Color", "B")]
public class HellMongrelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HellMongrel_Identity()
    {
        var card = HellMongrelFactory.Create(_alice);

        card.Name.Should().Be("Hell Mongrel");
        card.ManaCost.Should().Be("{3}{B}");
        card.BasePower.Should().Be(4);
        card.BaseToughness.Should().Be(3);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().BeEquivalentTo(
            new[] { CardSubtype.Nightmare, CardSubtype.Dog });
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Discard a card: +1/+1 until end of turn — ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void HellMongrel_HasExactlyOnePumpAbility_CostDiscardACard_NoMana_NoTargets()
    {
        var card = HellMongrelFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle()
            .Which;
        pump.Costs.Should().ContainSingle(
            "the sole activation cost is \"discard a card\" — no mana");
        pump.Costs.Single().Should().BeOfType<DiscardACardCost>();
        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the activation cost is purely a discard — no mana component");
        pump.TargetRequests.Should().BeNullOrEmpty(
            "the pump affects Hell Mongrel itself — no targets");
    }

    // -----------------------------------------------------------------------
    // Resolution + expiry (CR 613.1f / 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void HellMongrel_ActivatingPump_GivesPlusOnePlusOne()
    {
        var svc = new ContinuousEffectsService();
        var card = HellMongrelFactory.Create(_alice);
        card.ActiveEffects = svc;

        card.GetPower().Should().Be(4);
        card.GetToughness().Should().Be(3);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(5,
            "discard a card: +1/+1 until EOT — power increases by 1 (Layer 7c)");
        card.GetToughness().Should().Be(4,
            "+1/+1 increases toughness by 1");
    }

    [Fact]
    public void HellMongrel_PumpEffect_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var card = HellMongrelFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();
        card.GetPower().Should().Be(5, "pump is active");
        card.GetToughness().Should().Be(4, "pump is active");

        // CR 514.2 — cleanup step removes "until end of turn" effects.
        svc.ExpireEndOfTurn();

        card.GetPower().Should().Be(4,
            "PumpUntilEndOfTurnEffect expires at end of turn — power returns to 4");
        card.GetToughness().Should().Be(3,
            "toughness returns to 3 at end of turn");
    }

    [Fact]
    public void HellMongrel_PumpEffect_Repeatable_StacksWithMultipleActivations()
    {
        var svc = new ContinuousEffectsService();
        var card = HellMongrelFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();

        // No printed once-per-turn rider — the pump is repeatable so long as
        // the controller has a card to discard. Two resolutions => +2/+2.
        foreach (var effect in pump.Effects) effect.Execute();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(6, "two activations stack to +2/+0 over base");
        card.GetToughness().Should().Be(5, "two activations stack to +0/+2 over base");
    }

    [Fact]
    public void HellMongrel_PumpEffect_NullActiveEffects_DoesNotThrow()
    {
        var card = HellMongrelFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        var resolve = () => { foreach (var e in pump.Effects) e.Execute(); };

        resolve.Should().NotThrow(
            "shape-only path with no ContinuousEffectsService is a silent no-op");
    }

    // -----------------------------------------------------------------------
    // Discard cost — payable only with a card in hand (CR 117.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void HellMongrel_DiscardCost_RequiresCardInHand()
    {
        var card = HellMongrelFactory.Create(_alice);
        var cost = card.Abilities.OfType<ActivatedAbility>().Single()
            .Costs.OfType<DiscardACardCost>().Single();

        cost.CanPay(_alice).Should().BeFalse(
            "CR 117.1 — \"discard a card\" cannot be paid with an empty hand");

        _alice.Zones.Hand.AddCard(HellMongrelFactory.Create(_alice));

        cost.CanPay(_alice).Should().BeTrue(
            "with a card in hand the discard cost is payable");
    }
}
