using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Genesis Chamber (Fifth Dawn, {2}, Artifact). Oracle text
/// (verified against Scryfall):
///   "Whenever a nontoken creature enters, if this artifact is untapped,
///    that creature's controller creates a 1/1 colorless Myr artifact
///    creature token."
///
/// Covers:
///   - Card identity (name, Artifact type, {2} mana cost, owner/controller).
///   - The symmetric nontoken-creature ETB trigger fires when a nontoken
///     creature enters, minting a 1/1 colourless Myr artifact creature token
///     for THAT creature's controller.
///   - The trigger does NOT fire for a token creature entering ("nontoken").
///   - The trigger does NOT fire for a noncreature permanent entering.
///   - The intervening-if ("if this artifact is untapped") gates the trigger
///     when the chamber is tapped.
///   - The minted Myr's shape (1/1, colourless, Myr, artifact creature).
/// </summary>
[Trait("Color", "C")]
public class GenesisChamberFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature NewCreature(Player owner, string name)
    {
        var c = new Creature(name, manaCost: "{1}", power: 2, toughness: 2);
        c.SetOwner(owner);
        c.SetController(owner);
        return c;
    }

    private (EventBus bus, TriggerManager triggers, Artifact card) WireChamber()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var card = GenesisChamberFactory.Create(_alice, triggers, zoneService: null);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        return (bus, triggers, card);
    }

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_Artifact_AtCost2()
    {
        var card = GenesisChamberFactory.Create(_alice);

        card.Name.Should().Be("Genesis Chamber");
        card.ManaCost.Should().Be("{2}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HasSingleEntersTrigger()
    {
        var card = GenesisChamberFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Symmetric nontoken-creature ETB trigger (CR 603.6a)
    // -----------------------------------------------------------------------

    [Fact]
    public void NontokenCreatureEnters_Untapped_Triggers_AndMintsMyrForItsController()
    {
        var (bus, triggers, _) = WireChamber();

        // Bob's nontoken creature enters — symmetric trigger watches every
        // player's creatures.
        var entering = NewCreature(_bob, "Grizzly Bears");
        _bob.Zones.Battlefield.AddCard(entering);
        entering.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(entering, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1,
            "a nontoken creature entering triggers Genesis Chamber while it is untapped");

        var trigger = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>().Single()
            .Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // CR 109.4 — "that creature's controller" (Bob) gets the Myr.
        var myr = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>().FirstOrDefault(c => c.IsToken);

        myr.Should().NotBeNull("Bob, the entering creature's controller, creates the Myr");
        myr!.Name.Should().Be("Myr");
        myr.GetPower().Should().Be(1);
        myr.GetToughness().Should().Be(1);
        myr.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        myr.HasType(CardType.Artifact).Should().BeTrue("Myr tokens are artifact creatures (CR 111.1)");
        myr.HasType(CardType.Creature).Should().BeTrue();
        CardColors.GetColors(myr).Should().BeEmpty("the Myr is colourless");
    }

    [Fact]
    public void TokenCreatureEnters_DoesNotTrigger()
    {
        var (bus, triggers, _) = WireChamber();

        var token = NewCreature(_bob, "Some Token");
        token.MarkAsToken();
        _bob.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(token, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "the printed 'nontoken' rider excludes token creatures (CR 111.1)");
    }

    [Fact]
    public void NoncreaturePermanentEnters_DoesNotTrigger()
    {
        var (bus, triggers, _) = WireChamber();

        var land = new Land("Forest");
        land.SetOwner(_bob);
        land.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(land, ZoneType.Hand, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "only creatures entering trigger Genesis Chamber");
    }

    [Fact]
    public void TappedChamber_DoesNotTrigger_InterveningIf()
    {
        var (bus, triggers, card) = WireChamber();

        // CR 603.4 — "if this artifact is untapped" is an intervening-if,
        // checked at trigger time. A tapped chamber does nothing.
        card.Tap();

        var entering = NewCreature(_bob, "Grizzly Bears");
        _bob.Zones.Battlefield.AddCard(entering);
        entering.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(entering, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "the intervening-if 'if this artifact is untapped' fails while tapped");
    }
}
