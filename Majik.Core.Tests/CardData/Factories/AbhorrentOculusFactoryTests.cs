using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Abhorrent Oculus (Duskmourn: House of Horror, {2}{U}).
///
/// Oracle (Scryfall, 2024-09-27):
///   "As an additional cost to cast this spell, exile six cards from
///    your graveyard.
///    Flying
///    At the beginning of each opponent's upkeep, manifest dread."
///
/// Coverage:
/// - Identity / shape / NamedCardFactory dispatch.
/// - Flying KeywordAbility marker.
/// - ExileCardsFromGraveyardAdditionalCost legality + payment.
/// - Opponent-upkeep trigger fires on opponents' upkeeps only.
/// - Live TriggerManager registration via the two-arg overload.
/// </summary>
public class AbhorrentOculusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_HasEyeCreatureShape()
    {
        var oculus = AbhorrentOculusFactory.Create(_alice);

        oculus.Should().BeOfType<Creature>();
        oculus.Name.Should().Be("Abhorrent Oculus");
        oculus.HasType(CardType.Creature).Should().BeTrue();
        oculus.HasSubtype(CardSubtype.Eye).Should().BeTrue();
        oculus.ManaCost.Should().Be("{2}{U}");
        oculus.ManaCostValue.TotalValue.Should().Be(3);
        oculus.Power.Should().Be(5);
        oculus.Toughness.Should().Be(5);
        oculus.Owner.Should().BeSameAs(_alice);
        oculus.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_HasFlyingKeyword_AndOpponentUpkeepTrigger()
    {
        var oculus = AbhorrentOculusFactory.Create(_alice);

        oculus.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
        oculus.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsOculusShape()
    {
        var dispatched = NamedCardFactory.Create("Abhorrent Oculus", _alice);

        dispatched.Should().BeOfType<Creature>();
        dispatched.Name.Should().Be("Abhorrent Oculus");
        dispatched.HasType(CardType.Creature).Should().BeTrue();
        dispatched.HasSubtype(CardSubtype.Eye).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{2}{U}");
        ((Creature)dispatched).Power.Should().Be(5);
        ((Creature)dispatched).Toughness.Should().Be(5);
        dispatched.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Flying");
        dispatched.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Additional cost — exile six cards from your graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileSixCardsCost_RequiresSixCardsInGraveyard()
    {
        var cost = AbhorrentOculusFactory.BuildExileSixCardsAdditionalCost();

        cost.Count.Should().Be(6);

        // Empty graveyard → can't pay.
        cost.CanPay(_alice).Should().BeFalse();

        // Five cards → still can't pay.
        for (int i = 0; i < 5; i++)
        {
            var c = new Card($"Stuffer-{i}", "{0}");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }
        cost.CanPay(_alice).Should().BeFalse("five cards is one short of six");

        // Add a sixth — now we can pay.
        var sixth = new Card("Stuffer-5", "{0}");
        sixth.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(sixth);
        sixth.SetZone(ZoneType.Graveyard);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void ExileSixCardsCost_Pay_MovesSixCardsToExile()
    {
        var cost = AbhorrentOculusFactory.BuildExileSixCardsAdditionalCost();

        // Stock graveyard with eight cards — should pay six and leave two.
        var cards = new List<Card>();
        for (int i = 0; i < 8; i++)
        {
            var c = new Card($"Stuffer-{i}", "{0}");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
            cards.Add(c);
        }

        cost.Pay(_alice).Should().BeTrue();
        cost.Exiled.Should().HaveCount(6);
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(2);
        _alice.Zones.Exile.GetCards().Count().Should().Be(6);

        // Every exiled card has its zone updated.
        foreach (var c in cost.Exiled)
        {
            c.Zone.Should().Be(ZoneType.Exile);
        }
    }

    [Fact]
    public void ExileSixCardsCost_AcceptsAnyCardType_NotJustCreatures()
    {
        // Mix of types — sorcery, instant, land, creature — to prove the
        // cost isn't gated on CardType.Creature (distinct from Hogaak's
        // ExileCreaturesFromGraveyardAdditionalCost sibling).
        var sorcery = new Card("Some Sorcery", "{1}{B}");
        var instant = new Card("Some Instant", "{U}");
        var land = new Card("Some Land", "");
        var creature1 = new Creature("Some Creature 1", "{1}{G}", 1, 1);
        var creature2 = new Creature("Some Creature 2", "{2}{R}", 2, 2);
        var artifact = new Card("Some Artifact", "{0}");
        var grave = new ICard[] { sorcery, instant, land, creature1, creature2, artifact };

        foreach (var c in grave)
        {
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        var cost = AbhorrentOculusFactory.BuildExileSixCardsAdditionalCost();
        cost.CanPay(_alice).Should().BeTrue();
        cost.Pay(_alice).Should().BeTrue();

        cost.Exiled.Should().HaveCount(6,
            "the printed oracle says 'exile six cards' — no creature gate");
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void ExileSixCardsCost_Pay_FailsCleanWhenShort()
    {
        var cost = AbhorrentOculusFactory.BuildExileSixCardsAdditionalCost();

        // Only three cards — payment refuses, no partial exile.
        for (int i = 0; i < 3; i++)
        {
            var c = new Card($"Stuffer-{i}", "{0}");
            c.SetOwner(_alice);
            _alice.Zones.Graveyard.AddCard(c);
            c.SetZone(ZoneType.Graveyard);
        }

        cost.Pay(_alice).Should().BeFalse();
        cost.Exiled.Should().BeEmpty();
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(3,
            "failed payment must leave the graveyard untouched (CR 601.2f atomicity)");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Opponent-upkeep trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void OpponentUpkeepTrigger_LiveBus_FiresOnOpponentUpkeepOnly()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var oculus = AbhorrentOculusFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(oculus);
        oculus.SetZone(ZoneType.Battlefield);

        // Alice's upkeep — Oculus does NOT trigger (printed "each
        // opponent's upkeep").
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0,
            "controller's own upkeeps are excluded");

        // Bob's upkeep — trigger fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1,
            "an opponent's upkeep surfaces the manifest-dread trigger");
    }

    [Fact]
    public void OpponentUpkeepTrigger_OnlyFiresOnUpkeepStep_NotOtherSteps()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var oculus = AbhorrentOculusFactory.Create(_alice, triggers);
        _alice.Zones.Battlefield.AddCard(oculus);
        oculus.SetZone(ZoneType.Battlefield);

        // Bob's Draw step — wrong step, should not fire.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _bob));
        triggers.PendingCount.Should().Be(0);

        // Bob's End step — wrong step.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _bob));
        triggers.PendingCount.Should().Be(0);

        // Bob's Upkeep — fires.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public void OpponentUpkeepTrigger_ManifestDreadEffect_IsNoOpStubAtV1()
    {
        // Documented v1 gap — manifest dread (CR 701.59) is wired as a
        // structural stub. The trigger should fire + the effect should
        // execute, but no library reveal / face-down token / graveyard
        // move happens at v1. This test pins the documented behaviour so
        // a future real implementation of manifest dread breaks this
        // test and updates this assertion deliberately.
        var oculus = AbhorrentOculusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(oculus);
        oculus.SetZone(ZoneType.Battlefield);

        var aliceLibraryBefore = _alice.Zones.Library.GetCards().Count();
        var aliceGraveBefore = _alice.Zones.Graveyard.GetCards().Count();
        var aliceBattlefieldBefore = _alice.Zones.Battlefield.GetCards().Count();

        var upkeep = oculus.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in upkeep.Effects) e.Execute();

        _alice.Zones.Library.GetCards().Count().Should().Be(aliceLibraryBefore,
            "manifest dread v1 stub: no library mutation");
        _alice.Zones.Graveyard.GetCards().Count().Should().Be(aliceGraveBefore,
            "manifest dread v1 stub: no graveyard mutation");
        _alice.Zones.Battlefield.GetCards().Count().Should().Be(aliceBattlefieldBefore,
            "manifest dread v1 stub: no face-down 2/2 token created");
    }
}
