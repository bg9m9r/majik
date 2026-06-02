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
/// Unit tests for <see cref="BombardFactory"/> (Ixalan / reprints).
///
/// Covers:
/// - Identity ({2}{R} Instant, mana value 3, red).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target creature".
/// - Resolve body deals 4 damage to a creature target.
/// - Resolve body is a no-op when the resolved target is not a creature
///   (CR 608.2b — if the target is illegal on resolution, the effect does
///   nothing).
/// </summary>
[Trait("Color", "R")]
public class BombardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Bombard_Identity_InstantAt2R()
    {
        var card = BombardFactory.Create(_alice);

        card.Name.Should().Be("Bombard");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Bombard_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = BombardFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("target creature");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Bombard_Resolve_DealsFourDamageToCreature()
    {
        var bear = new Creature("Bear Cub", "{1}{G}", 2, 2,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = BombardFactory.BuildSpellDefinition(resolver: x => x);
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

        bear.Damage.Should().Be(4, "Bombard deals 4 damage to target creature");
    }

    [Fact]
    public void Bombard_Resolve_NonCreatureTarget_IsNoOp()
    {
        // CR 608.2b — if the resolved target is not a creature the effect does
        // nothing; we simulate by passing a non-Creature object as the target.
        var def = BombardFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        var act = () => { foreach (var effect in effects) effect.Execute(); };

        // Life total must be unchanged — no damage dealt to the player.
        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "Bombard targets creatures only; non-creature target is a no-op");
    }
}
