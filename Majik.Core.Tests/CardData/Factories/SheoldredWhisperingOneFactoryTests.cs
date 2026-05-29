using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SheoldredWhisperingOneFactory"/> (New Phyrexia,
/// {5}{B}{B}).
///
/// Covers:
/// - Identity (name, type Creature, supertype Legendary, subtype Praetor,
///   P/T 6/6, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - Swampwalk keyword marker (CR 702.13 landwalk).
/// - Your-upkeep trigger (CR 603.1): controller's Upkeep → return a creature
///   card from the controller's graveyard to the battlefield. Fires only on
///   the controller's Upkeep (not an opponent's, not a different step).
/// - Each-opponent's-upkeep trigger (CR 603.1): an opponent's Upkeep → that
///   player sacrifices a creature. Fires only for opponents (not the
///   controller's own upkeep); the triggering opponent's creature goes to
///   their graveyard. No-op when the opponent controls no creatures.
/// </summary>
public class SheoldredWhisperingOneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // The factory attaches the two upkeep triggers in oracle order:
    //   [0] = your-upkeep (return a creature from your graveyard),
    //   [1] = each-opponent's-upkeep (that player sacrifices a creature).
    private static TriggeredAbility YourUpkeepTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().ElementAt(0);

    private static TriggeredAbility OpponentUpkeepTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>().ElementAt(1);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_Identity()
    {
        var c = SheoldredWhisperingOneFactory.Create(_alice);

        c.Name.Should().Be("Sheoldred, Whispering One");
        c.ManaCost.Should().Be("{5}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue("Sheoldred is Legendary");
        c.HasSubtype(CardSubtype.Praetor).Should().BeTrue();
        c.BasePower.Should().Be(6);
        c.BaseToughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Sheoldred_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sheoldred, Whispering One", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Sheoldred, Whispering One");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Praetor).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Swampwalk (CR 702.13)
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_HasSwampwalkKeyword()
    {
        var c = SheoldredWhisperingOneFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Swampwalk",
            "CR 702.13 — Swampwalk is printed on Sheoldred, Whispering One");
    }

    // -----------------------------------------------------------------------
    // Your-upkeep trigger (CR 603.1) — return a creature from your graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_YourUpkeep_ReturnsCreatureFromGraveyardToBattlefield()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        // A dead creature in Alice's graveyard.
        var zombie = new Creature("Walking Corpse", "{2}{B}", 2, 2);
        zombie.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(zombie);
        zombie.SetZone(ZoneType.Graveyard);

        var yourUpkeep = YourUpkeepTrigger(sheoldred);

        // Alice's upkeep fires the return trigger.
        yourUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeTrue("At the beginning of your upkeep — CR 603.1");
        yourUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _bob))
            .Should().BeFalse("\"your upkeep\" fires for the controller only — CR 603.1");

        foreach (var e in yourUpkeep.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(zombie,
            "the creature card is returned from the graveyard to the battlefield");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(zombie);
        zombie.Zone.Should().Be(ZoneType.Battlefield);
        zombie.Controller.Should().BeSameAs(_alice, "returned under your control");
    }

    [Fact]
    public void Sheoldred_YourUpkeepTrigger_DoesNotFireOnOpponentUpkeepOrOtherSteps()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var yourUpkeep = YourUpkeepTrigger(sheoldred);

        yourUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _bob))
            .Should().BeFalse("opponent's upkeep must not fire the your-upkeep return");
        yourUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Draw, _alice))
            .Should().BeFalse("only the Upkeep step fires it");
    }

    [Fact]
    public void Sheoldred_YourUpkeep_NoCreatureInGraveyard_IsNoOp()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var yourUpkeep = YourUpkeepTrigger(sheoldred);

        // Empty graveyard → resolving the trigger is a clean no-op.
        var act = () => { foreach (var e in yourUpkeep.Effects) e.Execute(); };
        act.Should().NotThrow();
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(sheoldred);
    }

    // -----------------------------------------------------------------------
    // Each-opponent's-upkeep trigger (CR 603.1) — that player sacrifices
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_OpponentUpkeep_ThatPlayerSacrificesACreature()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        // Bob (the opponent) controls a creature.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var oppUpkeep = OpponentUpkeepTrigger(sheoldred);

        // Fire on Bob's upkeep (sets "that player" = Bob via the condition).
        oppUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _bob))
            .Should().BeTrue("At the beginning of each opponent's upkeep — CR 603.1");

        foreach (var e in oppUpkeep.Effects) e.Execute();

        // CR 701.16 — sacrifice moves the creature to its owner's graveyard.
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bear,
            "that player sacrifices a creature");
        _bob.Zones.Graveyard.GetCards().Should().Contain(bear);
        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Sheoldred_OpponentUpkeepTrigger_DoesNotFireOnControllersOwnUpkeep()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var oppUpkeep = OpponentUpkeepTrigger(sheoldred);

        oppUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _alice))
            .Should().BeFalse("the controller's own upkeep is not an opponent's upkeep — CR 102.1");
        oppUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Draw, _bob))
            .Should().BeFalse("only the Upkeep step fires it");
    }

    [Fact]
    public void Sheoldred_OpponentUpkeep_OpponentControlsNoCreature_IsNoOp()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(sheoldred);
        sheoldred.SetZone(ZoneType.Battlefield);

        var oppUpkeep = OpponentUpkeepTrigger(sheoldred);

        oppUpkeep.IsTriggered(new StepStartedEvent(PhaseStateType.Upkeep, _bob)).Should().BeTrue();

        // Bob controls no creatures → resolving is a clean no-op.
        var act = () => { foreach (var e in oppUpkeep.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Active zone
    // -----------------------------------------------------------------------

    [Fact]
    public void Sheoldred_UpkeepTriggers_OnlyActiveOnBattlefield()
    {
        var sheoldred = SheoldredWhisperingOneFactory.Create(_alice);

        foreach (var trigger in sheoldred.Abilities.OfType<TriggeredAbility>())
        {
            trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
            trigger.ActiveZones.Should().NotContain(ZoneType.Hand,
                "upkeep triggers are battlefield-only abilities — CR 113.6");
            trigger.ActiveZones.Should().NotContain(ZoneType.Graveyard);
        }
    }
}
