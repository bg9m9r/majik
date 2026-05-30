using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FlameJavelinFactory"/> (Shadowmoor,
/// {(2/R)}{(2/R)}{(2/R)}).
///
/// Flame Javelin — Instant.
/// Oracle text (verified against Scryfall):
///   "({2/R} can be paid with any two mana or with {R}. This card's mana
///    value is 6.)
///    Flame Javelin deals 4 damage to any target."
///
/// Covers:
/// - Identity ({2/R}{2/R}{2/R} Instant, name, owner/controller) loaded from
///   the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - Mana value 6 — each monocolored-hybrid pip takes its higher generic
///   alternative of 2 (CR 202.3f), so TotalValue = 6.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 4 damage to a player target (CR 120.3).
/// - Resolve deals 4 damage to a creature target.
/// - Resolve removes loyalty from a planeswalker target (CR 306.7).
/// </summary>
public class FlameJavelinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void FlameJavelin_Identity_InstantAtTwobridRRR()
    {
        var card = FlameJavelinFactory.Create(_alice);

        card.Name.Should().Be("Flame Javelin");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2/R}{2/R}{2/R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlameJavelin_ManaValue_IsSix()
    {
        // CR 202.3f — each {2/R} monocolored-hybrid pip counts its higher
        // generic alternative (2) toward mana value; 3 pips → mana value 6.
        var card = FlameJavelinFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(6,
            "each {2/R} pip contributes its generic alternative of 2 (CR 202.3f)");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_FlameJavelin()
    {
        var card = NamedCardFactory.Create("Flame Javelin", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Flame Javelin");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void FlameJavelin_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = FlameJavelinFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void FlameJavelin_Resolve_DealsFourDamageToPlayer()
    {
        var def = FlameJavelinFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(16, "Flame Javelin deals 4 damage to any target (CR 120.3)");
    }

    [Fact]
    public void FlameJavelin_Resolve_DealsFourDamageToCreature()
    {
        // 0/5 creature so 4 damage is not lethal — verifies the damage marker
        // is applied without an SBA wipe interfering.
        var wall = new Creature("Wall of Wood", "{G}", 0, 5,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = FlameJavelinFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(4, "Flame Javelin deals 4 damage to target creature");
    }

    [Fact]
    public void FlameJavelin_Resolve_RemovesLoyaltyFromPlaneswalker()
    {
        // CR 306.7 — damage to a planeswalker becomes loyalty removal.
        // Fx.DealDamageAny routes the planeswalker branch: 4 damage to a
        // 4-loyalty walker leaves it at 0.
        var walker = new Planeswalker("Test Walker", "{2}{B}", 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Liliana });
        walker.SetOwner(_bob);
        walker.SetController(_bob);
        walker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(walker);

        var def = FlameJavelinFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { walker } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        walker.Loyalty.Should().Be(0,
            "Flame Javelin to a 4-loyalty planeswalker removes 4 loyalty counters (CR 306.7)");
    }
}
