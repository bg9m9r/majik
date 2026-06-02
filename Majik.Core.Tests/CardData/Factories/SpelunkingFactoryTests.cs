using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Spelunking (The Lost Caverns of Ixalan, {2}{G}, Enchantment).
/// Oracle text (verified against Scryfall):
///   "When this enchantment enters, draw a card, then you may put a land card
///    from your hand onto the battlefield. If you put a Cave onto the
///    battlefield this way, you gain 4 life.
///    Lands you control enter untapped."
///
/// Covers:
///   - Card identity (name, Enchantment type, {2}{G} mana cost, green,
///     owner/controller).
///   - NamedCardFactory dispatch hands back the correct shape.
///   - The ETB trigger fires when Spelunking enters; does NOT fire for an
///     unrelated card.
///   - On resolve: draw a card, then put a land from hand onto the
///     battlefield (CR 113.6c), and gain 4 life iff that land is a Cave.
///   - Declining / no land in hand gains no life.
///   - "Lands you control enter untapped" forces an entering land untapped,
///     overriding a self-tapping replacement, and is one-sided (opponents'
///     lands untouched).
/// </summary>
[Trait("Color", "G")]
public class SpelunkingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        ICard c = new Card(name, "G");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return (Card)c;
    }

    private static Land NewLandInHand(Player owner, string name, params CardSubtype[] subtypes)
    {
        var land = new Land(name, subtypes: subtypes);
        land.SetOwner(owner);
        owner.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        return land;
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus rep, EventBus bus) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep, bus);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_Enchantment_AtCost2G()
    {
        var card = SpelunkingFactory.Create(_alice);

        card.Name.Should().Be("Spelunking");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
    }

    [Fact]
    public void Dispatch_ReturnsSpelunking()
    {
        var card = NamedCardFactory.Create("Spelunking", _alice);

        card.Should().NotBeNull();
        card.Name.Should().Be("Spelunking");
        card.HasType(CardType.Enchantment).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void HasEtbTrigger()
    {
        var card = SpelunkingFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void EntersBattlefield_FiresEtbTrigger()
    {
        var (zones, stack, triggers, rep, bus) = BuildEngine();
        var card = SpelunkingFactory.Create(_alice, bus, triggers, rep, zones);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "the ETB trigger fires when Spelunking enters");
    }

    [Fact]
    public void UnrelatedCardMove_DoesNotTrigger()
    {
        var (zones, stack, triggers, rep, bus) = BuildEngine();
        var card = SpelunkingFactory.Create(_alice, bus, triggers, rep, zones);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        var other = NewCardInLibrary(_alice, "Other");

        bus.Publish(new CardMovedEvent(other, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "only Spelunking entering triggers the ability");
    }

    // -----------------------------------------------------------------------
    // ETB resolve — draw, put land, conditional life gain
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DrawsACard_AndPutsLandFromHand()
    {
        var card = SpelunkingFactory.Create(_alice);
        var top = NewCardInLibrary(_alice, "Llanowar Elves");
        var forest = NewLandInHand(_alice, "Forest");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Hand, "the top card is drawn");
        forest.Zone.Should().Be(ZoneType.Battlefield, "the land is put onto the battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
    }

    [Fact]
    public void Resolve_PutCave_Gains4Life()
    {
        var card = SpelunkingFactory.Create(_alice);
        NewCardInLibrary(_alice, "Filler");
        var cave = NewLandInHand(_alice, "Hidden Cataract", CardSubtype.Cave);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        cave.Zone.Should().Be(ZoneType.Battlefield);
        _alice.LifeTotal.Should().Be(24, "putting a Cave this way gains 4 life (CR 119.3)");
    }

    [Fact]
    public void Resolve_PutNonCaveLand_NoLifeGain()
    {
        var card = SpelunkingFactory.Create(_alice);
        NewCardInLibrary(_alice, "Filler");
        NewLandInHand(_alice, "Forest");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.LifeTotal.Should().Be(20, "a non-Cave land grants no life");
    }

    [Fact]
    public void Resolve_NoLandInHand_DrawsOnly_NoLifeGain()
    {
        var card = SpelunkingFactory.Create(_alice);
        var top = NewCardInLibrary(_alice, "Filler");

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Hand, "the draw still happens");
        _alice.LifeTotal.Should().Be(20, "no land put → no life gain");
        _alice.Zones.Battlefield.GetCards().OfType<Land>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // "Lands you control enter untapped." (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void LandsEnterUntapped_OverridesSelfTappingReplacement()
    {
        var (zones, stack, triggers, rep, bus) = BuildEngine();
        var card = SpelunkingFactory.Create(_alice, bus, triggers, rep, zones);

        // Spelunking is on the battlefield → its untapped static is registered.
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        // A self-tapping land (would normally enter tapped).
        var tapLand = NewLandInHand(_alice, "Some Tap Land");
        rep.Register(new EntersTappedReplacement(tapLand));

        zones.MoveCardTo(tapLand, ZoneType.Battlefield, controller: _alice);

        tapLand.Zone.Should().Be(ZoneType.Battlefield);
        tapLand.IsTapped.Should().BeFalse(
            "Spelunking forces lands its controller controls to enter untapped");
    }

    [Fact]
    public void LandsEnterUntapped_OneSided_OpponentsLandStillTaps()
    {
        var (zones, stack, triggers, rep, bus) = BuildEngine();
        var card = SpelunkingFactory.Create(_alice, bus, triggers, rep, zones);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(card, ZoneType.Stack, ZoneType.Battlefield));

        // Bob's self-tapping land — Spelunking is Alice's, so it must NOT help.
        var bobLand = new Land("Bob Tap Land");
        bobLand.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobLand);
        bobLand.SetZone(ZoneType.Hand);
        rep.Register(new EntersTappedReplacement(bobLand));

        zones.MoveCardTo(bobLand, ZoneType.Battlefield, controller: _bob);

        bobLand.IsTapped.Should().BeTrue(
            "Spelunking only untaps lands its own controller controls (CR 109.5)");
    }
}
