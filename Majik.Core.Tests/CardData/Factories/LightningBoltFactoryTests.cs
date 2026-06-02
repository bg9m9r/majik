using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="LightningBoltFactory"/> (Alpha).
///
/// Covers:
/// - Identity ({R} Instant, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 3 damage to a player target.
/// - Resolve body routes creature damage through
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// - Resolve body removes loyalty from a planeswalker target
///   (CR 306.7 — Bolt to a 3-loyalty Walker leaves it at 0).
/// </summary>
[Trait("Color", "R")]
public class LightningBoltFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LightningBolt_Identity_InstantAtR()
    {
        var bolt = LightningBoltFactory.Create(_alice);

        bolt.Name.Should().Be("Lightning Bolt");
        bolt.HasType(CardType.Instant).Should().BeTrue();
        bolt.ManaCost.ToString().Should().Be("{R}");
        bolt.Owner.Should().BeSameAs(_alice);
        bolt.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void LightningBolt_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = LightningBoltFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void LightningBolt_Resolve_DealsThreeDamageToPlayer()
    {
        var def = LightningBoltFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(17, "Lightning Bolt deals 3 damage to any target");
    }

    [Fact]
    public void LightningBolt_Resolve_DealsThreeDamageToCreature()
    {
        // Use a 0/4 creature so 3 damage is not lethal — verifies damage
        // marker is applied without SBA wipe interfering.
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = LightningBoltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { wall },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        wall.Damage.Should().Be(3, "Lightning Bolt deals 3 damage to target creature");
    }

    [Fact]
    public void LightningBolt_Resolve_RemovesLoyaltyFromPlaneswalker()
    {
        // CR 306.7 — damage to a planeswalker becomes loyalty removal.
        // Fx.DealDamageAny routes the planeswalker branch.
        var walker = new Planeswalker("Test Walker", "{2}{B}", 3,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Liliana });
        walker.SetOwner(_bob);
        walker.SetController(_bob);
        walker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(walker);

        var def = LightningBoltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { walker },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        walker.Loyalty.Should().Be(0,
            "Lightning Bolt to a 3-loyalty planeswalker removes 3 loyalty counters (CR 306.7)");
    }
}
