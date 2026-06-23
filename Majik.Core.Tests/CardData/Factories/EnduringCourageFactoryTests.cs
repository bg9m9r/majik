using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="EnduringCourageFactory"/>.
///
/// Enduring Courage (Duskmourn, {2}{R}{R}). Enchantment Creature — Dog Glimmer
/// 3/3. Oracle text (verified against Scryfall 2026-06-23):
///   "Whenever another creature you control enters, it gets +2/+0 and gains
///    haste until end of turn.
///    When Enduring Courage dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// Covers (UNIQUE behaviour only — dispatch + well-formedness are covered by
/// CardFactoryContractTests for every implemented card):
/// - Identity ({2}{R}{R} Enchantment Creature — Dog Glimmer, 3/3, mono-R).
/// - ETB pump/haste trigger condition (CR 603.1 / 603.6a): another creature the
///   controller controls entering — not this card, not an opponent's creature.
/// - Resolution gives the entering creature +2/+0 and Haste until end of turn
///   (CR 613.7c / 613.1c / 702.10b — summoning sickness lifted).
/// - Dies → return-to-battlefield + Layer-4 type-strip (CR 603.6c / 701.20 /
///   613.1d): after the return it's an enchantment but no longer a creature;
///   a subsequent death does not re-return it.
/// </summary>
[Trait("Color", "R")]
public class EnduringCourageFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringCourage_Identity()
    {
        var c = EnduringCourageFactory.Create(_alice);

        c.Name.Should().Be("Enduring Courage");
        c.ManaCost.Should().Be("{2}{R}{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dog).Should().BeTrue();
        c.HasSubtype(CardSubtype.Glimmer).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Red);
        colors.Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Trigger shape
    // -----------------------------------------------------------------------

    [Fact]
    public void EnduringCourage_HasTwoTriggers()
    {
        var c = EnduringCourageFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "the ETB pump/haste trigger + the dies-return trigger");
        triggers.Should().OnlyContain(t => t.ActiveZones.Contains(ZoneType.Battlefield));
    }

    // -----------------------------------------------------------------------
    // ETB pump/haste trigger condition (CR 603.1 / 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void EnterTrigger_FiresForAnotherCreatureYouControl_NotSelf_NotOpponent()
    {
        var c = EnduringCourageFactory.Create(_alice);
        c.SetController(_alice);
        var trigger = EnterTrigger(c);

        var mine = new Creature("Bear", "{1}{G}", 2, 2);
        mine.SetOwner(_alice);
        mine.SetController(_alice);

        var theirs = new Creature("Goblin", "{R}", 1, 1);
        theirs.SetOwner(_bob);
        theirs.SetController(_bob);

        // Another creature I control enters → fires.
        trigger.Condition.Matches(
            new CardMovedEvent(mine, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue("another creature you control entering triggers it");

        // Enduring Courage itself entering → does NOT fire (CR 109.5 "another").
        trigger.Condition.Matches(
            new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeFalse("'another' excludes Enduring Courage itself");

        // An opponent's creature entering → does NOT fire ("you control").
        trigger.Condition.Matches(
            new CardMovedEvent(theirs, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeFalse("only creatures you control trigger it");

        // A creature you control going to the graveyard (not entering) → no.
        trigger.Condition.Matches(
            new CardMovedEvent(mine, ZoneType.Battlefield, ZoneType.Graveyard), trigger)
            .Should().BeFalse("only entering the battlefield triggers it");
    }

    // -----------------------------------------------------------------------
    // ETB pump/haste resolution — "+2/+0 and gains haste until end of turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void EnterTrigger_GivesEnteringCreaturePumpAndHaste()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringCourageFactory.Create(_alice, service);
        c.SetController(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.HasSummoningSickness = true;
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = EnterTrigger(c);

        // Fire the trigger condition so the anaphoric "it" captures the bear,
        // then resolve.
        trigger.Condition.Matches(
            new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        bear.Power.Should().Be(4, "the entering creature gets +2/+0 (CR 613.7c)");
        bear.Toughness.Should().Be(2, "toughness is unchanged (+2/+0, not +2/+2)");
        CombatAbilities.HasHaste(bear).Should().BeTrue(
            "the entering creature gains haste until end of turn (CR 613.1c)");
        bear.HasSummoningSickness.Should().BeFalse(
            "haste lifts summoning sickness so it can attack this turn (CR 702.10b)");
    }

    [Fact]
    public void EnterTrigger_PumpAndHaste_ExpireAtEndOfTurn()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringCourageFactory.Create(_alice, service);
        c.SetController(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var trigger = EnterTrigger(c);
        trigger.Condition.Matches(
            new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield), trigger)
            .Should().BeTrue();
        foreach (var effect in trigger.Effects) effect.Execute();

        bear.Power.Should().Be(4);
        CombatAbilities.HasHaste(bear).Should().BeTrue();

        // CR 514.2 — cleanup wipes "until end of turn" effects.
        service.ExpireEndOfTurn();

        bear.Power.Should().Be(2, "the +2/+0 expires at end of turn (CR 514.2)");
        CombatAbilities.HasHaste(bear).Should().BeFalse(
            "the haste grant expires at end of turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Dies → return as a (non-creature) enchantment
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_ReturnsToBattlefield_UnderOwnersControl()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringCourageFactory.Create(_alice, service);

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        var trig = DiesTrigger(c);
        foreach (var effect in trig.Effects) effect.Execute();

        c.Zone.Should().Be(ZoneType.Battlefield, "it returns to the battlefield (CR 701.20)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(c);
        c.Controller.Should().BeSameAs(_alice, "under its owner's control");
    }

    [Fact]
    public void AfterReturn_ItsAnEnchantmentNotACreature()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringCourageFactory.Create(_alice, service);

        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);

        foreach (var effect in DiesTrigger(c).Effects) effect.Execute();

        var chars = service.Compute((Permanent)c);
        chars.Types.Should().NotContain(CardType.Creature,
            "after returning, it's an enchantment, not a creature (CR 613.1d)");
        chars.Types.Should().Contain(CardType.Enchantment,
            "the printed Enchantment type is preserved (the strip is creature-only)");
    }

    [Fact]
    public void DiesTrigger_OnlyReturnsOnce_SecondDeathDoesNotReturn()
    {
        var service = new ContinuousEffectsService();
        var c = EnduringCourageFactory.Create(_alice, service);

        c.SetOwner(_alice);
        c.SetController(_alice);

        var diesTrigger = DiesTrigger(c);

        // First death → return.
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();
        c.Zone.Should().Be(ZoneType.Battlefield);

        // Second death (now a non-creature enchantment) → intervening-if fails.
        _alice.Zones.Battlefield.RemoveCard(c);
        _alice.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
        foreach (var effect in diesTrigger.Effects) effect.Execute();

        c.Zone.Should().Be(ZoneType.Graveyard,
            "once it has returned as a non-creature enchantment, dying again does not re-return it");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    // The ETB pump/haste trigger is active only on the battlefield; the dies
    // trigger is also active in the graveyard (so it survives the death move).
    private static TriggeredAbility EnterTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => !t.ActiveZones.Contains(ZoneType.Graveyard));

    private static TriggeredAbility DiesTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Graveyard));
}
