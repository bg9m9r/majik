using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Snakeskin Veil (Kaldheim, {G}, Instant).
///
/// Oracle text (verified against the embedded Modern seed):
///   "Put a +1/+1 counter on target creature you control. It gains hexproof
///    until end of turn. (It can't be the target of spells or abilities your
///    opponents control.)"
///
/// Covers the card's UNIQUE behaviour:
///   - Resolve places one +1/+1 counter (CR 122) and grants Hexproof until
///     end of turn (CR 702.11).
///   - End-of-turn cleanup lifts Hexproof (CR 514.2) — the counter persists.
///   - Illegal target (creature the caster does not control) → no-op
///     (CR 109.5 / 608.2b).
///
/// Plumbing (dispatch + well-formedness) is owned by CardFactoryContractTests.
/// </summary>
[Trait("Color", "G")]
public class SnakeskinVeilFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── +1/+1 counter + Hexproof grant ───────────────────────────────────

    [Fact]
    public void Resolve_PlacesCounter_AndGrantsHexproof()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        bear.HasEffectiveKeyword(SnakeskinVeilFactory.GrantedHexproof).Should().BeFalse();

        ExecuteResolve(bear);

        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 122 — Snakeskin Veil puts one +1/+1 counter on the target");
        bear.GetPower().Should().Be(3, "the +1/+1 counter raises power by 1");
        bear.GetToughness().Should().Be(3, "the +1/+1 counter raises toughness by 1");
        bear.HasEffectiveKeyword(SnakeskinVeilFactory.GrantedHexproof).Should().BeTrue(
            "CR 702.11 — Snakeskin Veil grants Hexproof until end of turn");
    }

    [Fact]
    public void Resolve_EndOfTurnCleanup_LiftsHexproof_CounterPersists()
    {
        var continuous = new ContinuousEffectsService();
        var bear = BuildCreature(continuous, _alice, power: 2, toughness: 2);

        ExecuteResolve(bear);
        bear.HasEffectiveKeyword(SnakeskinVeilFactory.GrantedHexproof).Should().BeTrue();

        // CR 514.2 — EOT-flagged effects expire at cleanup.
        continuous.ExpireEndOfTurn();

        bear.HasEffectiveKeyword(SnakeskinVeilFactory.GrantedHexproof).Should().BeFalse(
            "Hexproof is granted only until end of turn");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "the +1/+1 counter is a permanent change — it does not expire at cleanup");
        bear.GetPower().Should().Be(3);
    }

    [Fact]
    public void Resolve_TargetNotControlledByCaster_IsNoOp()
    {
        // Bob's creature — not "you control" from Alice's perspective.
        var continuous = new ContinuousEffectsService();
        var bobBear = BuildCreature(continuous, _bob, power: 2, toughness: 2);

        ExecuteResolve(bobBear);

        bobBear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "CR 109.5 / 608.2b — not controlled by the caster → no-op");
        bobBear.HasEffectiveKeyword(SnakeskinVeilFactory.GrantedHexproof).Should().BeFalse();
    }

    // ── Identity (non-vanilla cost) ───────────────────────────────────────

    [Fact]
    public void SnakeskinVeil_Identity()
    {
        var c = SnakeskinVeilFactory.Create(_alice);

        c.Name.Should().Be("Snakeskin Veil");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private void ExecuteResolve(Creature target)
    {
        var def = SnakeskinVeilFactory.BuildSpellDefinition(_alice, resolver: t => t);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { target } },
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }

    private Creature BuildCreature(
        ContinuousEffectsService continuous,
        Player controller,
        int power,
        int toughness)
    {
        var c = new Creature($"{power}/{toughness} Bear", "{G}", power, toughness)
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = continuous,
        };
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
