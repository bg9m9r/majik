using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MagebaneLizardFactory"/> (Creature — Lizard 1/4,
/// {1}{R}).
///
/// Oracle (verified against the embedded Scryfall seed): "Whenever a player
/// casts a noncreature spell, this creature deals damage to that player equal
/// to the number of noncreature spells they've cast this turn."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity (name, type, Lizard subtype, P/T, mana cost).
///   - One triggered ability present.
///   - A player's first noncreature spell → 1 damage to THAT player.
///   - Subsequent noncreature casts scale (Nth cast → N damage).
///   - Damage hits the CASTER, not the lizard's controller (any-player trigger).
///   - Creature spell → no trigger.
///   - No live TurnState (shape path) → trigger fires but ping no-ops.
/// </summary>
[Trait("Color", "R")]
public class MagebaneLizardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Majik.Core.Spells.Spell NewInstant(Player controller, string name = "Bolt") =>
        new(new Instant(name, "R") { Owner = controller }, controller);

    private static Majik.Core.Spells.Spell NewCreatureSpell(Player controller, string name = "Bear") =>
        new(new Creature(name, "1G", 2, 2) { Owner = controller }, controller);

    /// <summary>
    /// Resolve the top stack object through a live <see cref="GameContext"/>
    /// that threads <paramref name="turnState"/> (so the ping reads the
    /// per-player noncreature tally), exactly as the prod priority loop does.
    /// </summary>
    private static void ResolveTopWithTurnState(
        Majik.Core.Stack.Stack stack, TurnState turnState, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: true,
            turnState: turnState);
        stack.Pop()!.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MagebaneLizard_Identity_Lizard_1_4_AtCost1R()
    {
        var ml = MagebaneLizardFactory.Create(_alice);

        ml.Name.Should().Be("Magebane Lizard");
        ml.ManaCost.Should().Be("{1}{R}");
        ml.HasType(CardType.Creature).Should().BeTrue();
        ml.HasSubtype(CardSubtype.Lizard).Should().BeTrue();
        ml.BasePower.Should().Be(1);
        ml.BaseToughness.Should().Be(4);
        ml.Owner.Should().BeSameAs(_alice);
        ml.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MagebaneLizard_HasOneTriggeredAbility()
    {
        var ml = MagebaneLizardFactory.Create(_alice);
        ml.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // First noncreature spell → 1 damage to that player
    // -----------------------------------------------------------------------

    [Fact]
    public void FirstNoncreatureSpell_DealsOneDamageToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        var ml = MagebaneLizardFactory.Create(_alice, bus, triggers);
        ml.SetZone(ZoneType.Battlefield);

        // Alice casts her first noncreature spell this turn. The prod tally is
        // fed by TurnDriver at cast time — emulate that here.
        turnState.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red }, isNoncreatureSpell: true);
        bus.Publish(new SpellCastEvent(NewInstant(_alice, "Lightning Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveTopWithTurnState(stack, turnState, _alice, _alice, _bob);

        // CR 119.3 — damage to a player is life loss. 1 noncreature spell cast.
        _alice.LifeTotal.Should().Be(19);
        _bob.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Scales with the running noncreature count
    // -----------------------------------------------------------------------

    [Fact]
    public void ThirdNoncreatureSpell_DealsThreeDamageToThatPlayer()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        var ml = MagebaneLizardFactory.Create(_alice, bus, triggers);
        ml.SetZone(ZoneType.Battlefield);

        // Three noncreature spells cast this turn (the third triggers now).
        for (var i = 0; i < 3; i++)
        {
            turnState.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red }, isNoncreatureSpell: true);
        }
        bus.Publish(new SpellCastEvent(NewInstant(_alice, "Third Bolt")));

        triggers.PutPendingTriggersOnStack(_alice);
        ResolveTopWithTurnState(stack, turnState, _alice, _alice, _bob);

        // 3 noncreature spells this turn → 3 damage.
        _alice.LifeTotal.Should().Be(17);
    }

    // -----------------------------------------------------------------------
    // Any-player trigger: damages the CASTER, not the lizard's controller
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentCastsNoncreatureSpell_DealsDamageToTheOpponent()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var turnState = new TurnState();

        // Lizard is Alice's, but Bob (the opponent) is the one casting.
        var ml = MagebaneLizardFactory.Create(_alice, bus, triggers);
        ml.SetZone(ZoneType.Battlefield);

        turnState.RecordSpellCast(_bob, new HashSet<ManaColor> { ManaColor.Red }, isNoncreatureSpell: true);
        bus.Publish(new SpellCastEvent(NewInstant(_bob, "Bob's Bolt")));

        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        ResolveTopWithTurnState(stack, turnState, _alice, _alice, _bob);

        // Bob (the caster) takes the damage; Alice is untouched.
        _bob.LifeTotal.Should().Be(19);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // Creature spell does not trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingCreatureSpell_DoesNotTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ml = MagebaneLizardFactory.Create(_alice, bus, triggers);
        ml.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewCreatureSpell(_alice, "Grizzly Bears")));

        triggers.PendingCount.Should().Be(0);
        _alice.LifeTotal.Should().Be(20);
    }

    // -----------------------------------------------------------------------
    // No live TurnState → trigger fires but ping no-ops
    // -----------------------------------------------------------------------

    [Fact]
    public void NoLiveTurnState_TriggersButNoDamage()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var ml = MagebaneLizardFactory.Create(_alice, bus, triggers);
        ml.SetZone(ZoneType.Battlefield);

        bus.Publish(new SpellCastEvent(NewInstant(_alice, "Lightning Bolt")));
        triggers.PendingCount.Should().Be(1);

        // Resolve with no TurnState (Game.TurnState null) → count reads 0 → no-op.
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.LifeTotal.Should().Be(20);
    }
}
