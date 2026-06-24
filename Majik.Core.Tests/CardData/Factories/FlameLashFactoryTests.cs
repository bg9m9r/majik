using FluentAssertions;
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
/// Unit tests for <see cref="FlameLashFactory"/> (Onslaught, {3}{R}).
///
/// Flame Lash — Instant.
/// Oracle text (Scryfall-confirmed): "Flame Lash deals 4 damage to any target."
///
/// Same vanilla-burn shape as <see cref="LightningStrikeFactory"/>; only the
/// cost ({3}{R}) and payload (4 damage) differ. Dispatch + well-formedness are
/// covered for every implemented card by CardFactoryContractTests, so this
/// suite asserts only identity and the unique 4-damage payload.
///
/// Covers:
/// - Identity ({3}{R} Instant, name, owner/controller).
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 4 damage to a player target (CR 120.3).
/// - Resolve deals 4 damage to a creature target.
/// </summary>
[Trait("Color", "R")]
public class FlameLashFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void FlameLash_Identity_InstantAt3R()
    {
        var card = FlameLashFactory.Create(_alice);

        card.Name.Should().Be("Flame Lash");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{3}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FlameLash_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = FlameLashFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void FlameLash_Resolve_DealsFourDamageToPlayer()
    {
        var def = FlameLashFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(16, "Flame Lash deals 4 damage to any target (CR 120.3)");
    }

    [Fact]
    public void FlameLash_Resolve_DealsFourDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = FlameLashFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(4, "Flame Lash deals 4 damage to target creature");
    }
}
