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
/// Unit tests for <see cref="SunlanceFactory"/> (Coldsnap).
///
/// Oracle text: "Sunlance deals 3 damage to target nonwhite creature." ({W} Sorcery.)
///
/// Covers:
/// - Identity ({W} Sorcery, white).
/// - Spell definition shape: 1..1 "target nonwhite creature" request.
/// - Resolve body deals 3 damage to a nonwhite target creature (CR 119.2).
/// - Resolve body is a no-op when the target is a white creature
///   (CR 608.2b — illegal-target filter at resolution; CR 105 — colour).
/// - Resolve body is a no-op when the target is not a creature (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class SunlanceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature CreatureOnBattlefield(Player owner, string manaCost, int power, int tough)
    {
        var c = new Creature("Test Creature", manaCost, power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void Sunlance_Identity_SorceryAtW()
    {
        var card = SunlanceFactory.Create(_alice);

        card.Name.Should().Be("Sunlance");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{W}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sunlance_SpellDefinition_HasSingleNonwhiteTargetCreatureRequest()
    {
        var def = SunlanceFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonwhite");
        req.Description.Should().Contain("creature");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Sunlance_Resolve_DealsThreeDamageToNonwhiteCreature()
    {
        // {1}{G} creature — no white pip, so it is nonwhite (CR 105).
        var target = CreatureOnBattlefield(_bob, "{1}{G}", 5, 5);

        var def = SunlanceFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { target },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        target.Damage.Should().Be(3, "Sunlance deals 3 damage to target nonwhite creature");
    }

    [Fact]
    public void Sunlance_Resolve_NoOp_OnWhiteCreature()
    {
        // CR 105 — a {W} creature is white, so it is an illegal target. If a
        // white creature slips through the resolver, the effect skips it
        // (CR 608.2b).
        var white = CreatureOnBattlefield(_bob, "{1}{W}", 4, 4);

        var def = SunlanceFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { white },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        white.Damage.Should().Be(0, "Sunlance cannot target or damage a white creature");
    }

    [Fact]
    public void Sunlance_Resolve_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — if a spell's only target becomes illegal, the spell does
        // nothing on resolution.
        var def = SunlanceFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(20, "Sunlance only damages creatures, not players");
    }
}
