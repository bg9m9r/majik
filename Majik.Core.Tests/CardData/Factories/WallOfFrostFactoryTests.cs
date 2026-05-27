using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WallOfFrostFactory"/>.
///
/// Card: Wall of Frost — {1}{U}{U} Creature — Wall 0/7.
/// Oracle text:
///   "Defender.
///    Whenever this creature blocks a creature, that creature doesn't
///    untap during its controller's next untap step."
///
/// Covers:
/// - Identity ({1}{U}{U}, blue, 0/7, Creature — Wall, mana value 3,
///   owner/controller wired).
/// - NamedCardFactory dispatch.
/// - Defender keyword marker present.
/// - Exactly one battlefield-active TriggeredAbility.
/// - Blocks trigger fires when Wall of Frost is declared as a blocker
///   (via BlockersDeclaredEvent), and marks the blocked creature to
///   skip its controller's next untap step (CR 502.1 via
///   UntapStepRestrictions.MarkPermanentDoesNotUntap).
/// - Blocks trigger does NOT fire when a different creature blocks.
/// - The blocked creature is NOT tapped (Wall of Frost only causes
///   skip-untap, not a tap effect).
/// - CR 611.2b "next untap step" one-shot: skip-untap is removed on
///   the first Untap StepStartedEvent for the blocked creature's
///   controller.
/// </summary>
public class WallOfFrostFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public void Dispose()
    {
        UntapStepRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFrost_Identity()
    {
        var wall = WallOfFrostFactory.Create(_alice);

        wall.Name.Should().Be("Wall of Frost");
        wall.ManaCost.Should().Be("{1}{U}{U}");
        wall.HasType(CardType.Creature).Should().BeTrue();
        wall.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        wall.BasePower.Should().Be(0);
        wall.BaseToughness.Should().Be(7);
        wall.Owner.Should().BeSameAs(_alice);
        wall.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WallOfFrost_IsBlue()
    {
        var wall = WallOfFrostFactory.Create(_alice);

        CardColors.GetColors(wall).Should().Contain(ManaColor.Blue,
            "Wall of Frost has two {U} pips in its mana cost");
    }

    [Fact]
    public void WallOfFrost_ManaValueIsThree()
    {
        var wall = WallOfFrostFactory.Create(_alice);

        // {1}{U}{U} → 1 generic + 2 blue pips = mana value 3 (CR 202.3).
        ManaCost.Parse(wall.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void WallOfFrost_HasDefenderKeyword()
    {
        var wall = WallOfFrostFactory.Create(_alice);

        wall.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender",
                "Defender is wired as a KeywordAbility marker (CR 702.3)");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFrost_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Wall of Frost", _alice);

        card.Should().BeOfType<Creature>("Wall of Frost is a Creature");
        card.Name.Should().Be("Wall of Frost");
        card.HasSubtype(CardSubtype.Wall).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{U}{U}");
        ((Creature)card).BasePower.Should().Be(0);
        ((Creature)card).BaseToughness.Should().Be(7);
        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Defender");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WallOfFrost_ExactlyOneBattlefieldActiveTrigger()
    {
        var wall = WallOfFrostFactory.Create(_alice);

        var triggers = wall.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Wall of Frost has exactly one triggered ability — blocks-creature skip-untap");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the trigger is active while Wall of Frost is on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Blocks trigger — fires and marks skip-untap on blocked creature
    // -----------------------------------------------------------------------

    [Fact]
    public void BlocksTrigger_MarksBlockedCreatureToSkipNextUntapStep()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wall = WallOfFrostFactory.Create(_alice, triggers);
        wall.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wall);

        var attacker = new Creature("Goblin Guide", "{R}", 2, 2);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        attacker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(attacker);

        // Simulate a combat in which Wall of Frost blocks the attacker.
        // BlockersDeclaredEvent is the engine hook for "whenever ~ blocks".
        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        var attackerObj = new Majik.Core.Combat.Attacker(attacker, _alice);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(wall, attackerObj));
        combat.TransitionToAssigningDamage();

        _bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(1, "the blocks trigger fired");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        UntapStepRestrictions.ShouldSkipUntap(attacker, _bob).Should().BeTrue(
            "Wall of Frost marks the blocked creature to skip its controller's next untap step (CR 502.1)");
    }

    [Fact]
    public void BlocksTrigger_DoesNotTapBlockedCreature()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var wall = WallOfFrostFactory.Create(_alice, triggers);
        wall.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wall);

        var attacker = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        attacker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(attacker);
        // attacker was already tapped by attacking; untap to check wall does NOT re-tap
        // The Wall's ability does NOT tap — only marks skip-untap.
        // Leave untapped to verify no tap side-effect.

        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        var attackerObj = new Majik.Core.Combat.Attacker(attacker, _alice);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(wall, attackerObj));
        combat.TransitionToAssigningDamage();

        _bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        attacker.IsTapped.Should().BeFalse(
            "Wall of Frost's ability does not tap the blocked creature, only prevents untap");
    }

    [Fact]
    public void BlocksTrigger_DoesNotFire_WhenDifferentCreatureBlocks()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var wall = WallOfFrostFactory.Create(_alice, triggers);
        wall.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wall);

        var otherBlocker = new Creature("Staunch Defenders", "{3}{W}", 2, 6);
        otherBlocker.SetOwner(_alice);
        otherBlocker.SetController(_alice);
        otherBlocker.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(otherBlocker);

        var attacker = new Creature("Goblin Guide", "{R}", 2, 2);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        attacker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(attacker);

        // Wall of Frost is NOT in this combat — only the other blocker is.
        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        var attackerObj = new Majik.Core.Combat.Attacker(attacker, _alice);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(otherBlocker, attackerObj));
        combat.TransitionToAssigningDamage();

        _bus.Publish(new BlockersDeclaredEvent(combat));

        triggers.PendingCount.Should().Be(0,
            "Wall of Frost's trigger does not fire when a different creature blocks");
        UntapStepRestrictions.ShouldSkipUntap(attacker, _bob).Should().BeFalse(
            "attacker is not marked for skip-untap when Wall of Frost did not block");
    }

    // -----------------------------------------------------------------------
    // CR 611.2b — "next untap step" one-shot cleanup
    // -----------------------------------------------------------------------

    [Fact]
    public void BlocksTrigger_SkipUntap_ClearedAfterNextUntapStepOfBlockedCreaturesController()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);

        var wall = WallOfFrostFactory.Create(_alice, triggers, _bus);
        wall.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(wall);

        var attacker = new Creature("Goblin Guide", "{R}", 2, 2);
        attacker.SetOwner(_bob);
        attacker.SetController(_bob);
        attacker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(attacker);

        var combat = new Majik.Core.Combat.Combat(_bob, _alice);
        var attackerObj = new Majik.Core.Combat.Attacker(attacker, _alice);
        combat.AddAttacker(attackerObj);
        combat.TransitionToDeclaringBlockers();
        attackerObj.AddBlocker(new Majik.Core.Combat.Blocker(wall, attackerObj));
        combat.TransitionToAssigningDamage();

        _bus.Publish(new BlockersDeclaredEvent(combat));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        UntapStepRestrictions.ShouldSkipUntap(attacker, _bob).Should().BeTrue(
            "skip is registered after trigger resolves");

        // Simulate Alice's untap step — should NOT clear the skip (wrong player).
        _bus.Publish(new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Untap, _alice));
        UntapStepRestrictions.ShouldSkipUntap(attacker, _bob).Should().BeTrue(
            "Alice's untap step does not clear Bob's skip-untap restriction");

        // Simulate Bob's (the attacker's controller) untap step — clears the skip.
        _bus.Publish(new StepStartedEvent(Majik.Core.StateMachine.PhaseStateType.Untap, _bob));
        UntapStepRestrictions.ShouldSkipUntap(attacker, _bob).Should().BeFalse(
            "Bob's next untap step removes the skip-untap restriction (CR 611.2b — one-shot)");
    }
}
