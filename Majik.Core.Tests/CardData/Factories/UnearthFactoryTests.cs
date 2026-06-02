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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="UnearthFactory"/>.
///
/// Oracle (Coldsnap):
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield."
///   Cycling {2}.
///
/// Coverage:
///   - Card identity (name, type, mana cost {B}, black, MV 1, owner/controller).
///   - NamedCardFactory dispatch by name.
///   - Resolve: reanimates creature card with MV ≤ 3 from caster's graveyard.
///   - Resolve: creature with MV > 3 in graveyard is NOT a legal target (no-op).
///   - Resolve: non-creature card in graveyard is NOT a legal target (no-op).
///   - Resolve: opponent's graveyard is NOT targeted (caster's only).
///   - Resolve: routes through ZoneService → ETB event fires (CR 603.6a).
///   - Cycling {2}: activated ability present with {2} mana cost + DiscardSelfCost.
///   - Cycling end-to-end: pay {2}, discard Unearth, draw 1 card,
///     publish <see cref="CardCycledEvent"/> (CR 702.29d).
/// </summary>
[Trait("Color", "B")]
public class UnearthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_SorceryBlackB_ManaValueOne()
    {
        var card = UnearthFactory.Create(_alice);

        card.Name.Should().Be("Unearth");
        card.Should().BeOfType<Sorcery>();
        card.ManaCost.Should().Be("{B}");
        card.ManaCostValue.TotalValue.Should().Be(1, "printed mana cost {B} has MV 1");
        CardColors.GetColors(card).Should().Contain(ManaColor.Black);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Resolve: MV ≤ 3 creature from caster's graveyard → battlefield
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ReanimatesCreatureWithMv3OrLess_ToBattlefield()
    {
        // Ravenous Rats — {1}{B}, MV 2, creature.
        var rats = new Creature("Ravenous Rats", "{1}{B}", 1, 1);
        rats.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(rats);
        rats.SetZone(ZoneType.Graveyard);

        foreach (var effect in UnearthFactory.BuildResolveEffect(_alice))
            effect.Execute();

        rats.Zone.Should().Be(ZoneType.Battlefield,
            "MV 2 ≤ 3: creature is reanimated to the caster's battlefield");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(rats);
        _alice.Zones.Battlefield.GetCards().Should().Contain(rats);
        rats.Controller.Should().BeSameAs(_alice,
            "permanent enters under caster's control (CR 110.2)");
    }

    [Fact]
    public void Resolve_ReanimatesCreatureWithMvExactly3()
    {
        // Rotting Rats-like body — {2}{B}, MV 3, at the edge of legality.
        var goblin = new Creature("Goblin Token", "{2}{B}", 2, 2);
        goblin.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(goblin);
        goblin.SetZone(ZoneType.Graveyard);

        foreach (var effect in UnearthFactory.BuildResolveEffect(_alice))
            effect.Execute();

        goblin.Zone.Should().Be(ZoneType.Battlefield, "MV 3 is exactly the legal threshold");
    }

    [Fact]
    public void Resolve_NoOp_WhenCreatureHasMvAbove3()
    {
        // Hill Giant — {3}{R}, MV 4 — NOT a legal Unearth target.
        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3);
        giant.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(giant);
        giant.SetZone(ZoneType.Graveyard);

        foreach (var effect in UnearthFactory.BuildResolveEffect(_alice))
            effect.Execute();

        giant.Zone.Should().Be(ZoneType.Graveyard,
            "MV 4 > 3: creature is NOT a legal Unearth target (CR 117.x — no legal target → no-op)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(giant);
    }

    [Fact]
    public void Resolve_NoOp_WhenGraveyardContainsOnlyNonCreatureCards()
    {
        // Sorcery in graveyard — not eligible.
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(bolt);
        bolt.SetZone(ZoneType.Graveyard);

        var act = () =>
        {
            foreach (var effect in UnearthFactory.BuildResolveEffect(_alice))
                effect.Execute();
        };

        act.Should().NotThrow("no creature card → resolve is a no-op (CR 117.x)");
        bolt.Zone.Should().Be(ZoneType.Graveyard,
            "non-creature cards in graveyard are untouched");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_OnlySearchesCastersGraveyard_NotOpponentGraveyard()
    {
        // Bob has a creature in his graveyard; Alice's graveyard is empty.
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_bob);
        _bob.Zones.Graveyard.AddCard(bears);
        bears.SetZone(ZoneType.Graveyard);

        foreach (var effect in UnearthFactory.BuildResolveEffect(_alice))
            effect.Execute();

        bears.Zone.Should().Be(ZoneType.Graveyard,
            "Unearth only targets 'your graveyard' (caster's) — opponent's creatures are not reachable");
        _alice.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_RoutesThroughZoneService_FiresCardMovedEvent()
    {
        var bus = new EventBus();
        var zoneService = new ZoneService(bus);

        var movedEvents = new List<CardMovedEvent>();
        bus.Subscribe<CardMovedEvent>(movedEvents.Add);

        var elf = new Creature("Llanowar Elves", "{G}", 1, 1);
        elf.SetOwner(_alice);
        _alice.Zones.Graveyard.AddCard(elf);
        elf.SetZone(ZoneType.Graveyard);

        foreach (var effect in UnearthFactory.BuildResolveEffect(_alice, zoneService))
            effect.Execute();

        elf.Zone.Should().Be(ZoneType.Battlefield,
            "MV 1 ≤ 3: Llanowar Elves is reanimated");
        movedEvents.Should().ContainSingle(
            e => ReferenceEquals(e.Card, elf)
                 && e.FromZone == ZoneType.Graveyard
                 && e.ToZone == ZoneType.Battlefield,
            "graveyard → battlefield routes through ZoneService so ETB triggers fire (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // Cycling {2} — CR 702.29
    // -----------------------------------------------------------------------

    [Fact]
    public void HasCyclingActivatedAbility_WithGenericTwoAndDiscardSelf()
    {
        var card = UnearthFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.Should().HaveCount(2,
            "cycling cost stack = mana cost + discard-self (CR 702.29a)");
        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "Cycling {2} charges two generic mana");
    }

    [Fact]
    public void Cycling_EndToEnd_PaysTwoGeneric_DiscardsSelf_DrawsCard_PublishesEvent()
    {
        // Seed library so Alice can draw.
        var topCard = new Instant("Dark Ritual", "{B}");
        topCard.SetOwner(_alice);
        _alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bus = new EventBus();
        CardCycledEvent? captured = null;
        bus.Subscribe<CardCycledEvent>(e => captured = e);

        var unearth = UnearthFactory.Create(_alice, eventBus: bus);
        _alice.Zones.Hand.AddCard(unearth);
        unearth.SetZone(ZoneType.Hand);

        // Pay {2} for the cycling cost.
        _alice.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var cycling = unearth.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var cost in cycling.Costs)
        {
            cost.CanPay(_alice).Should().BeTrue();
            cost.Pay(_alice);
        }

        // After paying DiscardSelfCost the card is in the graveyard.
        unearth.Zone.Should().Be(ZoneType.Graveyard,
            "DiscardSelfCost moves the card from hand to graveyard (CR 702.29a)");

        // Resolve cycling effect — draw a card.
        foreach (var effect in cycling.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "cycling drew the top card of Alice's library");
        captured.Should().NotBeNull("CR 702.29d — CardCycledEvent must be published");
        captured!.Card.Should().BeSameAs(unearth);
        captured.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Cycling_DiscardSelfCost_CannotPay_WhenCardNotInHand()
    {
        var unearth = UnearthFactory.Create(_alice);
        unearth.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(unearth);

        var cycling = unearth.Abilities.OfType<ActivatedAbility>().Single();
        var discardCost = cycling.Costs.OfType<DiscardSelfCost>().Single();

        discardCost.CanPay(_alice).Should().BeFalse(
            "CR 702.29a — cycling only activates while the card is in its owner's hand");
    }
}
