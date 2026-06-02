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
/// Unit tests for <see cref="CharFactory"/> (Portal Second Age / Tempest Remastered,
/// {2}{R}).
///
/// Oracle text:
///   "Char deals 4 damage to any target and 2 damage to you."
///
/// Covers:
/// - Identity ({2}{R} Instant, red colour identity).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve deals 4 damage to a player target AND 2 damage to the caster.
/// - Resolve deals 4 damage to a creature target AND 2 damage to the caster.
/// </summary>
[Trait("Color", "R")]
public class CharFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Char_Identity_InstantAt2R()
    {
        var card = CharFactory.Create(_alice);

        card.Name.Should().Be("Char");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Char_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = CharFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Char_Resolve_DealsFourDamageToTargetPlayerAndTwoToCaster()
    {
        var def = CharFactory.BuildSpellDefinition(_alice, resolver: x => x);
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

        _bob.LifeTotal.Should().Be(16, "Char deals 4 damage to the target player");
        _alice.LifeTotal.Should().Be(18, "Char deals 2 damage to the caster (you)");
    }

    [Fact]
    public void Char_Resolve_DealsFourDamageToTargetCreatureAndTwoToCaster()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = CharFactory.BuildSpellDefinition(_alice, resolver: x => x);
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

        wall.Damage.Should().Be(4, "Char deals 4 damage to the target creature");
        _alice.LifeTotal.Should().Be(18, "Char deals 2 damage to the caster (you)");
    }
}
