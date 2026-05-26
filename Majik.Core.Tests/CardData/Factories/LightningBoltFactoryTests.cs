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
/// </summary>
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
    public void LightningBolt_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lightning Bolt", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Lightning Bolt");
        card.HasType(CardType.Instant).Should().BeTrue();
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
        // 0/4 so 3 damage is not lethal — verifies damage marker is
        // applied without SBA wipe interfering.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = LightningBoltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { bear },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        bear.Damage.Should().Be(3, "Lightning Bolt deals 3 damage to target creature");
    }
}
