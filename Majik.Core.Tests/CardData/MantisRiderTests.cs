using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MantisRiderFactory"/> and
/// <see cref="ReflectorMageFactory"/>.
///
/// Mantis Rider:
/// - Card identity (name, Creature type, Human Monk subtypes, owner/controller)
/// - Flying, Vigilance, and Haste keyword markers are wired.
///
/// Reflector Mage:
/// - Card identity (name, Creature type, Human Wizard subtypes, owner/controller)
/// - ETB trigger: opponent's creature is bounced to its owner's hand.
/// - ETB trigger: no legal target (empty opponent battlefield) → no-op.
/// - ETB trigger: CR 608.2b — target no longer on battlefield at resolution → no-op.
/// </summary>
public class MantisRiderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Mantis Rider — card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MantisRider_NameIsCorrect()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Name.Should().Be("Mantis Rider");
    }

    [Fact]
    public void MantisRider_IsCreature()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void MantisRider_HasHumanMonkSubtypes()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.HasSubtype(CardSubtype.Human).Should().BeTrue();
        rider.HasSubtype(CardSubtype.Monk).Should().BeTrue();
    }

    [Fact]
    public void MantisRider_PowerAndToughnessAreThreeThree()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Power.Should().Be(3);
        rider.Toughness.Should().Be(3);
    }

    [Fact]
    public void MantisRider_OwnerAndControllerAreSet()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Owner.Should().BeSameAs(_alice);
        rider.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mantis Rider — keyword markers
    // -----------------------------------------------------------------------

    [Fact]
    public void MantisRider_HasFlyingKeyword()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "Mantis Rider has the printed Flying ability (CR 702.9)");
    }

    [Fact]
    public void MantisRider_HasVigilanceKeyword()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Vigilance",
                "Mantis Rider has the printed Vigilance ability (CR 702.20)");
    }

    [Fact]
    public void MantisRider_HasHasteKeyword()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Haste",
                "Mantis Rider has the printed Haste ability (CR 702.10)");
    }

    [Fact]
    public void MantisRider_HasExactlyThreeKeywordAbilities()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<KeywordAbility>().Should().HaveCount(3,
            "Flying, Vigilance, Haste — no other keyword markers on Mantis Rider");
    }

    [Fact]
    public void MantisRider_HasNoTriggeredAbilities()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Mantis Rider is vanilla — no triggered abilities");
    }

    [Fact]
    public void MantisRider_HasNoManaAbilities()
    {
        var rider = MantisRiderFactory.Create(_alice);
        rider.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Mantis Rider produces no mana");
    }

    // -----------------------------------------------------------------------
    // Reflector Mage — card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectorMage_NameIsCorrect()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.Name.Should().Be("Reflector Mage");
    }

    [Fact]
    public void ReflectorMage_IsCreature()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.HasType(CardType.Creature).Should().BeTrue();
    }

    [Fact]
    public void ReflectorMage_HasHumanWizardSubtypes()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.HasSubtype(CardSubtype.Human).Should().BeTrue();
        mage.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
    }

    [Fact]
    public void ReflectorMage_PowerAndToughnessAreTwoThree()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.Power.Should().Be(2);
        mage.Toughness.Should().Be(3);
    }

    [Fact]
    public void ReflectorMage_OwnerAndControllerAreSet()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.Owner.Should().BeSameAs(_alice);
        mage.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ReflectorMage_HasExactlyOneTriggeredAbility()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        mage.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB bounce trigger on Reflector Mage");
    }

    [Fact]
    public void ReflectorMage_EtbTrigger_HasOneTargetRequest()
    {
        var mage = ReflectorMageFactory.Create(_alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        etb.TargetRequests.Should().HaveCount(1,
            "exactly one 'target creature an opponent controls' request");
        etb.TargetRequests[0].Description.Should()
            .Be("target creature an opponent controls");
        etb.TargetRequests[0].MinTargets.Should().Be(1);
        etb.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Reflector Mage — ETB trigger: opponent creature bounced to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectorMage_EtbEffect_BouncesOpponentCreatureToHand()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Set up an opponent creature on the battlefield.
        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        bob.Zones.Battlefield.AddCard(target);
        target.SetZone(ZoneType.Battlefield);

        var mage = ReflectorMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        // Simulate target selection via SetChosenTargets.
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        target.Zone.Should().Be(ZoneType.Hand,
            "Reflector Mage ETB bounces the chosen creature to its owner's hand");
        bob.Zones.Hand.GetCards().Should().Contain(target,
            "the bounced creature ends up in Bob's hand");
        bob.Zones.Battlefield.GetCards().Should().NotContain(target,
            "the creature has left Bob's battlefield");
    }

    [Fact]
    public void ReflectorMage_EtbEffect_NoTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        // Bob has no creatures — no legal target declared; ChosenTargets left empty.
        var mage = ReflectorMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "ETB with no chosen target is a no-op (no opponent creatures)");
        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "no creature was bounced when there was no target");
    }

    [Fact]
    public void ReflectorMage_EtbEffect_TargetAlreadyLeft_IsNoOp()
    {
        // CR 608.2b — if the chosen target is no longer on the battlefield at
        // resolution, the ability does nothing.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);

        var target = new Creature("Grizzly Bears", "1G", 2, 2);
        target.SetOwner(bob);
        target.SetController(bob);
        // Target is in the graveyard at resolution time (not on battlefield).
        bob.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard);

        var mage = ReflectorMageFactory.Create(alice);
        var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "CR 608.2b: illegal target at resolution is a no-op, not an exception");
        bob.Zones.Hand.GetCards().Should().BeEmpty(
            "the already-dead creature is not bounced to hand");
        bob.Zones.Graveyard.GetCards().Should().Contain(target,
            "the creature stays in the graveyard (it was already there)");
    }

    // -----------------------------------------------------------------------
    // Reflector Mage — per-player name restriction (CR 601.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void ReflectorMage_EtbEffect_RegistersPerPlayerNameBan()
    {
        try
        {
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);

            // Use a unique card name so concurrently-running test classes
            // that share the process-static CastingRestrictions table
            // (RangerCaptain / Teferi / Drannith / Containment / Veil-of-Summer
            // each `Clear()` on setup/teardown) can't trample our assertions.
            var uniqueName = "Reflector Mage Test Bears " + Guid.NewGuid();
            var target = new Creature(uniqueName, "1G", 2, 2);
            target.SetOwner(bob);
            target.SetController(bob);
            bob.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);

            var mage = ReflectorMageFactory.Create(alice);
            var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
            etb.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { target },
            });
            foreach (var effect in etb.Effects) effect.Execute();

            CastingRestrictions.IsCardNameBlockedForPlayer(bob, uniqueName)
                .Should().BeTrue("Reflector Mage bans the bounced creature's name for its owner.");
            CastingRestrictions.IsCardNameBlockedForPlayer(alice, uniqueName)
                .Should().BeFalse("the ban is per-player — Alice (controller) is not affected.");
            // Global rail must NOT be set — Meddling Mage's global rail is
            // a different surface.
            CastingRestrictions.IsCardNameBlocked(uniqueName).Should().BeFalse(
                "Reflector Mage uses the per-player rail, not the global one.");

            // Teardown — token-scoped removal leaves no leakage even if
            // sibling tests don't Clear before us.
            CastingRestrictions.RemoveNamedCardBlock(mage);
        }
        finally
        {
            // Belt + braces.
            CastingRestrictions.Clear();
        }
    }

    [Fact]
    public void ReflectorMage_EtbEffect_NameBan_LiftsOnControllersNextTurn_WithEventBus()
    {
        try
        {
            var alice = new Player("Alice", 20);
            var bob = new Player("Bob", 20);
            var bus = new EventBus();

            var uniqueName = "Reflector Mage Test Bears " + Guid.NewGuid();
            var target = new Creature(uniqueName, "1G", 2, 2);
            target.SetOwner(bob);
            target.SetController(bob);
            bob.Zones.Battlefield.AddCard(target);
            target.SetZone(ZoneType.Battlefield);

            var mage = ReflectorMageFactory.Create(alice, zoneService: null, eventBus: bus, triggers: null);
            var etb = mage.Abilities.OfType<TriggeredAbility>().Single();
            etb.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { target },
            });
            foreach (var effect in etb.Effects) effect.Execute();

            CastingRestrictions.IsCardNameBlockedForPlayer(bob, uniqueName).Should().BeTrue();

            // Bob's turn starts — ban still in place (CR 702 "your next turn"
            // reads as the controller's next turn, not the affected player's).
            bus.Publish(new TurnStartedEvent(bob, 2));
            CastingRestrictions.IsCardNameBlockedForPlayer(bob, uniqueName).Should().BeTrue(
                "Bob's turn is not Alice's — ban remains.");

            // Alice's NEXT turn — ban lifts. Reflector Mage's resolve happens
            // during the controller's CURRENT turn, so the TurnStartedEvent
            // for that turn has already been published before the cleanup
            // subscription; the first delivered matching event is the
            // controller's next turn.
            bus.Publish(new TurnStartedEvent(alice, 3));
            CastingRestrictions.IsCardNameBlockedForPlayer(bob, uniqueName).Should().BeFalse(
                "Alice's next turn — Reflector Mage's name ban expires (CR 702 'until your next turn').");
        }
        finally
        {
            CastingRestrictions.Clear();
        }
    }
}
