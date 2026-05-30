using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="RecklessRageFactory"/>.
///
/// Card: Reckless Rage — Instant {R} (Rivals of Ixalan).
///   "Reckless Rage deals 4 damage to target creature you don't control
///    and 2 damage to target creature you control."
///
/// Shape mirrors <see cref="ArcTrailFactory"/> (two simultaneous 1..1
/// target requests, fixed damage per request, both routed through
/// <see cref="Fx.DealDamageAny"/>) but each request is constrained by the
/// target's controller relative to the caster (CR 601.2c). The two
/// constrained candidate pools are produced by per-request
/// <see cref="TargetRequest.CandidateGatherer"/>s keyed off
/// <see cref="GameContext.Self"/>, the same live-gatherer posture as
/// <see cref="FellFactory"/>.
///
/// Covers:
///   - Card identity (Instant, {R}, owner/controller) + NamedCardFactory dispatch.
///   - Resolve: 4 damage to the "you don't control" creature, 2 to the
///     "you control" creature (marked-damage path, CR 119.3).
///   - The two target requests are declared 1..1 each.
///   - The candidate gatherers partition battlefield creatures by controller
///     relative to the caster (CR 601.2c): request[0] yields only creatures
///     the caster does NOT control; request[1] yields only creatures the
///     caster DOES control.
/// </summary>
public class RecklessRageFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void RecklessRage_Identity()
    {
        var rr = RecklessRageFactory.Create(_alice);

        rr.Name.Should().Be("Reckless Rage");
        rr.ManaCost.Should().Be("{R}");
        rr.HasType(CardType.Instant).Should().BeTrue();
        rr.Owner.Should().BeSameAs(_alice);
        rr.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_RecklessRage()
    {
        var card = NamedCardFactory.Create("Reckless Rage", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Reckless Rage");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_4ToOpponentCreature_2ToOwnCreature()
    {
        var enemy = MakeCreature("Goblin", _bob, 5, 5);
        var mine = MakeCreature("Bear", _alice, 3, 3);

        var def = RecklessRageFactory.BuildSpellDefinition(o => o!);
        var effects = def.EffectFactory(MakeChosen(new object[] { enemy }, new object[] { mine }));
        foreach (var e in effects) e.Execute();

        enemy.Damage.Should().Be(4, "the creature the caster doesn't control takes 4 damage");
        mine.Damage.Should().Be(2, "the creature the caster controls takes 2 damage");
    }

    [Fact]
    public void Resolve_DeclaresTwoTargetRequests()
    {
        var def = RecklessRageFactory.BuildSpellDefinition(o => o!);

        def.TargetRequests.Should().HaveCount(2);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[1].MinTargets.Should().Be(1);
        def.TargetRequests[1].MaxTargets.Should().Be(1);
    }

    [Fact]
    public void CandidateGatherers_PartitionCreaturesByController()
    {
        var enemy = MakeCreature("Goblin", _bob, 5, 5);
        var mine = MakeCreature("Bear", _alice, 3, 3);

        var ctx = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: PhaseStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        var def = RecklessRageFactory.BuildSpellDefinition(o => o!);

        var dontControl = def.TargetRequests[0].ResolveCandidates(ctx);
        var control = def.TargetRequests[1].ResolveCandidates(ctx);

        dontControl.Should().ContainSingle().Which.Should().BeSameAs(enemy);
        control.Should().ContainSingle().Which.Should().BeSameAs(mine);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Creature MakeCreature(string name, Player controller, int power, int toughness)
    {
        var c = new Creature(name, "{1}", power, toughness);
        c.SetOwner(controller);
        c.SetController(controller);
        controller.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static ChosenSpellParams MakeChosen(object[] first, object[] second) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { first, second },
            Mana: ManaPayment.Empty);
}
