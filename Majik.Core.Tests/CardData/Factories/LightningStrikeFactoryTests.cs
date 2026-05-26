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
/// Unit tests for <see cref="LightningStrikeFactory"/> (Magic 2015).
///
/// Covers:
/// - Identity ({1}{R} Instant).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 3 damage to a player target.
/// - Resolve body routes creature damage through
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// </summary>
public class LightningStrikeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LightningStrike_Identity_InstantAt1R()
    {
        var strike = LightningStrikeFactory.Create(_alice);

        strike.Name.Should().Be("Lightning Strike");
        strike.HasType(CardType.Instant).Should().BeTrue();
        strike.ManaCost.ToString().Should().Be("{1}{R}");
        strike.Owner.Should().BeSameAs(_alice);
        strike.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LightningStrike_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lightning Strike", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Lightning Strike");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void LightningStrike_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = LightningStrikeFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void LightningStrike_Resolve_DealsThreeDamageToPlayer()
    {
        var def = LightningStrikeFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(17, "Lightning Strike deals 3 damage to any target");
    }

    [Fact]
    public void LightningStrike_Resolve_DealsThreeDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = LightningStrikeFactory.BuildSpellDefinition(resolver: x => x);
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

        wall.Damage.Should().Be(3, "Lightning Strike deals 3 damage to target creature");
    }
}
