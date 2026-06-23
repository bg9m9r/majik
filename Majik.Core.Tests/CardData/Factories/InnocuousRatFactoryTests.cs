using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Innocuous Rat (Murders at Karlov Manor, {1}{B}).
///
/// Oracle (Scryfall):
///   "When this creature dies, manifest dread. (Look at the top two cards of
///    your library. Put one onto the battlefield face down as a 2/2 creature
///    and the other into your graveyard. Turn it face up any time for its mana
///    cost if it's a creature card.)"
///
/// Coverage (UNIQUE behaviour only — CardFactoryContractTests covers dispatch +
/// well-formedness for every implemented card):
/// - Identity assert: exact mana cost / P-T / Rat subtype.
/// - Single dies TriggeredAbility on the card shape.
/// - Dies trigger fires on the Battlefield → Graveyard transition (live bus).
/// - Trigger body runs real manifest dread (CR 701.59): top of library becomes
///   a face-down 2/2, second-of-two goes to graveyard.
/// </summary>
[Trait("Color", "B")]
public class InnocuousRatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity (non-vanilla stats — exact mana cost / P-T / subtype)
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasRatCreatureShape_AndSingleDiesTrigger()
    {
        var rat = InnocuousRatFactory.Create(_alice);

        rat.Should().BeOfType<Creature>();
        rat.Name.Should().Be("Innocuous Rat");
        rat.HasType(CardType.Creature).Should().BeTrue();
        rat.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        rat.ManaCost.Should().Be("{1}{B}");
        rat.Power.Should().Be(1);
        rat.Toughness.Should().Be(1);

        // Exactly one triggered ability — the dies trigger.
        rat.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Dies trigger — CR 603.6c
    // -----------------------------------------------------------------------

    [Fact]
    public void DiesTrigger_LiveBus_FiresOnBattlefieldToGraveyardOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var rat = InnocuousRatFactory.Create(_alice, triggers, zones: null);
        _alice.Zones.Battlefield.AddCard(rat);
        rat.SetZone(ZoneType.Battlefield);

        // A move that isn't battlefield → graveyard must not fire.
        bus.Publish(new CardMovedEvent(rat, ZoneType.Battlefield, ZoneType.Exile));
        triggers.PendingCount.Should().Be(0,
            "the trigger is a dies trigger (battlefield → graveyard), not a generic leave");

        // The death move fires the manifest-dread trigger (CR 603.6c).
        bus.Publish(new CardMovedEvent(rat, ZoneType.Battlefield, ZoneType.Graveyard));
        triggers.PendingCount.Should().Be(1,
            "Innocuous Rat dying surfaces the manifest-dread trigger");
    }

    [Fact]
    public void DiesTrigger_ManifestDreadEffect_ResolvesManifestDread()
    {
        // CR 701.59 — the death trigger invokes real manifest dread: top of
        // Alice's library becomes a face-down 2/2 ManifestedCreature on her
        // battlefield; the second-from-top goes to her graveyard.
        var rat = InnocuousRatFactory.Create(_alice);

        // Stock Alice's library with two specific cards. AddCard appends to the
        // end and Fx.LookAtTopN reads index 0 = "top", so add the intended top
        // first (mirrors the Abhorrent Oculus manifest-dread test).
        var topCard = new Creature("Top Card Creature", "{1}{G}", 3, 3);
        topCard.SetOwner(_alice);
        var secondCard = new Card("Second Card", "{R}");
        secondCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        _alice.Zones.Library.AddCard(secondCard);

        var libraryBefore = _alice.Zones.Library.GetCards().Count();
        var battlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        var diesTrigger = rat.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in diesTrigger.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Count().Should().Be(libraryBefore - 2,
            "manifest dread looks at + consumes the top 2 of library");
        _alice.Zones.Graveyard.GetCards().Should().Contain(secondCard,
            "second-of-two looked-at card goes to graveyard");
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(battlefieldBefore + 1,
            "the manifested wrapper joins the battlefield as a face-down 2/2");

        var wrapper = _alice.Zones.Battlefield.GetCards()
            .OfType<ManifestedCreature>().Single();
        wrapper.IsFaceDown.Should().BeTrue();
        wrapper.UnderlyingCard.Should().BeSameAs(topCard);
    }
}
