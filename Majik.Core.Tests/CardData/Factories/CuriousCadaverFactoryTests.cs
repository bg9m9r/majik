using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CuriousCadaverFactory"/> (Murders at Karlov
/// Manor, {2}{U}{B}).
///
/// Creature — Zombie Detective 3/1. Oracle text (verified against Scryfall):
///   "Flying
///    When you sacrifice a Clue, return this card from your graveyard to your
///    hand."
///
/// Covers:
///   - Identity (Zombie Detective 3/1 at {2}{U}{B}).
///   - Flying keyword (CR 702.9).
///   - Sacrifice-a-Clue trigger structure: active only while Curious Cadaver
///     is in the graveyard (CR 603.6d — a graveyard-resident trigger).
///   - Trigger condition fires only on the OWNER's sacrifice of a CLUE; an
///     opponent's sacrifice, or the owner's sacrifice of a non-Clue, do not
///     fire it.
///   - Mechanic: on resolution Curious Cadaver moves Graveyard → Hand; no-op
///     when it isn't in the graveyard.
///   - Live wiring: registered with a TriggerManager, a qualifying
///     PermanentSacrificedEvent surfaces the trigger as pending.
/// </summary>
[Trait("Color", "M")]
public class CuriousCadaverFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutInGraveyard(Player p, ICard c)
    {
        p.Zones.Graveyard.AddCard(c);
        c.SetZone(ZoneType.Graveyard);
    }

    private static Artifact Clue(Player owner)
    {
        var clue = new Artifact("Clue", "", subtypes: new[] { CardSubtype.Clue });
        clue.SetOwner(owner);
        clue.SetController(owner);
        return clue;
    }

    [Fact]
    public void CuriousCadaver_Identity()
    {
        var c = CuriousCadaverFactory.Create(_alice);

        c.Name.Should().Be("Curious Cadaver");
        c.ManaCost.Should().Be("{2}{U}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.HasSubtype(CardSubtype.Detective).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CuriousCadaver_HasFlying()
    {
        var c = CuriousCadaverFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Curious Cadaver has Flying");
    }

    // -----------------------------------------------------------------------
    // Graveyard-resident trigger — CR 603.6d
    // -----------------------------------------------------------------------

    [Fact]
    public void CuriousCadaver_Trigger_IsActiveOnlyInGraveyard()
    {
        var c = CuriousCadaverFactory.Create(_alice);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ActiveZones.Should().Contain(ZoneType.Graveyard);
        trigger.ActiveZones.Should().NotContain(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Trigger condition — "When you sacrifice a Clue"
    // -----------------------------------------------------------------------

    [Fact]
    public void CuriousCadaver_Condition_FiresOnOwnerSacrificesClue()
    {
        var c = CuriousCadaverFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var clue = Clue(_alice);
        var ev = new PermanentSacrificedEvent(clue, _alice, wasToken: true);

        trigger.Condition.Matches(ev, trigger).Should().BeTrue(
            "the owner sacrificed a Clue — the recursion trigger fires");
    }

    [Fact]
    public void CuriousCadaver_Condition_IgnoresOpponentSacrificesClue()
    {
        var c = CuriousCadaverFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var clue = Clue(_bob);
        var ev = new PermanentSacrificedEvent(clue, _bob, wasToken: true);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "CR 109.5 — 'you sacrifice' is scoped to the owner; an opponent's " +
            "Clue sacrifice does not fire the trigger");
    }

    [Fact]
    public void CuriousCadaver_Condition_IgnoresOwnerSacrificesNonClue()
    {
        var c = CuriousCadaverFactory.Create(_alice);
        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();

        var treasure = new Artifact("Treasure", "", subtypes: new[] { CardSubtype.Treasure });
        treasure.SetOwner(_alice);
        treasure.SetController(_alice);
        var ev = new PermanentSacrificedEvent(treasure, _alice, wasToken: true);

        trigger.Condition.Matches(ev, trigger).Should().BeFalse(
            "'a Clue' — sacrificing a non-Clue permanent does not fire the trigger");
    }

    // -----------------------------------------------------------------------
    // Resolution — returns self from graveyard to hand
    // -----------------------------------------------------------------------

    [Fact]
    public void CuriousCadaver_Resolution_ReturnsFromGraveyardToHand()
    {
        var c = CuriousCadaverFactory.Create(_alice);
        PutInGraveyard(_alice, c);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        c.Zone.Should().Be(ZoneType.Hand,
            "the trigger returns Curious Cadaver from graveyard to its owner's hand");
        _alice.Zones.Hand.GetCards().Should().Contain(c);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(c);
    }

    [Fact]
    public void CuriousCadaver_Resolution_NoOp_WhenNotInGraveyard()
    {
        // CR 603.6d — the return is re-checked at resolution. If Curious
        // Cadaver is no longer in the graveyard, nothing happens.
        var c = CuriousCadaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var trigger = c.Abilities.OfType<TriggeredAbility>().Single();
        var act = () => { foreach (var e in trigger.Effects) e.Execute(); };

        act.Should().NotThrow();
        c.Zone.Should().Be(ZoneType.Battlefield,
            "resolution re-checks the graveyard zone — an off-zone activation is a no-op");
        _alice.Zones.Hand.GetCards().Should().NotContain(c);
    }

    // -----------------------------------------------------------------------
    // Live wiring — registered trigger surfaces as pending
    // -----------------------------------------------------------------------

    [Fact]
    public void CuriousCadaver_LiveWiring_OwnerSacrificesClue_RegistersPendingTrigger()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var cadaver = CuriousCadaverFactory.Create(_alice, zoneService: null, triggers: triggers);
        PutInGraveyard(_alice, cadaver);

        // Bob sacrifices a Clue — does NOT trigger (only the owner's own).
        var bobClue = Clue(_bob);
        bus.Publish(new PermanentSacrificedEvent(bobClue, _bob, wasToken: true));
        triggers.PendingCount.Should().Be(0,
            "Curious Cadaver only triggers when its owner sacrifices a Clue");

        // Alice sacrifices a Clue — trigger surfaces as pending.
        var aliceClue = Clue(_alice);
        bus.Publish(new PermanentSacrificedEvent(aliceClue, _alice, wasToken: true));
        triggers.PendingCount.Should().Be(1);
    }
}
