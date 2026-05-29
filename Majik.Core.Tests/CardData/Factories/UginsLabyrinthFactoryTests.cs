using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Ugin's Labyrinth (The Brothers' War).
///
/// Land. Oracle text:
///   "Imprint — When this land enters, you may exile a colorless card with
///    mana value 7 or greater from your hand.
///    {T}: Add {C}. If a card is exiled with this land, add {C}{C} instead.
///    {T}: Return the exiled card to its owner's hand."
///
/// Covers:
///   - Identity (Land, "Ugin's Labyrinth", no subtypes, owner/controller).
///   - NamedCardFactory dispatch.
///   - The mana ability produces {C} when nothing is imprinted, {C}{C} when
///     a card is imprinted (CR 605.1 conditional generator).
///   - The {T}: Return activated ability is present.
///   - ETB trigger gated to Battlefield (CR 603.1); fires on enter.
///   - On ETB resolve when agent says YES: a colorless MV>=7 card is exiled
///     from hand and imprinted on the land.
///   - On ETB resolve when agent says NO: nothing imprinted, hand untouched.
///   - Ineligible hand cards (colored, or MV<7) are never offered/exiled.
///   - Return ability: imprinted card goes back to its owner's hand and is
///     no longer imprinted; mana ability reverts to {C}.
/// </summary>
public class UginsLabyrinthFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>A colorless card with the given printed mana cost, in hand.</summary>
    private static Creature ColorlessCard(Player owner, string manaCost, string name)
    {
        var c = new Creature(name, manaCost, 5, 5) { Owner = owner };
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_Identity()
    {
        var land = UginsLabyrinthFactory.Create(_alice);

        land.Name.Should().Be("Ugin's Labyrinth");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void UginsLabyrinth_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ugin's Labyrinth", _alice);

        c.Should().BeOfType<Land>();
        c.Name.Should().Be("Ugin's Labyrinth");
        c.HasType(CardType.Land).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Mana ability (conditional {C} / {C}{C})
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_ManaAbility_ProducesSingleColorless_WhenNoImprint()
    {
        var land = UginsLabyrinthFactory.Create(_alice);

        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.Activate().Generic.Should().Be(1,
            "with nothing exiled the land adds {C}");
    }

    [Fact]
    public void UginsLabyrinth_ManaAbility_ProducesDoubleColorless_WhenCardImprinted()
    {
        var land = UginsLabyrinthFactory.Create(_alice);
        land.AddImprinted(new Creature("Ulamog", "10", 10, 10) { Owner = _alice });

        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.Activate().Generic.Should().Be(2,
            "if a card is exiled with this land, add {C}{C} instead");
    }

    [Fact]
    public void UginsLabyrinth_HasReturnActivatedAbility()
    {
        var land = UginsLabyrinthFactory.Create(_alice);

        // {T}: Return — an ActivatedAbility distinct from the mana ability.
        land.Abilities.OfType<ActivatedAbility>()
            .Should().ContainSingle("{T}: Return the exiled card to its owner's hand");
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_EtbTrigger_GatedToBattlefield()
    {
        var land = UginsLabyrinthFactory.Create(_alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void UginsLabyrinth_EtbTrigger_FiresOnEnter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var land = UginsLabyrinthFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(land, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "Imprint ETB trigger fires on enter");
    }

    // -----------------------------------------------------------------------
    // Resolve — agent says YES (exile + imprint)
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_EtbResolve_YesExilesColorlessMv7CardAndImprints()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var land = UginsLabyrinthFactory.Create(_alice, bus, triggers);
            var ulamog = ColorlessCard(_alice, "10", "Ulamog");
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(land, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            land.ImprintedCards.Should().Contain(ulamog,
                "the chosen colorless MV>=7 card is exiled with the land");
            _alice.Zones.Hand.GetCards().Should().NotContain(ulamog);
            ulamog.Zone.Should().Be(ZoneType.Exile);

            // Mana ability now produces {C}{C}.
            land.Abilities.OfType<ManaAbility>().Single().Activate().Generic.Should().Be(2);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // Resolve — agent says NO (decline the optional exile)
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_EtbResolve_NoLeavesHandUntouched()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var land = UginsLabyrinthFactory.Create(_alice, bus, triggers);
            var ulamog = ColorlessCard(_alice, "10", "Ulamog");
            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(land, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            land.ImprintedCards.Should().BeEmpty("declining the may-exile imprints nothing");
            _alice.Zones.Hand.GetCards().Should().Contain(ulamog, "the card stays in hand");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void UginsLabyrinth_EtbResolve_IneligibleCardsNotExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var land = UginsLabyrinthFactory.Create(_alice, bus, triggers);
            // Colored MV>=7 (white) and colorless MV 3 — both ineligible.
            var colored = new Creature("Akroma", "5WW", 6, 6) { Owner = _alice };
            _alice.Zones.Hand.AddCard(colored);
            colored.SetZone(ZoneType.Hand);
            var smallColorless = ColorlessCard(_alice, "3", "Ornithopter");

            _alice.Zones.Battlefield.AddCard(land);
            land.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(land, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            land.ImprintedCards.Should().BeEmpty(
                "no colorless MV>=7 card exists in hand to exile");
            _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { colored, smallColorless });
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // {T}: Return the exiled card to its owner's hand
    // -----------------------------------------------------------------------

    [Fact]
    public void UginsLabyrinth_ReturnAbility_MovesExiledCardToOwnersHandAndUnimprints()
    {
        var land = UginsLabyrinthFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var ulamog = new Creature("Ulamog", "10", 10, 10) { Owner = _alice };
        _alice.Zones.Exile.AddCard(ulamog);
        ulamog.SetZone(ZoneType.Exile);
        land.AddImprinted(ulamog);

        var returnAbility = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in returnAbility.Effects)
        {
            effect.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().Contain(ulamog,
            "return sends the exiled card back to its owner's hand");
        ulamog.Zone.Should().Be(ZoneType.Hand);
        land.ImprintedCards.Should().BeEmpty("the card is no longer exiled with the land");
        land.Abilities.OfType<ManaAbility>().Single().Activate().Generic.Should().Be(1,
            "with the card returned the mana ability reverts to {C}");
    }
}
