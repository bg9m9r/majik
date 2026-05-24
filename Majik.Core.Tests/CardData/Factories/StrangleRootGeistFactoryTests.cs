using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StrangleRootGeistFactory"/>.
///
/// Covers:
/// - Card identity (name, Creature type, Spirit subtype, P/T, mana cost, owner/controller).
/// - Ability set: Haste + Undying KeywordAbility markers + a single Undying TriggeredAbility.
/// - Undying return: dies with no +1/+1 counters → returns to battlefield with one +1/+1 counter.
/// - Undying interveningIf (CR 603.4): dies with a +1/+1 counter → stays in graveyard.
/// - Comes-back-with-Haste: post-Undying-return the Haste keyword is still present and
///   <see cref="CombatValidator.CanAttack"/> permits the attack the turn it returns.
/// - NamedCardFactory dispatch returns a fully-wired Strangleroot Geist instance.
/// </summary>
public class StrangleRootGeistFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void StrangleRootGeist_NameIsCorrect()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.Name.Should().Be("Strangleroot Geist");
    }

    [Fact]
    public void StrangleRootGeist_IsCreatureSpirit()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.HasType(CardType.Creature).Should().BeTrue();
        g.HasSubtype(CardSubtype.Spirit).Should().BeTrue("printed oracle is Creature — Spirit");
    }

    [Fact]
    public void StrangleRootGeist_HasCorrectStats()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.BasePower.Should().Be(2);
        g.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void StrangleRootGeist_HasPrintedManaCost()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        // Printed cost is {G}{G}. ManaCost.Parse round-trips via two green
        // pips (total mana value 2).
        var parsed = ManaCost.Parse(StrangleRootGeistFactory.PrintedManaCost);
        parsed.Green.Should().Be(2, "the printed cost is two green pips");
        parsed.TotalValue.Should().Be(2);
        g.ManaCost.Should().Be(StrangleRootGeistFactory.PrintedManaCost);
    }

    [Fact]
    public void StrangleRootGeist_OwnerAndControllerAreSet()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability set
    // -----------------------------------------------------------------------

    [Fact]
    public void StrangleRootGeist_HasHasteKeyword()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Haste",
                "Strangleroot Geist has Haste (CR 702.10)");
        CombatAbilities.HasHaste(g).Should().BeTrue(
            "CombatAbilities.HasHaste must read the Haste keyword marker");
    }

    [Fact]
    public void StrangleRootGeist_HasUndyingKeyword()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.Abilities.OfType<KeywordAbility>()
            .Should().Contain(a => a.Keyword == "Undying",
                "Strangleroot Geist has Undying (CR 702.93)");
    }

    [Fact]
    public void StrangleRootGeist_HasExactlyOneTriggeredAbility()
    {
        var g = StrangleRootGeistFactory.Create(_alice);

        g.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the Undying trigger is the only triggered ability");
    }

    // -----------------------------------------------------------------------
    // Undying — dies without a +1/+1 counter returns with one
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.93b — Strangleroot Geist dies with no +1/+1 counters → returns
    /// to the battlefield under its owner's control with one +1/+1 counter.
    /// </summary>
    [Fact]
    public void StrangleRootGeist_DiesWithNoCounters_ReturnsToBattlefieldWithCounter()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var geist = StrangleRootGeistFactory.Create(_alice, triggers);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        triggers.BindCard(geist);

        // Simulate death via ZoneService (moves the Geist to graveyard, fires CardMovedEvent).
        zones.MoveCardTo(geist, ZoneType.Graveyard);

        triggers.PendingCount.Should().Be(1, "Undying trigger must queue on death without a +1/+1 counter");
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Geist should be back on the battlefield.
        geist.Zone.Should().Be(ZoneType.Battlefield);
        _alice.Zones.Battlefield.GetCards().Should().Contain(geist);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(geist);

        // Geist should have exactly one +1/+1 counter (effectively a 3/2).
        geist.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Undying interveningIf — dies WITH a +1/+1 counter → stays dead
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.93 + CR 603.4 — "if it had no +1/+1 counters on it": a creature
    /// that already carried a +1/+1 counter when it died does NOT return.
    /// The interveningIf condition gates the trigger from going on the stack.
    /// </summary>
    [Fact]
    public void StrangleRootGeist_DiesWithPlusOneCounter_StaysInGraveyard()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var geist = StrangleRootGeistFactory.Create(_alice, triggers);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        triggers.BindCard(geist);

        // Give Geist a +1/+1 counter before it dies (e.g. from a previous Undying return
        // or a Hardened Scales effect).
        geist.Counters.Add(CounterType.PlusOnePlusOne, 1);
        geist.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // Die.
        zones.MoveCardTo(geist, ZoneType.Graveyard);

        // InterveningIf fails — trigger must NOT go on the stack.
        triggers.PendingCount.Should().Be(0,
            "Undying must not trigger when a +1/+1 counter was present at death");

        geist.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // Comes back with Haste — attack-allowed the turn it returns
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 702.10 + CR 702.93 — both keyword markers (Haste, Undying) survive
    /// the Undying return as the creature re-enters the battlefield as the
    /// same object. After return, <see cref="CombatAbilities.HasHaste"/>
    /// still reads true and <see cref="CombatValidator.CanAttack"/> permits
    /// the Geist to attack the same turn (Haste bypasses the CR 302.1
    /// summoning-sickness check).
    /// </summary>
    [Fact]
    public void StrangleRootGeist_AfterUndyingReturn_HasHasteAndCanAttack()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var geist = StrangleRootGeistFactory.Create(_alice, triggers);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        triggers.BindCard(geist);

        // Kill it.
        zones.MoveCardTo(geist, ZoneType.Graveyard);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Geist is back on the battlefield.
        geist.Zone.Should().Be(ZoneType.Battlefield);

        // Haste keyword still present after the return.
        CombatAbilities.HasHaste(geist).Should().BeTrue(
            "the Haste keyword marker survives the Undying return — the returned " +
            "Geist is the same card object with the same printed keywords");

        // Combat validator must permit the attack the turn it returns.
        var validator = new CombatValidator();
        validator.CanAttack(geist, _alice).Should().BeTrue(
            "CR 702.10 — Haste bypasses CR 302.1's summoning-sickness gate, so the " +
            "Undying-returned Geist can attack the same turn it re-enters");
    }

    // -----------------------------------------------------------------------
    // Second death after Undying return: stays dead
    // -----------------------------------------------------------------------

    /// <summary>
    /// After an Undying return (the Geist now has a +1/+1 counter), a second
    /// death must NOT trigger Undying again — the interveningIf fails because
    /// the counter is present (CR 702.93).
    /// </summary>
    [Fact]
    public void StrangleRootGeist_AfterUndyingReturn_SecondDeathDoesNotTrigger()
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var triggers = new TriggerManager(stack, _bus);
        var zones = new ZoneService(_bus);

        var geist = StrangleRootGeistFactory.Create(_alice, triggers);
        geist.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(geist);
        triggers.BindCard(geist);

        // First death — no counter.
        zones.MoveCardTo(geist, ZoneType.Graveyard);
        triggers.PendingCount.Should().Be(1);
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // Geist is back on battlefield with +1/+1 counter.
        geist.Zone.Should().Be(ZoneType.Battlefield);
        geist.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);

        // BindCard is idempotent — re-sync active-zone membership after the raw zone-move
        // that the Undying effect body performs.
        triggers.BindCard(geist);

        // Second death — now has the +1/+1 counter from the Undying return.
        zones.MoveCardTo(geist, ZoneType.Graveyard);

        // Trigger is queued (event fired) but InterveningIf fails when going on the stack.
        triggers.PutPendingTriggersOnStack(_alice);

        // Nothing should resolve — Geist stays dead.
        stack.IsEmpty.Should().BeTrue(
            "Undying must not return the creature a second time after it already " +
            "returned with a +1/+1 counter");
        geist.Zone.Should().Be(ZoneType.Graveyard);
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_DispatchesStrangleRootGeist()
    {
        var card = Majik.Core.CardData.NamedCardFactory.Create("Strangleroot Geist", _alice);

        card.Should().BeOfType<Creature>("Strangleroot Geist is a Creature");
        card.Name.Should().Be("Strangleroot Geist");

        var keywords = card.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(a => a.Keyword == "Haste",
            "the dispatcher returns a fully-wired card with the Haste marker");
        keywords.Should().Contain(a => a.Keyword == "Undying",
            "the dispatcher returns a fully-wired card with the Undying marker");

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the dispatcher attaches the Undying trigger built via UndyingFactory");
    }
}
