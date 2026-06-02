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
/// Unit tests for <see cref="LavaAxeFactory"/> (Portal / many reprints, {4}{R}).
///
/// Covers:
/// - Identity ({4}{R} Sorcery, red).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "target player or planeswalker".
/// - Resolve body deals 5 damage to a player target.
/// - Resolve body deals 5 damage to a planeswalker target (loyalty removal — CR 306.7).
/// - Resolve body is a no-op when target is a creature (CR 608.2b).
/// </summary>
[Trait("Color", "R")]
public class LavaAxeFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void LavaAxe_Identity_SorceryAt4R()
    {
        var axe = LavaAxeFactory.Create(_alice);

        axe.Name.Should().Be("Lava Axe");
        axe.HasType(CardType.Sorcery).Should().BeTrue();
        axe.ManaCost.ToString().Should().Be("{4}{R}");
        axe.Owner.Should().BeSameAs(_alice);
        axe.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void LavaAxe_SpellDefinition_HasPlayerOrPlaneswalkerRequest()
    {
        var def = LavaAxeFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target player or planeswalker");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void LavaAxe_Resolve_DealsFiveDamageToPlayer()
    {
        var def = LavaAxeFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(15, "Lava Axe deals 5 damage to target player");
    }

    [Fact]
    public void LavaAxe_Resolve_DealsFiveDamageToPlaneswalker_ViaLoyaltyRemoval()
    {
        var pw = new Planeswalker("Chandra Torch of Defiance", "{2}{R}{R}", 4,
            Array.Empty<CardSupertype>(),
            new[] { CardSubtype.Chandra });
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        var def = LavaAxeFactory.BuildSpellDefinition(resolver: x => x);
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

        // CR 306.7 — damage to a planeswalker is dealt as loyalty removal.
        // 4 loyalty − 5 = -1 → clamped to 0 by SBAs, but RemoveLoyalty itself
        // should yield a negative or zero value; test for ≤ 0.
        pw.Loyalty.Should().BeLessOrEqualTo(0,
            "Lava Axe deals 5 damage to target planeswalker as loyalty removal (CR 306.7)");
        _bob.LifeTotal.Should().Be(20,
            "damage to a planeswalker does not reduce its controller's life total");
    }

    [Fact]
    public void LavaAxe_Resolve_NoOp_WhenTargetIsCreature()
    {
        // CR 608.2b — "target player or planeswalker" excludes creatures;
        // if somehow a creature ends up as the resolved target (e.g. Spellskite
        // redirect) the effect does nothing.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = LavaAxeFactory.BuildSpellDefinition(resolver: x => x);
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

        bear.Damage.Should().Be(0, "Lava Axe must not damage a creature (CR 608.2b)");
        _bob.LifeTotal.Should().Be(20, "player life total must be unchanged");
    }
}
