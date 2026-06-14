using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MaraudingMakoFactory"/> (Outlaws of Thunder
/// Junction).
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({R} Creature — Shark Pirate 1/1).
/// - Discard trigger (CR 603.1): discarding one card puts one +1/+1
///   counter on the Mako; discarding N cards puts N counters ("that many").
/// - Lands count too (no nonland gate — CR 701.8).
/// - Opponent discards do NOT grow the Mako ("you discard" — CR 109.5).
/// - Cycling {2} ability shape (CR 702.32 — generic {2} + DiscardSelfCost).
///
/// (Dispatch + well-formedness are covered for every implemented card by
/// CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "R")]
public class MaraudingMakoFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity — CR 205.3m
    // -----------------------------------------------------------------------

    [Fact]
    public void MaraudingMako_Identity_SharkPirate11()
    {
        var card = MaraudingMakoFactory.Create(_alice);

        card.Name.Should().Be("Marauding Mako");
        card.ManaCost.ToString().Should().Be("{R}");
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Shark).Should().BeTrue();
        card.HasSubtype(CardSubtype.Pirate).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cycling {2} ability shape — CR 702.32
    // -----------------------------------------------------------------------

    [Fact]
    public void MaraudingMako_HasCyclingActivatedAbility_WithTwoGenericAndDiscardSelf()
    {
        var card = MaraudingMakoFactory.Create(_alice);
        var cycling = card.Abilities.OfType<ActivatedAbility>().Single();

        cycling.Costs.OfType<DiscardSelfCost>().Should().ContainSingle();

        var mana = cycling.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(2, "cycling {2} charges two generic");
        mana.Red.Should().Be(0, "cycling {2} has no coloured pips");
    }

    // -----------------------------------------------------------------------
    // Discard trigger — CR 603.1 / 701.8
    // -----------------------------------------------------------------------

    private Creature WiredMakoOnBattlefield(IEventBus bus, ReplacementBus? replacements = null)
    {
        var mako = MaraudingMakoFactory.Create(_alice, bus, replacements);
        _alice.Zones.Battlefield.AddCard(mako);
        mako.SetZone(ZoneType.Battlefield);
        return mako;
    }

    private void DiscardCard(Card card, Player owner, IEventBus bus)
    {
        card.SetOwner(owner);
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        owner.Zones.Hand.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(card, ZoneType.Hand, ZoneType.Graveyard));
    }

    [Fact]
    public void MaraudingMako_YouDiscardOneCard_GetsOnePlusOneCounter()
    {
        var bus = new EventBus();
        var mako = WiredMakoOnBattlefield(bus);

        DiscardCard(new Instant("Lightning Bolt", "{R}"), _alice, bus);

        mako.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 603.1 — discarding one card puts one +1/+1 counter on the Mako");
    }

    [Fact]
    public void MaraudingMako_YouDiscardThreeCards_GetsThreePlusOneCounters()
    {
        var bus = new EventBus();
        var mako = WiredMakoOnBattlefield(bus);

        DiscardCard(new Instant("Bolt 1", "{R}"), _alice, bus);
        DiscardCard(new Instant("Bolt 2", "{R}"), _alice, bus);
        DiscardCard(new Instant("Bolt 3", "{R}"), _alice, bus);

        mako.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3,
            "CR 603.1 — 'that many' counters: three discards => three +1/+1 counters");
    }

    [Fact]
    public void MaraudingMako_DiscardLand_StillGrows_NoNonlandGate()
    {
        var bus = new EventBus();
        var mako = WiredMakoOnBattlefield(bus);

        DiscardCard(new Land("Mountain"), _alice, bus);

        mako.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            "CR 701.8 — 'discard one or more cards' counts every card type, lands included");
    }

    [Fact]
    public void MaraudingMako_OpponentDiscards_DoesNotGrow()
    {
        var bus = new EventBus();
        var mako = WiredMakoOnBattlefield(bus);

        DiscardCard(new Instant("Bob's Bolt", "{R}"), _bob, bus);

        mako.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            "'you discard' (CR 109.5) — an opponent's discard does not grow the Mako");
    }
}
