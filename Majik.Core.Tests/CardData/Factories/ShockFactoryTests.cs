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
/// Unit tests for <see cref="ShockFactory"/> (Mirage).
///
/// Covers:
/// - Identity ({R} Instant, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 2 damage to a player target.
/// - Resolve body routes creature damage through
///   <see cref="Primitives.Fx.DealDamageAny"/>.
/// </summary>
public class ShockFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Shock_Identity_InstantAtR()
    {
        var shock = ShockFactory.Create(_alice);

        shock.Name.Should().Be("Shock");
        shock.HasType(CardType.Instant).Should().BeTrue();
        shock.ManaCost.ToString().Should().Be("{R}");
        shock.Owner.Should().BeSameAs(_alice);
        shock.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Shock_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Shock", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Shock");
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    [Fact]
    public void Shock_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = ShockFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Shock_Resolve_DealsTwoDamageToPlayer()
    {
        var def = ShockFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(18, "Shock deals 2 damage to any target");
    }

    [Fact]
    public void Shock_Resolve_DealsTwoDamageToCreature()
    {
        // Use a 0/3 creature so 2 damage is not lethal — verifies damage
        // marker is applied without SBA wipe interfering.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 0, 3,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Bear });
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = ShockFactory.BuildSpellDefinition(resolver: x => x);
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

        bear.Damage.Should().Be(2, "Shock deals 2 damage to target creature");
    }
}
