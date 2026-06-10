using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BlazingRootwallaFactory"/> (Torment / many reprints).
///
/// Creature — Lizard 1/1, mana cost {R}. Oracle text (verified against
/// Scryfall 2026-06-10):
///   "{R}: This creature gets +2/+0 until end of turn. Activate only once
///    each turn.
///    Madness {0}"
///
/// Madness is intrinsic (CR 702.35) — handled by
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> + the discard funnel — so
/// these tests only cover the card's UNIQUE non-madness body: the
/// {R}: +2/+0-until-EOT firebreathing self-pump and its "only once each turn"
/// restriction (CR 602.5e). Same self-pump shape as
/// <see cref="WallOfFireFactory"/> (PumpUntilEndOfTurnEffect on
/// <see cref="Creature.ActiveEffects"/>); same once-per-turn lock as
/// <see cref="HiredClawFactory"/>.
/// </summary>
[Trait("Color", "R")]
public class BlazingRootwallaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazingRootwalla_Identity()
    {
        var card = BlazingRootwallaFactory.Create(_alice);

        card.Name.Should().Be("Blazing Rootwalla");
        card.ManaCost.Should().Be("{R}");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Subtypes.Should().ContainSingle()
            .Which.Should().Be(CardSubtype.Lizard);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {R}: +2/+0 until end of turn — ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazingRootwalla_HasExactlyOnePumpAbility_CostOneRed_NoTargets()
    {
        var card = BlazingRootwallaFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Should().ContainSingle()
            .Which;
        var cost = pump.Costs.OfType<ManaCostCost>().Single();
        cost.Cost.Red.Should().Be(1, "activation cost is exactly one red mana");
        cost.Cost.Generic.Should().Be(0, "no generic component in {R}");
        pump.TargetRequests.Should().BeNullOrEmpty(
            "the pump affects Blazing Rootwalla itself — no targets");
    }

    // -----------------------------------------------------------------------
    // {R}: +2/+0 until end of turn — resolution + expiry (CR 613.1f / 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazingRootwalla_ActivatingPump_GivesPlusTwoPlusZero()
    {
        var svc = new ContinuousEffectsService();
        var card = BlazingRootwallaFactory.Create(_alice);
        card.ActiveEffects = svc;

        card.GetPower().Should().Be(1);
        card.GetToughness().Should().Be(1);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();

        card.GetPower().Should().Be(3,
            "{R}: +2/+0 until EOT — power increases by 2 (Layer 7c)");
        card.GetToughness().Should().Be(1,
            "+2/+0 does NOT modify toughness");
    }

    [Fact]
    public void BlazingRootwalla_PumpEffect_ExpiresAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var card = BlazingRootwallaFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();
        card.GetPower().Should().Be(3, "pump is active");

        // CR 514.2 — cleanup step removes "until end of turn" effects.
        svc.ExpireEndOfTurn();

        card.GetPower().Should().Be(1,
            "PumpUntilEndOfTurnEffect expires at end of turn — power returns to 1");
        card.GetToughness().Should().Be(1);
    }

    [Fact]
    public void BlazingRootwalla_PumpEffect_NullActiveEffects_DoesNotThrow()
    {
        var card = BlazingRootwallaFactory.Create(_alice);

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        var resolve = () => { foreach (var e in pump.Effects) e.Execute(); };

        resolve.Should().NotThrow(
            "shape-only path with no ContinuousEffectsService is a silent no-op");
    }

    // -----------------------------------------------------------------------
    // "Activate only once each turn" (CR 602.5e)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazingRootwalla_OnceEachTurn_GateClosesAfterActivation()
    {
        var svc = new ContinuousEffectsService();
        var card = BlazingRootwallaFactory.Create(_alice);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        pump.CanActivateNow().Should().BeTrue(
            "first activation of the turn is permitted");

        foreach (var effect in pump.Effects) effect.Execute();

        pump.CanActivateNow().Should().BeFalse(
            "CR 602.5e — \"Activate only once each turn\": the gate closes after the first activation");
    }

    [Fact]
    public void BlazingRootwalla_OnceEachTurn_GateReopensOnTurnStart()
    {
        var svc = new ContinuousEffectsService();
        var bus = new EventBus();
        var card = BlazingRootwallaFactory.Create(_alice, bus);
        card.ActiveEffects = svc;

        var pump = card.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in pump.Effects) effect.Execute();
        pump.CanActivateNow().Should().BeFalse("gate closed this turn");

        // CR 500.1 — a new turn resets the once-per-turn lock.
        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        pump.CanActivateNow().Should().BeTrue(
            "the lock resets at the start of each turn");
    }
}
