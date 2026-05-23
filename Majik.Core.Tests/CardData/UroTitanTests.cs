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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="UroTitanFactory"/>.
///
/// Covers:
/// - Identity (name, type Creature, P/T 6/6, Giant subtype, Legendary
///   supertype, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Self-sacrifice ETB trigger: Uro is sacrificed when its ETB trigger
///   resolves (CR 603.1 / CR 701.16 — Escape branch not wired).
/// - ETB +3 life / draw 1 / may-play-land-from-hand trigger
///   (CR 119.3 / CR 121.1 / CR 113.6c).
/// - Attack trigger fires on CreatureAttacksEvent with the same body as
///   the ETB +3/draw/may-land trigger (CR 508.1f).
/// </summary>
public class UroTitanTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UroTitan_Identity()
    {
        var c = UroTitanFactory.Create(_alice);

        c.Name.Should().Be("Uro, Titan of Nature's Wrath");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue(
            "Uro is an Elder Giant — Giant subtype wired (Elder deferred from CardSubtype)");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4 — Uro is a Legendary creature");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{1}{G}{U}");
    }

    [Fact]
    public void UroTitan_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Uro, Titan of Nature's Wrath", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Uro, Titan of Nature's Wrath");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{G}{U}");
    }

    // -----------------------------------------------------------------------
    // Self-sacrifice ETB trigger — CR 603.1 / CR 701.16
    // Escape (CR 702.143) is deferred so the trigger always sacs on ETB —
    // faithful to the printed hardcast case.
    // -----------------------------------------------------------------------

    [Fact]
    public void UroTitan_EtbSacTrigger_SacrificesSelf_WhenNotEscaped()
    {
        var alice = new Player("Alice", 20);
        var uro = UroTitanFactory.Create(alice);

        // Put Uro on the battlefield (simulate ETB landing).
        alice.Zones.Battlefield.AddCard(uro);
        uro.SetZone(ZoneType.Battlefield);

        // The factory wires TWO ETB triggers (sac + gain/draw/may-land).
        // Pick the sacrifice one by looking at the effect description.
        var sacTrigger = uro.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("sacrifice unless escaped")));

        foreach (var effect in sacTrigger.Effects) effect.Execute();

        uro.Zone.Should().Be(ZoneType.Graveyard,
            "Escape is not wired (CR 702.143), so the ETB sac trigger fires unconditionally — Uro goes to its owner's graveyard (CR 701.16)");
        alice.Zones.Graveyard.GetCards().Should().Contain(uro);
        alice.Zones.Battlefield.GetCards().Should().NotContain(uro);
    }

    // -----------------------------------------------------------------------
    // ETB +3 life / draw 1 / may put a land from hand onto battlefield
    // CR 119.3 + CR 121.1 + CR 113.6c.
    // -----------------------------------------------------------------------

    [Fact]
    public void UroTitan_EtbValueTrigger_Gains3_Draws1_AndPutsLandFromHand()
    {
        var alice = new Player("Alice", 20);

        // Seed library so the draw succeeds.
        var topOfLibrary = new Creature("Grizzly Bears", "1G", 2, 2);
        topOfLibrary.SetOwner(alice);
        alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        // Land in hand — eligible for the "may put a land card from your
        // hand onto the battlefield" rider.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(alice);
        alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var uro = UroTitanFactory.Create(alice);

        // Pick the gain/draw/may-land ETB trigger by its description.
        var valueTrigger = uro.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("ETB +3 life")));

        var lifeBefore = alice.LifeTotal;
        foreach (var effect in valueTrigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(lifeBefore + 3,
            "CR 119.3 — Uro's enter/attack trigger gains the controller 3 life");
        alice.Zones.Hand.GetCards().Should().Contain(topOfLibrary,
            "CR 121.1 — Uro's enter/attack trigger draws a card from the top of the library");
        alice.Zones.Library.GetCards().Should().NotContain(topOfLibrary);

        forest.Zone.Should().Be(ZoneType.Battlefield,
            "CR 113.6c — the may-play-land clause auto-accepts the first land in hand in v1");
        alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        alice.Zones.Hand.GetCards().Should().NotContain(forest);
        forest.Controller.Should().BeSameAs(alice,
            "CR 110.2 — the put-in permanent enters under the activator's control");
    }

    // -----------------------------------------------------------------------
    // Attack trigger — same body as ETB (CR 508.1f)
    // -----------------------------------------------------------------------

    [Fact]
    public void UroTitan_AttackTrigger_FiresOnCreatureAttacksEvent_AndAppliesGainDrawLand()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Seed library so the draw succeeds.
        var topOfLibrary = new Creature("Llanowar Elves", "G", 1, 1);
        topOfLibrary.SetOwner(alice);
        alice.Zones.Library.AddCard(topOfLibrary);
        topOfLibrary.SetZone(ZoneType.Library);

        // Land in hand for the may-play-land rider.
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(alice);
        alice.Zones.Hand.AddCard(island);
        island.SetZone(ZoneType.Hand);

        var uro = UroTitanFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(uro);
        uro.SetZone(ZoneType.Battlefield);

        // Locate the attack trigger by its CreatureAttacksEvent condition.
        var attackTrigger = uro.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // CR 508.1f — fires when Uro is declared as the attacker.
        var attackEvent = new CreatureAttacksEvent(uro, bob);
        attackTrigger.IsTriggered(attackEvent).Should().BeTrue(
            "the attack trigger matches CreatureAttacksEvent where the source is the attacker");

        // A different attacker should NOT trigger Uro's per-attacker ability.
        var otherAttacker = new Creature("Hill Giant", "3R", 3, 3);
        otherAttacker.SetOwner(alice);
        otherAttacker.SetController(alice);
        otherAttacker.SetZone(ZoneType.Battlefield);
        var otherEvent = new CreatureAttacksEvent(otherAttacker, bob);
        attackTrigger.IsTriggered(otherEvent).Should().BeFalse(
            "the per-attacker trigger only fires for Uro itself");

        var lifeBefore = alice.LifeTotal;
        foreach (var effect in attackTrigger.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(lifeBefore + 3,
            "CR 119.3 — attack trigger gains 3 life, same body as the ETB trigger");
        alice.Zones.Hand.GetCards().Should().Contain(topOfLibrary,
            "CR 121.1 — attack trigger draws a card");
        island.Zone.Should().Be(ZoneType.Battlefield,
            "CR 113.6c — attack trigger's may-play-land clause auto-accepts the first land in hand");
        island.Controller.Should().BeSameAs(alice);
    }
}
