using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PuresteelPaladinFactory"/>,
/// <see cref="ZeroEquipCostEffect"/>, and dispatcher wiring.
///
/// Card text (New Phyrexia, {1}{W}):
///   "Whenever an Equipment enters under your control, you may draw a card.
///    As long as you control three or more artifacts, Equipment you control
///    have equip {0}."
///
/// Process-global <see cref="ZeroEquipCostEffect"/> registry is cleared in
/// <see cref="Dispose"/> so tests can run in any order without leaking
/// state. Tests do not share xUnit collections (no other suite touches the
/// zero-equip registry today).
/// </summary>
public class PuresteelPaladinTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public PuresteelPaladinTests()
    {
        // Defensive: clear registry before each test in case a prior
        // assembly run left state.
        ZeroEquipCostEffect.ResetForTests();
    }

    public void Dispose()
    {
        ZeroEquipCostEffect.ResetForTests();
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PuresteelPaladin_Identity()
    {
        var c = PuresteelPaladinFactory.Create(_alice);

        c.Name.Should().Be("Puresteel Paladin");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeFalse(
            "Puresteel Paladin is an artifact-care card, not itself an artifact");
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PuresteelPaladin_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Puresteel Paladin", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Puresteel Paladin");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB-draw trigger — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public void EquipmentEntersUnderControl_DrawsCardViaTriggerEffect()
    {
        // Seed a top-of-library card so the draw has something to pull.
        var topOfDeck = new Card("Top Card", "");
        topOfDeck.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topOfDeck);
        topOfDeck.SetZone(ZoneType.Library);

        // An Equipment that's entering Alice's battlefield.
        var sword = new Artifact("Sword of Test", "2",
            subtypes: new[] { CardSubtype.Equipment });
        sword.SetOwner(_alice);
        sword.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sword);
        sword.SetZone(ZoneType.Battlefield);

        var paladin = PuresteelPaladinFactory.Create(_alice);
        var trigger = paladin.Abilities.OfType<TriggeredAbility>().Single();

        // Condition should match an Equipment-entering event for Alice.
        var movedEvent = new CardMovedEvent(
            card: sword, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(movedEvent, ability: null!).Should().BeTrue();

        // Fire the effect — the controller should draw the top card.
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topOfDeck,
            "the trigger draws the top of the controller's library (CR 121)");
        _alice.Zones.Library.GetCards().Should().NotContain(topOfDeck);
    }

    // -----------------------------------------------------------------------
    // ETB-draw trigger — condition negatives
    // -----------------------------------------------------------------------

    [Fact]
    public void NonEquipmentEntering_DoesNotMatchTriggerCondition()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice);
        var trigger = paladin.Abilities.OfType<TriggeredAbility>().Single();

        // Plain artifact (no Equipment subtype) — must not match.
        var doodad = new Artifact("Random Doodad", "2");
        doodad.SetOwner(_alice);
        doodad.SetController(_alice);

        var e = new CardMovedEvent(
            card: doodad, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(e, ability: null!).Should().BeFalse(
            "the trigger only fires on Equipment, not bare artifacts");

        // Creature entering — must not match (no Equipment subtype, not an
        // Artifact).
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        var bearEvent = new CardMovedEvent(
            card: bear, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(bearEvent, ability: null!).Should().BeFalse(
            "the trigger does not fire on creatures");
    }

    [Fact]
    public void OpponentEquipmentEntering_DoesNotMatchTriggerCondition()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice);
        var trigger = paladin.Abilities.OfType<TriggeredAbility>().Single();

        // Equipment that enters under BOB's control — must not match.
        var bobSword = new Artifact("Bob's Sword", "2",
            subtypes: new[] { CardSubtype.Equipment });
        bobSword.SetOwner(_bob);
        bobSword.SetController(_bob);

        var e = new CardMovedEvent(
            card: bobSword, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield);
        trigger.Condition.Matches(e, ability: null!).Should().BeFalse(
            "the trigger reads 'under YOUR control' — opponent Equipment doesn't qualify");
    }

    // -----------------------------------------------------------------------
    // Zero-equip static — threshold semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void ZeroEquipCost_Inactive_WhenControllerHasNoArtifacts()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        // Trigger the lifecycle's ETB sync — emit a CardMovedEvent so the
        // bus-subscribed lifecycle registers itself.
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        // No artifacts on the battlefield (Puresteel itself is a Creature).
        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeFalse(
            "with 0 artifacts on the battlefield the threshold of 3 is not met");
    }

    [Fact]
    public void ZeroEquipCost_Inactive_WithTwoArtifacts_BelowThreshold()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        // Two artifacts — still under the 3 threshold.
        AddArtifact(_alice, "Mox A");
        AddArtifact(_alice, "Mox B");

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeFalse(
            "2 artifacts is below the 3-artifact threshold");
    }

    [Fact]
    public void ZeroEquipCost_Active_WithThreeArtifactsOnControllerBattlefield()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        AddArtifact(_alice, "Mox A");
        AddArtifact(_alice, "Mox B");
        AddArtifact(_alice, "Mox C");

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeTrue(
            "3 artifacts on Alice's battlefield meets Puresteel's threshold");
    }

    [Fact]
    public void ZeroEquipCost_DoesNotCountOpponentArtifacts()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        // Alice has 0 artifacts; Bob has 5.
        for (int i = 0; i < 5; i++) AddArtifact(_bob, $"Bob Mox {i}");

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeFalse(
            "opponent artifacts must not count toward 'you control 3 or more artifacts'");
    }

    [Fact]
    public void ZeroEquipCost_DoesNotCountPuresteelItself()
    {
        // Puresteel is a Creature, not an Artifact. With three artifacts +
        // Puresteel on the battlefield the threshold is met; removing one
        // artifact should drop the count to 2 (Puresteel itself does not
        // make up the difference).
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        var a = AddArtifact(_alice, "Mox A");
        AddArtifact(_alice, "Mox B");
        AddArtifact(_alice, "Mox C");

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeTrue();

        // Remove one — now only 2 artifacts (Puresteel doesn't count).
        _alice.Zones.Battlefield.RemoveCard(a);
        a.SetZone(ZoneType.Graveyard);

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeFalse(
            "Puresteel Paladin is a Creature, not an Artifact, so it cannot " +
            "satisfy its own 'three or more artifacts' threshold");
    }

    [Fact]
    public void ZeroEquipCost_Deactivates_WhenPuresteelLeavesBattlefield()
    {
        var paladin = PuresteelPaladinFactory.Create(_alice, eventBus: _bus, triggers: null);
        _alice.Zones.Battlefield.AddCard(paladin);
        paladin.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Hand, toZone: ZoneType.Battlefield));

        AddArtifact(_alice, "Mox A");
        AddArtifact(_alice, "Mox B");
        AddArtifact(_alice, "Mox C");

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeTrue();

        // Puresteel leaves the battlefield — lifecycle unregisters itself
        // even though the threshold is still met.
        _alice.Zones.Battlefield.RemoveCard(paladin);
        paladin.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(
            card: paladin, fromZone: ZoneType.Battlefield, toZone: ZoneType.Graveyard));

        ZeroEquipCostEffect.IsZeroEquipActiveFor(_alice).Should().BeFalse(
            "with Puresteel no longer on the battlefield its static effect is inert");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static Artifact AddArtifact(Player owner, string name)
    {
        var a = new Artifact(name, "0");
        a.SetOwner(owner);
        a.SetController(owner);
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }
}
