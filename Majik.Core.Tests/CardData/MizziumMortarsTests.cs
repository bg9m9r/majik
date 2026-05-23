using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Mizzium Mortars (Return to Ravnica, {1}{R}, Sorcery).
///
/// Oracle text:
///   "Mizzium Mortars deals 4 damage to target creature. Overload {4}{R}{R}".
///   After overload substitution (CR 702.96b): "deals 4 damage to each
///   creature you don't control".
///
/// Covers:
///   - Card identity (Sorcery, {1}{R}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve default-not-overloaded → 4 damage to one target creature.
///   - Resolve structural overloaded branch → 4 damage to each creature
///     the controller does NOT control (controller's own creatures
///     untouched; non-creature permanents untouched).
///
/// Overload (CR 702.96) is an alternative cost. The
/// <see cref="Majik.Core.Costs.OverloadAlternativeCost"/> primitive is a
/// stub (gates cast + carries an IsOverloaded flag) but is not yet
/// plumbed through <see cref="SpellCastFlow"/>, so production casts ship
/// not-overloaded. The overloaded branch is exercised here by passing
/// <c>wasOverloaded: true</c> through the spell-definition builder
/// directly (same posture as Burst Lightning's wasKicked toggle).
/// </summary>
public class MizziumMortarsTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MizziumMortarsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // -----------------------------------------------------------------------
    // Card identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MizziumMortars_IsSorcery_AtCost1R()
    {
        var mm = MizziumMortarsFactory.Create(_alice);

        mm.Name.Should().Be("Mizzium Mortars");
        mm.ManaCost.Should().Be("{1}{R}");
        mm.HasType(CardType.Sorcery).Should().BeTrue();
        mm.Owner.Should().BeSameAs(_alice);
        mm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MizziumMortars()
    {
        var card = NamedCardFactory.Create("Mizzium Mortars", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Mizzium Mortars");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolution — default (not overloaded)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MizziumMortars_NotOverloaded_Deals4Damage_ToTargetCreature()
    {
        var target = NewCreatureOnBattlefield(_bob, "Tarmogoyf", "{1}{G}", 4, 5);

        await CastAndResolveTargeting(target, wasOverloaded: false);

        // Default cast — 4 damage to target creature only (CR 702.96b not
        // engaged; printed "target" still resolves to one creature).
        target.Damage.Should().Be(4);
    }

    [Fact]
    public async Task MizziumMortars_NotOverloaded_DoesNotTouch_OtherCreatures()
    {
        var target = NewCreatureOnBattlefield(_bob, "Tarmogoyf", "{1}{G}", 4, 5);
        var bystander = NewCreatureOnBattlefield(_bob, "Wild Mongrel", "{1}{G}", 2, 2);
        var aliceOwn = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        await CastAndResolveTargeting(target, wasOverloaded: false);

        target.Damage.Should().Be(4);
        bystander.Damage.Should().Be(0, "non-targets are untouched by the printed cast");
        aliceOwn.Damage.Should().Be(0, "controller's creatures are untouched by the printed cast");
    }

    // -----------------------------------------------------------------------
    // Resolution — overloaded branch
    // -----------------------------------------------------------------------

    [Fact]
    public void MizziumMortars_Overloaded_Deals4_ToEachCreature_YouDontControl()
    {
        // Bob (opponent) creatures — all should take 4.
        var bobBear = NewCreatureOnBattlefield(_bob, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobTitan = NewCreatureOnBattlefield(_bob, "Craw Wurm", "{4}{G}{G}", 6, 4);

        // Alice (controller) creatures — must NOT take damage (CR 702.96b
        // rewrites to "each creature you don't control"; the spell's
        // controller is the "you" reference per CR 109.5).
        var aliceBear = NewCreatureOnBattlefield(_alice, "Runeclaw Bear", "{1}{G}", 2, 2);
        var aliceGiant = NewCreatureOnBattlefield(_alice, "Hill Giant", "{3}{R}", 3, 3);

        // Non-creature permanent on opponent's side — must not be hit.
        var bobArtifact = new Artifact("Mishra's Bauble", "{0}");
        bobArtifact.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        var def = MizziumMortarsFactory.BuildSpellDefinition(
            controller: _alice,
            allPlayers: new[] { _alice, _bob },
            resolver: t => t,
            wasOverloaded: true);

        // No targets — overloaded branch carries no TargetRequests
        // (CR 702.96b — "target" is rewritten to "each").
        def.TargetRequests.Count.Should().Be(0);

        // Build an empty ChosenSpellParams (no targets) and fire the effect.
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        bobBear.Damage.Should().Be(4, "Bob's bear is an 'each creature you don't control' hit");
        bobTitan.Damage.Should().Be(4, "Bob's titan is hit too");
        aliceBear.Damage.Should().Be(0, "Alice (controller) is the 'you'; her creatures are spared");
        aliceGiant.Damage.Should().Be(0);
        bobArtifact.Zone.Should().Be(ZoneType.Battlefield);

        // SBA-style sanity: 2-toughness creatures took lethal.
        bobBear.IsDead().Should().BeTrue("4 damage on a 2/2 is lethal");
        bobTitan.IsDead().Should().BeTrue("4 damage on a 4-toughness creature is lethal");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Cast Mizzium Mortars from Alice's hand at <paramref name="target"/>
    /// and resolve the resulting stack object. Mirrors the
    /// BurstLightningTests / UnholyHeatTests cast harness — direct
    /// cast/resolve, no priority loop. <paramref name="wasOverloaded"/>
    /// is plumbed through the spell-definition builder because there is
    /// no Overload primitive in <see cref="SpellCastFlow"/> yet (see
    /// <see cref="MizziumMortarsFactory"/> xmldoc).
    /// </summary>
    private async Task CastAndResolveTargeting(object target, bool wasOverloaded)
    {
        var mm = MizziumMortarsFactory.Create(_alice);
        mm.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(mm);

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { target });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, mm,
            MizziumMortarsFactory.BuildSpellDefinition(
                _alice, new[] { _alice, _bob }, t => t, wasOverloaded),
            agent, ctx);

        mm.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();
    }

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
