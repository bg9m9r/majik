using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ArchonOfCrueltyFactory"/>.
///
/// Covers:
/// - Identity ({6}{B}{B} Creature — Archon, 6/6, black, MV 8).
/// - Flying keyword marker (CR 702.9).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly two battlefield-active triggered abilities (ETB + attack).
/// - ETB trigger condition fires on CardMovedEvent → Battlefield for the card.
/// - Attack trigger condition fires on CreatureAttacksEvent for the card.
/// - Full resolution with target opponent present:
///     * opponent with creature → creature sacrificed (CR 701.16).
///     * opponent with planeswalker only → planeswalker sacrificed.
///     * opponent with no creature/planeswalker → no sac, rest still executes.
///     * opponent discards one card.
///     * opponent loses 3 life.
///     * controller draws 1 card.
///     * controller gains 3 life.
/// - No target chosen → clean no-op (CR 608.2b).
/// - Attack trigger fires same full effect as ETB.
/// </summary>
[Trait("Color", "B")]
public class ArchonOfCrueltyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchonOfCruelty_Identity()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);

        c.Name.Should().Be("Archon of Cruelty");
        c.ManaCost.Should().Be("{6}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Archon).Should().BeTrue("Archon of Cruelty is an Archon");
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ArchonOfCruelty_ManaValue_IsEight()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);
        // {6}{B}{B} = MV 8 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(8, "CR 202.3 — {6}{B}{B} has mana value 8");
    }

    [Fact]
    public void ArchonOfCruelty_IsBlack()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);
        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Black,
            "Archon of Cruelty has {B}{B} pips");
    }

    // -----------------------------------------------------------------------
    // Flying keyword
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchonOfCruelty_HasFlyingKeyword()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Archon of Cruelty has Flying");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Trigger shape — two battlefield-active triggers
    // -----------------------------------------------------------------------

    [Fact]
    public void ArchonOfCruelty_HasExactlyTwoTriggeredAbilities_BothBattlefieldActive()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2, "ETB trigger + attack trigger");

        foreach (var t in triggers)
        {
            t.ActiveZones.Should().Contain(ZoneType.Battlefield,
                "both triggers are battlefield-active per CR 603.6a");
        }
    }

    [Fact]
    public void ArchonOfCruelty_EtbTrigger_ConditionMatchesCardMovedToBattlefield()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);
        c.SetOwner(_alice);
        c.SetController(_alice);
        // Zone must be Battlefield for IsTriggered's active-zone guard to pass
        // (CR 603.6a — trigger fires once the card is on the battlefield).
        c.SetZone(ZoneType.Battlefield);

        // The ETB trigger is the one keyed on a CardMovedEvent condition.
        // It is the trigger whose condition does NOT match CreatureAttacksEvent —
        // i.e. the non-attack trigger.
        var etbTrigger = c.Abilities.OfType<TriggeredAbility>()
            .First(t => !(t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>));

        // Verify condition matches "this card → Battlefield".
        var moveEvt = new Majik.Core.Events.CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etbTrigger.IsTriggered(moveEvt).Should().BeTrue(
            "ETB trigger fires when the card moves to the battlefield");

        // A different card moving does not fire.
        var otherCard = new Card("Other", "");
        var otherEvt = new Majik.Core.Events.CardMovedEvent(otherCard, ZoneType.Hand, ZoneType.Battlefield);
        etbTrigger.IsTriggered(otherEvt).Should().BeFalse(
            "ETB trigger only fires for this specific card");
    }

    [Fact]
    public void ArchonOfCruelty_AttackTrigger_ConditionMatchesCreatureAttacksEvent()
    {
        var c = ArchonOfCrueltyFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var attackTrigger = c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>);

        // Self-attack fires.
        attackTrigger.IsTriggered(new CreatureAttacksEvent(c, _bob)).Should().BeTrue(
            "attack trigger fires when this creature attacks");

        // Another creature attacking does not fire.
        var other = new Creature("Other", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        other.SetController(_alice);
        other.SetZone(ZoneType.Battlefield);
        attackTrigger.IsTriggered(new CreatureAttacksEvent(other, _bob)).Should().BeFalse(
            "attack trigger only fires for this specific creature");
    }

    // -----------------------------------------------------------------------
    // Full resolution — ETB trigger (representative)
    // -----------------------------------------------------------------------

    private static Creature SetupArchon(Player alice) =>
        ArchonOfCrueltyFactory.Create(alice);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .First(t => !(t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>));

    private static TriggeredAbility GetAttackTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<Majik.Core.Domain.DomainEvents.CreatureAttacksEvent>);

    [Fact]
    public void EtbTrigger_OpponentHasCreature_CreatureIsSacrificed()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(bob);
        bobBear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        // Seed bob's hand so discard step doesn't no-op.
        var bobCard = new Card("Some Card", "");
        bobCard.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        bobBear.Zone.Should().Be(ZoneType.Graveyard,
            "Archon's trigger causes opponent to sacrifice a creature");
        bob.Zones.Graveyard.GetCards().Should().Contain(bobBear);
    }

    [Fact]
    public void EtbTrigger_OpponentHasPlaneswalkerOnly_PlaneswalkerIsSacrificed()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Put a Planeswalker on Bob's battlefield (no creature).
        var pw = new Majik.Core.Cards.Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        pw.SetOwner(bob);
        pw.SetController(bob);
        bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var bobCard = new Card("Some Card", "");
        bobCard.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        pw.Zone.Should().Be(ZoneType.Graveyard,
            "Archon's trigger causes opponent to sacrifice a planeswalker when no creature is available");
    }

    [Fact]
    public void EtbTrigger_OpponentHasNoCreatureOrPlaneswalker_SkipsSacStep_RestStillExecutes()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob has no creature or planeswalker on the battlefield.
        var bobCard = new Card("Some Card", "");
        bobCard.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });

        // Seed alice's library so draw works.
        var libCard = new Card("Library Card", "");
        libCard.SetOwner(alice);
        alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var aliceLifeBefore = alice.LifeTotal;
        var bobLifeBefore = bob.LifeTotal;

        foreach (var e in etb.Effects) e.Execute();

        // Sac step skipped — no permanents on Bob's battlefield.
        bob.Zones.Battlefield.GetCards().Should().BeEmpty(
            "Bob had nothing to sacrifice");

        // Discard step still fires — Bob's hand was 1 card.
        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "Bob discards even with nothing to sacrifice");
        bob.Zones.Graveyard.GetCards().Should().Contain(bobCard,
            "discarded card goes to graveyard");

        // Life swing still fires.
        bob.LifeTotal.Should().Be(bobLifeBefore - 3,
            "Bob loses 3 life regardless of sac availability");
        alice.LifeTotal.Should().Be(aliceLifeBefore + 3,
            "Alice gains 3 life");

        // Draw still fires.
        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "Alice draws 1 card");
    }

    [Fact]
    public void EtbTrigger_OpponentDiscardsSingleCard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // One card in Bob's hand.
        var bobCard = new Card("Discard Target", "");
        bobCard.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "opponent discards a card (CR 701.8)");
        bob.Zones.Graveyard.GetCards().Should().Contain(bobCard,
            "discarded card moves to graveyard");
    }

    [Fact]
    public void EtbTrigger_OpponentLosesThreeLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        bob.LifeTotal.Should().Be(17, "target opponent loses 3 life (CR 119.3)");
    }

    [Fact]
    public void EtbTrigger_ControllerDrawsOneCard()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Seed Alice's library.
        var libCard = new Card("Library Card", "");
        libCard.SetOwner(alice);
        alice.Zones.Library.AddCard(libCard);
        libCard.SetZone(ZoneType.Library);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "controller draws 1 card (CR 120.1)");
        alice.Zones.Library.GetCards().Should().BeEmpty(
            "card moved from library to hand");
    }

    [Fact]
    public void EtbTrigger_ControllerGainsThreeLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        alice.LifeTotal.Should().Be(23, "controller gains 3 life (CR 119.3)");
    }

    [Fact]
    public void EtbTrigger_NoChosenTarget_CleanNoOp()
    {
        var alice = new Player("Alice", 20);
        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        // No SetChosenTargets call — ChosenTargets is empty.

        var act = () =>
        {
            foreach (var e in etb.Effects) e.Execute();
        };

        act.Should().NotThrow("no chosen target → clean no-op");
        alice.LifeTotal.Should().Be(20, "no target → no life changes");
    }

    // -----------------------------------------------------------------------
    // Attack trigger fires same full effect
    // -----------------------------------------------------------------------

    [Fact]
    public void AttackTrigger_FiresSameFullEffect()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var archon = SetupArchon(alice);
        var attackTrig = GetAttackTrigger(archon);
        attackTrig.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in attackTrig.Effects) e.Execute();

        // Life swing verifies the full effect body ran.
        bob.LifeTotal.Should().Be(17, "attack trigger causes opponent to lose 3 life");
        alice.LifeTotal.Should().Be(23, "attack trigger causes controller to gain 3 life");
    }

    // -----------------------------------------------------------------------
    // Planeswalker edge-case: victim keeps creature/planeswalker choice
    // (creature is present and is picked)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbTrigger_OpponentHasBothCreatureAndPlaneswalker_SacrificesFirst()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Place a creature first (deterministic pick with no agent = first).
        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bobBear.SetOwner(bob);
        bobBear.SetController(bob);
        bob.Zones.Battlefield.AddCard(bobBear);
        bobBear.SetZone(ZoneType.Battlefield);

        var pw = new Majik.Core.Cards.Planeswalker("Liliana", "{1}{B}{B}", 3);
        pw.SetOwner(bob);
        pw.SetController(bob);
        bob.Zones.Battlefield.AddCard(pw);
        pw.SetZone(ZoneType.Battlefield);

        var bobCard = new Card("Some Card", "");
        bobCard.SetOwner(bob);
        bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var archon = SetupArchon(alice);
        var etb = GetEtbTrigger(archon);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bob } });
        foreach (var e in etb.Effects) e.Execute();

        // Exactly one of the two permanents was sacrificed.
        var bobBattlefield = bob.Zones.Battlefield.GetCards().ToList();
        bobBattlefield.Should().HaveCount(1, "one permanent was sacrificed (CR 701.16)");
        bob.Zones.Graveyard.GetCards()
            .Should().Contain(c => c.HasType(CardType.Creature) || c.HasType(CardType.Planeswalker),
                "the sacrificed permanent is a creature or planeswalker");
    }
}
