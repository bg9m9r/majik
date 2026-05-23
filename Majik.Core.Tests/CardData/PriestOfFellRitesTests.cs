using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PriestOfFellRitesFactory"/>.
///
/// Covers:
/// - Identity (name, type, P/T, subtypes, mana cost, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB trigger filter: rejects non-creature cards in graveyard
///   (Lightning Bolt mv 1), accepts a creature with mv ≤ 3 (Bear mv 1).
/// - ETB trigger no-op when graveyard has no eligible target.
/// - ETB trigger routes through ZoneService when supplied so ETB triggers
///   on the reanimated creature fire (CR 603.6a regression — PR #165).
/// - Activated ability shape: {2}{W}{B} mana cost.
/// - Activated ability resolve: exiles Priest from graveyard and
///   reanimates target creature card (no mana-value cap).
/// </summary>
public class PriestOfFellRitesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_Identity()
    {
        var c = PriestOfFellRitesFactory.Create(_alice);

        c.Name.Should().Be("Priest of Fell Rites");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Priest of Fell Rites is a Human");
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue("Priest of Fell Rites is a Cleric");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.ManaCost.Should().Be("{W}{B}");
    }

    [Fact]
    public void PriestOfFellRites_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Priest of Fell Rites", _alice);

        c.Should().BeOfType<Creature>("Priest of Fell Rites is a Creature");
        c.Name.Should().Be("Priest of Fell Rites");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // ETB trigger — eligibility filter
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_EtbTrigger_IgnoresInstantsInGraveyard()
    {
        var alice = new Player("Alice", 20);

        // A non-creature (instant) at mv 1 is not eligible regardless of mv.
        var bolt = new Instant("Lightning Bolt", "R");
        bolt.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var etb = priest.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "Lightning Bolt is an instant — no eligible creature target, so the ETB no-ops");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "the instant must stay in the graveyard — only creature cards reanimate");
        alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
    }

    [Fact]
    public void PriestOfFellRites_EtbTrigger_ReanimatesCreatureWithManaValueAtMostThree()
    {
        var alice = new Player("Alice", 20);

        // A Bear (mv 1 creature) is eligible.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var etb = priest.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield,
            "Bear is a creature with mv ≤ 3 — eligible for the ETB reanimation");
        alice.Zones.Graveyard.GetCards().Should().NotContain(bear);
        alice.Zones.Battlefield.GetCards().Should().Contain(bear);
        bear.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void PriestOfFellRites_EtbTrigger_NoEligibleTarget_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        // A high-mv creature is NOT eligible (mv 4 > 3 cap).
        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(alice);
        var etb = priest.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };

        act.Should().NotThrow("no creature card with mv ≤ 3 in graveyard → no-op (CR 117.x)");
        giant.Zone.Should().Be(ZoneType.Graveyard,
            "Hill Giant has mv 4 — outside the ≤ 3 cap, must remain in graveyard");
        alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    // -----------------------------------------------------------------------
    // ETB trigger — CR 603.6a regression: ZoneService routing
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_EtbTrigger_RoutesThroughZoneService_PublishesCardMovedEvent()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var zones = new ZoneService(eventBus: bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var priest = PriestOfFellRitesFactory.Create(
            alice, zoneService: zones, eventBus: bus, triggers: null);

        var etb = priest.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        bear.Zone.Should().Be(ZoneType.Battlefield);
        movedEvents.Should().ContainSingle(
                e => ReferenceEquals(e.Card, bear)
                    && e.FromZone == ZoneType.Graveyard
                    && e.ToZone == ZoneType.Battlefield,
                "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Activated ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_ActivatedAbility_HasManaCost()
    {
        var priest = PriestOfFellRitesFactory.Create(_alice);

        var ability = priest.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activated ability requires {2}{W}{B} mana");
    }

    // -----------------------------------------------------------------------
    // Activated ability — resolution (graveyard-activated unearth)
    // -----------------------------------------------------------------------

    [Fact]
    public void PriestOfFellRites_ActivatedAbility_ExilesSelf_AndReanimatesTargetCreature()
    {
        var alice = new Player("Alice", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        // Priest is in graveyard (precondition for the activated ability).
        alice.Zones.Graveyard.AddCard(priest);
        priest.SetZone(ZoneType.Graveyard);

        // A target creature card (no mv cap on the activated ability).
        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var ability = priest.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        priest.Zone.Should().Be(ZoneType.Exile,
            "Priest of Fell Rites pays its activation cost by exiling itself from the graveyard");
        alice.Zones.Graveyard.GetCards().Should().NotContain(priest);
        alice.Zones.Exile.GetCards().Should().Contain(priest);

        giant.Zone.Should().Be(ZoneType.Battlefield,
            "the target creature was reanimated to the battlefield");
        alice.Zones.Graveyard.GetCards().Should().NotContain(giant);
        giant.Controller.Should().BeSameAs(alice,
            "the reanimated permanent enters under the activator's control (CR 110.2)");
    }

    [Fact]
    public void PriestOfFellRites_ActivatedAbility_NotInGraveyard_IsNoOp()
    {
        var alice = new Player("Alice", 20);

        var priest = PriestOfFellRitesFactory.Create(alice);
        // Priest is on the battlefield, NOT in graveyard.
        alice.Zones.Battlefield.AddCard(priest);
        priest.SetZone(ZoneType.Battlefield);

        var giant = new Creature("Hill Giant", "3R", 3, 3);
        giant.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        var ability = priest.Abilities.OfType<ActivatedAbility>().Single();
        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow(
            "shape guard — the graveyard-activated ability no-ops when Priest is not in graveyard");
        priest.Zone.Should().Be(ZoneType.Battlefield,
            "Priest must not be moved when activation precondition fails");
        giant.Zone.Should().Be(ZoneType.Graveyard,
            "no reanimation happens without a valid graveyard-zone activation");
    }
}
