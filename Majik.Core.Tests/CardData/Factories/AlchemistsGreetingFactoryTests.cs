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
/// Unit tests for <see cref="AlchemistsGreetingFactory"/> (Eldritch Moon).
///
/// Oracle text: "Alchemist's Greeting deals 4 damage to target creature.
/// Madness {1}{R}" ({4}{R} Sorcery.)
///
/// Covers only the printed (non-madness) body:
/// - Identity ({4}{R} Sorcery).
/// - Spell definition shape: 1..1 "target creature" request.
/// - Resolve body deals 4 damage to a target creature (CR 119.2).
/// - Resolve body is a no-op when the target is not a creature (CR 608.2b).
///
/// Madness itself is intrinsic (MadnessCatalog + Fx.DiscardCard funnel) and
/// is covered by MadnessDiscardFunnelTests — not retested here.
/// </summary>
[Trait("Color", "R")]
public class AlchemistsGreetingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature CreatureOnBattlefield(Player owner, int power, int tough)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, tough);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    [Fact]
    public void AlchemistsGreeting_Identity_SorceryAt4R()
    {
        var card = AlchemistsGreetingFactory.Create(_alice);

        card.Name.Should().Be("Alchemist's Greeting");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{4}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AlchemistsGreeting_SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = AlchemistsGreetingFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void AlchemistsGreeting_Resolve_DealsFourDamageToCreature()
    {
        var target = CreatureOnBattlefield(_bob, 5, 5);

        var def = AlchemistsGreetingFactory.BuildSpellDefinition(resolver: x => x);
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

        target.Damage.Should().Be(4, "Alchemist's Greeting deals 4 damage to target creature");
    }

    [Fact]
    public void AlchemistsGreeting_Resolve_NoOp_OnNonCreatureTarget()
    {
        // CR 608.2b — if a spell's only target becomes illegal, the spell does
        // nothing on resolution. Non-creature objects that slip through the
        // resolver are silently skipped.
        var def = AlchemistsGreetingFactory.BuildSpellDefinition(resolver: x => x);
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

        _bob.LifeTotal.Should().Be(20, "Alchemist's Greeting only damages creatures, not players");
    }
}
