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
/// Unit tests for <see cref="SkewerTheCriticsFactory"/> (Ravnica Allegiance,
/// {2}{R}).
///
/// Sorcery. Oracle text:
///   "Spectacle {R} (You may cast this spell for its spectacle cost rather
///    than its mana cost if an opponent lost life this turn.)
///    Skewer the Critics deals 3 damage to any target."
///
/// Covers:
/// - Identity ({2}{R} Sorcery, mana value 3).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spectacle cost: returns non-null {R} when an opponent lost life this turn;
///   returns null otherwise; caster's own life loss does not enable it.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 3 damage to a player target.
/// - Resolve body deals 3 damage to a creature target.
/// </summary>
[Trait("Color", "R")]
public class SkewerTheCriticsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------

    [Fact]
    public void SkewerTheCritics_Identity_SorceryAt2R()
    {
        var card = SkewerTheCriticsFactory.Create(_alice);

        card.Name.Should().Be("Skewer the Critics");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.ManaCostValue.TotalValue.Should().Be(3, "mana value is 3");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -------------------------------------------------------------------
    // Spectacle alt cost
    // -------------------------------------------------------------------

    [Fact]
    public void Spectacle_OpponentLostLifeThisTurn_BindsCostR()
    {
        _bob.LoseLife(1);
        _bob.LifeLostThisTurn.Should().Be(1);

        var cost = SkewerTheCriticsFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().NotBeNull();
        cost!.AlternativeManaCost.Red.Should().Be(1, "Spectacle cost is {R}");
        cost.AlternativeManaCost.TotalValue.Should().Be(1);
    }

    [Fact]
    public void Spectacle_NoOpponentLostLifeThisTurn_ReturnsNull()
    {
        _bob.LifeLostThisTurn.Should().Be(0);

        var cost = SkewerTheCriticsFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().BeNull("Spectacle alt cost is illegal until an opponent loses life");
    }

    [Fact]
    public void Spectacle_CasterLostLife_DoesNotEnableSpectacle()
    {
        // Caster's own life loss does NOT enable Spectacle (CR 702.118a).
        _alice.LoseLife(3);

        var cost = SkewerTheCriticsFactory.BuildSpectacleCost(_alice, new[] { _alice, _bob });

        cost.Should().BeNull("Spectacle keys on an OPPONENT losing life, not the caster");
    }

    // -------------------------------------------------------------------
    // Spell definition shape
    // -------------------------------------------------------------------

    [Fact]
    public void SkewerTheCritics_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = SkewerTheCriticsFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // -------------------------------------------------------------------
    // Resolve — 3 damage to any target
    // -------------------------------------------------------------------

    [Fact]
    public void SkewerTheCritics_Resolve_DealsThreeDamageToPlayer()
    {
        var def = SkewerTheCriticsFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(17, "Skewer the Critics deals 3 damage to any target");
    }

    [Fact]
    public void SkewerTheCritics_Resolve_DealsThreeDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = SkewerTheCriticsFactory.BuildSpellDefinition(resolver: x => x);
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

        wall.Damage.Should().Be(3, "Skewer the Critics deals 3 damage to target creature");
    }
}
