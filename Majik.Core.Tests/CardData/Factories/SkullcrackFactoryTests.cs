using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkullcrackFactory"/> (Gatecrash, {1}{R}).
///
/// Oracle text:
///   "Players can't gain life this turn. Damage can't be prevented this turn.
///    Skullcrack deals 3 damage to target player or planeswalker."
///
/// Covers:
/// - Identity ({1}{R} Instant).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target player or planeswalker".
/// - Resolve body deals 3 damage to a player target.
/// - Resolve body routes planeswalker damage through loyalty removal (CR 306.7).
/// - No-op when target is a creature (CR 608.2b — only player/planeswalker legal).
/// - Life-gain rider: subsequent GainLife attempts are zeroed this turn
///   when a <see cref="ReplacementBus"/> is supplied.
/// - No life-gain prevention when no bus is supplied (shape-only path).
/// </summary>
[Trait("Color", "R")]
public class SkullcrackFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullcrack_Identity_InstantAtOneR()
    {
        var card = SkullcrackFactory.Create(_alice);

        card.Name.Should().Be("Skullcrack");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.ManaCostValue.TotalValue.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // SpellDefinition shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullcrack_SpellDefinition_HasPlayerOrPlaneswalkerRequest()
    {
        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target player or planeswalker");
        def.HasVariableX.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Resolve body — damage
    // -----------------------------------------------------------------------

    [Fact]
    public void Skullcrack_Resolve_DealsThreeDamageToPlayer()
    {
        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(17, "Skullcrack deals 3 damage to target player");
    }

    [Fact]
    public void Skullcrack_Resolve_DealsThreeDamageToPlaneswalker_ViaLoyaltyRemoval()
    {
        var pw = new Planeswalker("Chandra, Torch of Defiance", "{2}{R}{R}", 4,
            Array.Empty<CardSupertype>(),
            new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { pw },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        // CR 306.7 — damage to a planeswalker removes loyalty counters.
        pw.Loyalty.Should().Be(1,
            "Skullcrack deals 3 damage → planeswalker loses 3 loyalty (4−3=1)");
        _bob.LifeTotal.Should().Be(20,
            "damage to a planeswalker does not reduce its controller's life total");
    }

    [Fact]
    public void Skullcrack_Resolve_NoOpsOnCreatureTarget_CR608_2b()
    {
        // Skullcrack targets only player or planeswalker (not creatures).
        // If a creature somehow ends up as the resolved target (illegal
        // targeting but CR 608.2b says do as much as possible), the
        // damage should not apply — DealDamageAny on a Creature would
        // incorrectly deal damage. The factory dispatches to DealDamageAny
        // which routes to OracleSpellBinder.DealDamage for non-planeswalker
        // targets; creature damage would go through that path.
        //
        // This test documents the CR 608.2b expectation: a creature is not
        // a legal target; if passed, the effect no-ops (factory guards).
        var hippo = new Creature("Watchwolf", "{G}{W}", 3, 3);
        hippo.SetOwner(_bob);
        hippo.SetController(_bob);

        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { hippo },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        hippo.Damage.Should().Be(0,
            "Skullcrack only deals damage to players and planeswalkers — creature is no-op (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20, "Bob takes no damage from the creature no-op");
    }

    // -----------------------------------------------------------------------
    // Life-gain prevention rider ("Players can't gain life this turn")
    // -----------------------------------------------------------------------

    [Fact]
    public void LifeGainReplacement_BlocksGainLife_WhenBusSupplied()
    {
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        _bob.AttachReplacementBus(bus);

        // Build spell definition with bus — registers the no-lifegain rider.
        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x, replacements: bus);

        // Resolve targeting Bob.
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        var aliceLifeAfterDamage = _alice.LifeTotal; // untouched
        var bobLifeAfterDamage = _bob.LifeTotal;     // 17

        // Now attempt life gain — should be zeroed by the replacement.
        _alice.GainLife(5);
        _bob.GainLife(7);

        _alice.LifeTotal.Should().Be(aliceLifeAfterDamage,
            "Skullcrack's 'players can't gain life this turn' zeroes Alice's gain");
        _bob.LifeTotal.Should().Be(bobLifeAfterDamage,
            "Skullcrack's 'players can't gain life this turn' zeroes Bob's gain");
    }

    [Fact]
    public void LifeGainReplacement_NotRegistered_WhenNoBusSupplied()
    {
        // No bus → shape-only path; GainLife proceeds normally.
        var def = SkullcrackFactory.BuildSpellDefinition(resolver: x => x);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        var aliceLifeBefore = _alice.LifeTotal;
        _alice.GainLife(5);

        _alice.LifeTotal.Should().Be(aliceLifeBefore + 5,
            "no bus attached → no replacement runs; gain proceeds");
    }
}
