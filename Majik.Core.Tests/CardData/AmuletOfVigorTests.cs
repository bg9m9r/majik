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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Amulet of Vigor (Worldwake, {1}).
///
/// Covers:
///   - Card identity (name, type, mana cost, owner/controller, one triggered ability).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - Tapped permanent ETB under controller → Amulet's trigger fires and
///     untaps it (verified end-to-end through ZoneService + ReplacementBus
///     + TriggerManager + Stack).
///   - Untapped permanent ETB under controller → trigger does not fire.
///   - Tapped permanent ETB under opponent → trigger does not fire (oracle
///     "under your control" gate).
///   - Two copies of Amulet of Vigor + one tapped ETB → both triggers
///     queue; resolving both yields a single untap (the second is a no-op
///     because IsTapped is already false at its resolution).
/// </summary>
public class AmuletOfVigorTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void AmuletOfVigor_Identity_ArtifactAt1()
    {
        var amulet = AmuletOfVigorFactory.Create(_alice);

        amulet.Name.Should().Be("Amulet of Vigor");
        amulet.ManaCost.Should().Be("{1}");
        amulet.HasType(CardType.Artifact).Should().BeTrue();
        amulet.Owner.Should().BeSameAs(_alice);
        amulet.Controller.Should().BeSameAs(_alice);
        amulet.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void AmuletOfVigor_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Amulet of Vigor", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Amulet of Vigor");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void TappedPermanentEntersUnderController_AmuletUntapsIt()
    {
        var (zones, rep, stack, triggers) = BuildEngine();

        // Amulet of Vigor on the battlefield under Alice's control.
        var amulet = AmuletOfVigorFactory.Create(_alice, triggers);
        amulet.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(amulet);

        // Boseiju, Who Endures — printed "enters tapped unless you control
        // two or fewer other lands." With three other lands on the
        // battlefield it will enter tapped (CR 614.1c).
        var boseiju = NamedCardFactory.Create("Boseiju, Who Endures", _alice);
        _alice.Zones.Hand.AddCard(boseiju);
        boseiju.SetZone(ZoneType.Hand);
        var entity = new Majik.Core.CardData.CardEntity
        {
            Name = "Boseiju, Who Endures",
            OracleText = "Boseiju, Who Endures enters tapped unless you control two or fewer other lands.",
            TypeLine = "Legendary Land",
        };
        ConditionalEntersTappedBinder.Bind((Land)boseiju, entity, rep).Should().BeTrue();

        // Seed three other lands so Boseiju enters tapped.
        for (int i = 0; i < 3; i++)
        {
            var forest = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
            forest.SetOwner(_alice);
            _alice.Zones.Battlefield.AddCard(forest);
            forest.SetZone(ZoneType.Battlefield);
        }

        zones.MoveCardTo(boseiju, ZoneType.Battlefield, controller: _alice);

        // ZoneService taps before publishing CardMovedEvent; the trigger
        // condition reads IsTapped=true and queues. CR 614.6.
        ((Permanent)boseiju).IsTapped.Should().BeTrue();
        triggers.PendingCount.Should().Be(1);

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        ((Permanent)boseiju).IsTapped.Should().BeFalse();
    }

    [Fact]
    public void UntappedPermanentEntersUnderController_AmuletDoesNotTrigger()
    {
        var (zones, _, _, triggers) = BuildEngine();

        var amulet = AmuletOfVigorFactory.Create(_alice, triggers);
        amulet.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(amulet);

        // Grizzly Bears — vanilla creature, enters untapped (no ETB-tapped
        // replacement registered).
        var bear = NamedCardFactory.Create("Grizzly Bears", _alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        ((Permanent)bear).IsTapped.Should().BeFalse();
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TappedPermanentEntersUnderOpponent_AmuletDoesNotTrigger()
    {
        var (zones, rep, _, triggers) = BuildEngine();

        // Alice controls Amulet of Vigor.
        var amulet = AmuletOfVigorFactory.Create(_alice, triggers);
        amulet.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(amulet);

        // Bob casts an unconditional "enters tapped" land — should NOT
        // benefit from Alice's Amulet (oracle: "under YOUR control").
        var bobLand = new Land("Bob's Tap Land");
        bobLand.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobLand);
        bobLand.SetZone(ZoneType.Hand);
        rep.Register<ZoneMoveIntent>(new LambdaReplacement<ZoneMoveIntent>(
            (i, _) => ReferenceEquals(i.Card, bobLand) && i.ToZone == ZoneType.Battlefield,
            (i, _) => i with { EntersTapped = true }));

        zones.MoveCardTo(bobLand, ZoneType.Battlefield, controller: _bob);

        bobLand.IsTapped.Should().BeTrue();
        triggers.PendingCount.Should().Be(0);
    }

    [Fact]
    public void TwoAmulets_TappedEtb_OnlyOneUntapNeeded_SecondResolutionIsNoOp()
    {
        var (zones, rep, stack, triggers) = BuildEngine();

        // Two Amulets of Vigor on the battlefield.
        var amulet1 = AmuletOfVigorFactory.Create(_alice, triggers);
        amulet1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(amulet1);

        var amulet2 = AmuletOfVigorFactory.Create(_alice, triggers);
        amulet2.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(amulet2);

        // A creature that enters tapped via a replacement.
        var creature = new Creature("Tapped Bear", "{1}{G}", 2, 2);
        creature.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);
        rep.Register<ZoneMoveIntent>(new LambdaReplacement<ZoneMoveIntent>(
            (i, _) => ReferenceEquals(i.Card, creature) && i.ToZone == ZoneType.Battlefield,
            (i, _) => i with { EntersTapped = true }));

        zones.MoveCardTo(creature, ZoneType.Battlefield, controller: _alice);

        // Both Amulets see the same CardMovedEvent — two pending triggers.
        creature.IsTapped.Should().BeTrue();
        triggers.PendingCount.Should().Be(2);

        triggers.PutPendingTriggersOnStack(_alice);

        // Resolve the top (LIFO) — untaps the creature.
        stack.Pop()!.Resolve();
        creature.IsTapped.Should().BeFalse();

        // Resolve the second — the IsTapped guard makes it a no-op. The
        // assertion is twofold: (a) no throw from Permanent.Untap on an
        // already-untapped permanent, and (b) the creature remains
        // untapped.
        Action resolveSecond = () => stack.Pop()!.Resolve();
        resolveSecond.Should().NotThrow();
        creature.IsTapped.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, ReplacementBus rep, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, rep, stack, triggers);
    }
}
