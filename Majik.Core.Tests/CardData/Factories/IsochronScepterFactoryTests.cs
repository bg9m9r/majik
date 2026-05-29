using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Isochron Scepter (Mirrodin).
///
/// Artifact — {2}. Oracle text (verified against Scryfall 2026-05-29):
///   "Imprint — When this artifact enters, you may exile an instant card with
///    mana value 2 or less from your hand.
///    {2}, {T}: You may copy the exiled card. If you do, you may cast the copy
///    without paying its mana cost."
///
/// Covers:
///   - Identity (Artifact, "Isochron Scepter", {2}, owner/controller).
///   - NamedCardFactory dispatch.
///   - Imprint ETB trigger (CR 603.1 / CR 702.49), gated to Battlefield; fires
///     on enter.
///   - ETB resolve agent YES: an instant MV<=2 card is exiled from hand and
///     imprinted on the Scepter.
///   - ETB resolve agent NO: nothing imprinted, hand untouched.
///   - Ineligible hand cards (non-instant, or MV>2) are never offered/exiled.
///   - The {2},{T} activated ability is present with a ManaCostCost({2}) + a
///     tap cost.
///   - Activated-ability resolve: the imprinted card's bound SpellDefinition
///     effects run in place (CR 707.10 copy + cast for free), and the
///     imprinted card itself stays in exile (a copy is cast, not the card).
///   - Activated-ability resolve with nothing imprinted: clean no-op.
/// </summary>
public class IsochronScepterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>An instant with the given printed mana cost, in hand.</summary>
    private static Instant InstantInHand(Player owner, string manaCost, string name)
    {
        var c = new Instant(name, manaCost) { Owner = owner };
        owner.Zones.Hand.AddCard(c);
        c.SetZone(ZoneType.Hand);
        return c;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_Identity()
    {
        var scepter = IsochronScepterFactory.Create(_alice);

        scepter.Name.Should().Be("Isochron Scepter");
        scepter.HasType(CardType.Artifact).Should().BeTrue();
        scepter.ManaCost.Should().Be("{2}");
        scepter.Owner.Should().BeSameAs(_alice);
        scepter.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IsochronScepter_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Isochron Scepter", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Isochron Scepter");
        c.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {2}, {T}: activated ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_HasActivatedAbility_WithManaAndTapCosts()
    {
        var scepter = IsochronScepterFactory.Create(_alice);

        var ability = scepter.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();
        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            "{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_EtbTrigger_GatedToBattlefield()
    {
        var scepter = IsochronScepterFactory.Create(_alice);
        var trigger = scepter.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void IsochronScepter_EtbTrigger_FiresOnEnter()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var scepter = IsochronScepterFactory.Create(_alice, bus, triggers);
        _alice.Zones.Battlefield.AddCard(scepter);
        scepter.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(scepter, ZoneType.Stack, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "Imprint ETB trigger fires on enter");
    }

    // -----------------------------------------------------------------------
    // ETB resolve — agent says YES (exile + imprint)
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_EtbResolve_YesExilesInstantMv2CardAndImprints()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var scepter = IsochronScepterFactory.Create(_alice, bus, triggers);
            var bolt = InstantInHand(_alice, "{R}", "Lightning Bolt");
            _alice.Zones.Battlefield.AddCard(scepter);
            scepter.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(scepter, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            scepter.ImprintedCards.Should().Contain(bolt,
                "the chosen instant MV<=2 card is exiled with the Scepter");
            _alice.Zones.Hand.GetCards().Should().NotContain(bolt);
            bolt.Zone.Should().Be(ZoneType.Exile);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // ETB resolve — agent says NO (decline the optional exile)
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_EtbResolve_NoLeavesHandUntouched()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var scepter = IsochronScepterFactory.Create(_alice, bus, triggers);
            var bolt = InstantInHand(_alice, "{R}", "Lightning Bolt");
            _alice.Zones.Battlefield.AddCard(scepter);
            scepter.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(scepter, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            scepter.ImprintedCards.Should().BeEmpty("declining the may-exile imprints nothing");
            _alice.Zones.Hand.GetCards().Should().Contain(bolt, "the card stays in hand");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void IsochronScepter_EtbResolve_IneligibleCardsNotExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        AgentRegistry.Set(_alice, agent);
        try
        {
            var scepter = IsochronScepterFactory.Create(_alice, bus, triggers);
            // Sorcery MV 1 (wrong type) and instant MV 3 (too expensive) — both ineligible.
            var sorcery = new Sorcery("Duress", "{B}") { Owner = _alice };
            _alice.Zones.Hand.AddCard(sorcery);
            sorcery.SetZone(ZoneType.Hand);
            var bigInstant = InstantInHand(_alice, "{1}{U}{U}", "Cancel");

            _alice.Zones.Battlefield.AddCard(scepter);
            scepter.SetZone(ZoneType.Battlefield);

            bus.Publish(new CardMovedEvent(scepter, ZoneType.Stack, ZoneType.Battlefield));
            triggers.PutPendingTriggersOnStack(_alice);
            stack.Pop()!.Resolve();

            scepter.ImprintedCards.Should().BeEmpty(
                "no instant MV<=2 card exists in hand to exile");
            _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { sorcery, bigInstant });
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // {2},{T}: copy the exiled card and cast the copy for free
    // -----------------------------------------------------------------------

    [Fact]
    public void IsochronScepter_Activate_CopiesAndCastsExiledCardForFree()
    {
        // Imprinted instant sitting in exile.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Exile);
        _alice.Zones.Exile.AddCard(bolt);

        int copyExecutions = 0;
        SpellDefinition? Lookup(ICard card) =>
            new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect("test-copy-sentinel", () => copyExecutions++),
                });

        var scepter = IsochronScepterFactory.Create(_alice);
        scepter.AddImprinted(bolt);

        var effects = IsochronScepterFactory.BuildActivatedEffects(scepter, _alice, Lookup);
        foreach (var e in effects) e.Execute();

        copyExecutions.Should().Be(1,
            "the exiled card is copied and the copy is cast for free — its effects run once");
        bolt.Zone.Should().Be(ZoneType.Exile,
            "a copy is cast; the imprinted card itself stays exiled with the Scepter");
        scepter.ImprintedCards.Should().Contain(bolt, "the imprint persists for reuse");
    }

    [Fact]
    public void IsochronScepter_Activate_NothingImprinted_IsCleanNoOp()
    {
        int copyExecutions = 0;
        SpellDefinition? Lookup(ICard card) =>
            new SpellDefinition(
                Modes: Array.Empty<string>(),
                HasVariableX: false,
                TargetRequests: Array.Empty<TargetRequest>(),
                EffectFactory: _ => new IEffect[]
                {
                    new Effect("test-copy-sentinel", () => copyExecutions++),
                });

        var scepter = IsochronScepterFactory.Create(_alice);

        var effects = IsochronScepterFactory.BuildActivatedEffects(scepter, _alice, Lookup);
        foreach (var e in effects) e.Execute();

        copyExecutions.Should().Be(0, "nothing imprinted — no copy fires");
    }
}
