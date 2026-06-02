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
/// Unit tests for <see cref="KroxaTitanFactory"/> — Kroxa, Titan of
/// Death's Hunger ({B}{R}, Legendary Creature — Elder Giant 6/6).
///
/// Oracle text (Scryfall verified):
///   "When Kroxa enters, sacrifice it unless it escaped.
///    Whenever Kroxa enters or attacks, each opponent discards a card,
///    then each opponent who didn't discard a nonland card this way loses
///    3 life.
///    Escape—{B}{B}{R}{R}, Exile five other cards from your graveyard."
///
/// Covers:
/// - Identity (name, type, P/T 6/6, Elder Giant subtypes, Legendary, cost).
/// - NamedCardFactory dispatch.
/// - Self-sacrifice ETB trigger: sacrificed when hardcast, kept when escaped
///   (CR 603.1 / CR 701.16 / CR 702.138b — mirrors Uro/Phlage).
/// - Enters-or-attacks trigger: each opponent discards; an opponent who
///   discarded a NONLAND card takes no life loss; an opponent who discarded
///   a LAND (or had an empty hand) loses 3 life (CR 701.8 / CR 119.3).
/// - Escape alt-cost shape ({B}{B}{R}{R}, exile 5).
/// </summary>
[Trait("Color", "M")]
public class KroxaTitanFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void KroxaTitan_Identity()
    {
        var c = KroxaTitanFactory.Create(_alice);

        c.Name.Should().Be("Kroxa, Titan of Death's Hunger");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasSubtype(CardSubtype.Giant).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elder).Should().BeTrue(
            "Kroxa is an Elder Giant — both subtypes are in CardSubtype");
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "CR 205.4 — Kroxa is a Legendary creature");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{B}{R}");
    }
    // -----------------------------------------------------------------------
    // Self-sacrifice ETB trigger — CR 603.1 / CR 701.16 / CR 702.138b
    // -----------------------------------------------------------------------

    [Fact]
    public void KroxaTitan_EtbSacTrigger_SacrificesSelf_WhenNotEscaped()
    {
        var alice = new Player("Alice", 20);
        var kroxa = KroxaTitanFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(kroxa);
        kroxa.SetZone(ZoneType.Battlefield);

        var sacTrigger = kroxa.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("sacrifice unless escaped")));

        foreach (var effect in sacTrigger.Effects) effect.Execute();

        kroxa.Zone.Should().Be(ZoneType.Graveyard,
            "hardcast Kroxa has WasCastForEscape=false, so the sac trigger fires (CR 701.16)");
        alice.Zones.Graveyard.GetCards().Should().Contain(kroxa);
        alice.Zones.Battlefield.GetCards().Should().NotContain(kroxa);
    }

    [Fact]
    public void KroxaTitan_EtbSacTrigger_SkipsSacrifice_WhenEscaped()
    {
        var alice = new Player("Alice", 20);
        var kroxa = KroxaTitanFactory.Create(alice);

        alice.Zones.Battlefield.AddCard(kroxa);
        kroxa.SetZone(ZoneType.Battlefield);
        kroxa.SetWasCastForEscape(true);  // simulate the SpellCastFlow stamp

        var sacTrigger = kroxa.Abilities.OfType<TriggeredAbility>()
            .Where(t => t.Condition is EventTriggerCondition<CardMovedEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("sacrifice unless escaped")));

        foreach (var effect in sacTrigger.Effects) effect.Execute();

        kroxa.Zone.Should().Be(ZoneType.Battlefield,
            "escaped Kroxa (CR 702.138b) is NOT sacrificed by the ETB trigger");
        alice.Zones.Battlefield.GetCards().Should().Contain(kroxa);
    }

    // -----------------------------------------------------------------------
    // Escape alt-cost shape — CR 702.138
    // -----------------------------------------------------------------------

    [Fact]
    public void KroxaTitan_BuildAlternativeCost_ReturnsEscapeAltCost_WithPrintedShape()
    {
        var cost = KroxaTitanFactory.BuildAlternativeCost();

        cost.Should().NotBeNull();
        cost.ExileFromGraveyardCount.Should().Be(5,
            "Kroxa's printed Escape rider exiles 5 OTHER graveyard cards");
        // {B}{B}{R}{R} = 2 black + 2 red, no generic.
        cost.AlternativeManaCost.Black.Should().Be(2);
        cost.AlternativeManaCost.Red.Should().Be(2);
        cost.AlternativeManaCost.Generic.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Enters-or-attacks trigger — each opponent discards, conditional life
    // loss. CR 701.8 (discard) + CR 119.3 (life loss).
    // -----------------------------------------------------------------------

    [Fact]
    public void KroxaTitan_EtbValueTrigger_OpponentDiscardingNonland_TakesNoLifeLoss()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob holds a nonland card he can discard → no life loss.
        var spell = new Creature("Grizzly Bears", "1G", 2, 2);
        spell.SetOwner(bob);
        bob.Zones.Hand.AddCard(spell);
        spell.SetZone(ZoneType.Hand);

        var kroxa = KroxaTitanFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            triggers: null,
            opponentAgent: null);

        var valueTrigger = SelectEntersOrAttacksTrigger(kroxa, isEtb: true);

        var bobLifeBefore = bob.LifeTotal;
        foreach (var effect in valueTrigger.Effects) effect.Execute();

        bob.Zones.Hand.GetCards().Should().NotContain(spell,
            "CR 701.8 — each opponent discards a card");
        bob.Zones.Graveyard.GetCards().Should().Contain(spell);
        bob.LifeTotal.Should().Be(bobLifeBefore,
            "Bob discarded a NONLAND card, so he loses no life");
    }

    [Fact]
    public void KroxaTitan_EtbValueTrigger_OpponentDiscardingLand_Loses3Life()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob's only card is a LAND → discarding it does NOT prevent the drain.
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(bob);
        bob.Zones.Hand.AddCard(swamp);
        swamp.SetZone(ZoneType.Hand);

        var kroxa = KroxaTitanFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            triggers: null,
            opponentAgent: null);

        var valueTrigger = SelectEntersOrAttacksTrigger(kroxa, isEtb: true);

        foreach (var effect in valueTrigger.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().Contain(swamp,
            "CR 701.8 — Bob still discards his land");
        bob.LifeTotal.Should().Be(17,
            "Bob did NOT discard a nonland card this way, so he loses 3 life (CR 119.3)");
    }

    [Fact]
    public void KroxaTitan_EtbValueTrigger_OpponentWithEmptyHand_Loses3Life()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        // Bob's hand is empty — he can't discard at all → loses 3 life.

        var kroxa = KroxaTitanFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            triggers: null,
            opponentAgent: null);

        var valueTrigger = SelectEntersOrAttacksTrigger(kroxa, isEtb: true);

        foreach (var effect in valueTrigger.Effects) effect.Execute();

        bob.LifeTotal.Should().Be(17,
            "an opponent who didn't discard a nonland card this way loses 3 life (CR 119.3)");
    }

    [Fact]
    public void KroxaTitan_AttackTrigger_FiresOnCreatureAttacksEvent_AndAppliesSameBody()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(bob);
        bob.Zones.Hand.AddCard(swamp);
        swamp.SetZone(ZoneType.Hand);

        var kroxa = KroxaTitanFactory.Create(
            alice,
            opponentResolver: () => new[] { bob },
            triggers: null,
            opponentAgent: null);
        alice.Zones.Battlefield.AddCard(kroxa);
        kroxa.SetZone(ZoneType.Battlefield);

        var attackTrigger = kroxa.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

        // CR 508.1f — fires when Kroxa is declared as the attacker.
        attackTrigger.IsTriggered(new CreatureAttacksEvent(kroxa, bob)).Should().BeTrue();

        // A different attacker should NOT trigger Kroxa's per-attacker ability.
        var other = new Creature("Hill Giant", "3R", 3, 3);
        other.SetOwner(alice);
        other.SetController(alice);
        other.SetZone(ZoneType.Battlefield);
        attackTrigger.IsTriggered(new CreatureAttacksEvent(other, bob)).Should().BeFalse();

        foreach (var effect in attackTrigger.Effects) effect.Execute();

        bob.Zones.Graveyard.GetCards().Should().Contain(swamp);
        bob.LifeTotal.Should().Be(17,
            "attack trigger shares the ETB body — land discard → 3 life loss");
    }

    private static TriggeredAbility SelectEntersOrAttacksTrigger(Creature kroxa, bool isEtb)
    {
        return kroxa.Abilities.OfType<TriggeredAbility>()
            .Where(t => isEtb
                ? t.Condition is EventTriggerCondition<CardMovedEvent>
                : t.Condition is EventTriggerCondition<CreatureAttacksEvent>)
            .Single(t => t.Effects.Any(e => e.Description != null
                && e.Description.Contains("each opponent discards")));
    }
}
