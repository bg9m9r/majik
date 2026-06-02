using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MishrasResearchDeskFactory"/> — Mishra's Research
/// Desk ({1}, Artifact, Modern Horizons 3).
///
/// Oracle text (Scryfall verified):
///   "{1}, {T}, Sacrifice this artifact: Exile the top two cards of your
///    library. Choose one of them. Until the end of your next turn, you may
///    play that card.
///    Unearth {1}{R} ({1}{R}: Return this card from your graveyard to the
///    battlefield. Exile it at the beginning of the next end step or if it
///    would leave the battlefield. Unearth only as a sorcery.)"
///
/// Covers:
/// - Identity (name, Artifact, {1}, colorless, owner/controller).
/// - NamedCardFactory dispatch.
/// - The impulse ability: {1} + Tap + Sacrifice this, no sorcery rider.
/// - Impulse resolve: exiles top two; grants play on exactly ONE (the chosen);
///   the unchosen stays exiled with no grant.
/// - Impulse resolve agent-less: first exiled card gets the grant.
/// - Impulse resolve with single card in library: exiles one, grants it.
/// - Impulse resolve empty library: clean no-op.
/// - Unearth {1}{R}: sorcery-speed activated ability with a {1}{R} mana cost.
/// - Unearth resolve: returns the artifact graveyard → battlefield (no haste —
///   noncreature artifact).
/// </summary>
[Trait("Color", "C")]
public class MishrasResearchDeskFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Card NewCardInLibrary(string name, string cost = "{1}")
    {
        var c = new Card(name, cost);
        c.AddCardType(CardType.Instant);
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static ActivatedAbility ImpulseAbility(Artifact desk) =>
        desk.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<SacrificeSelfCost>().Any());

    private static ActivatedAbility UnearthAbility(Artifact desk) =>
        desk.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_Artifact_CostOne_Colorless()
    {
        var desk = MishrasResearchDeskFactory.Create(_alice);

        desk.Name.Should().Be("Mishra's Research Desk");
        desk.Should().BeOfType<Artifact>();
        desk.HasType(CardType.Artifact).Should().BeTrue();
        desk.ManaCost.Should().Be("{1}");
        desk.Owner.Should().BeSameAs(_alice);
        desk.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MishrasResearchDesk()
    {
        var card = NamedCardFactory.Create("Mishra's Research Desk", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mishra's Research Desk");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void Desk_HasTwoActivatedAbilities_NoManaAbilities()
    {
        var desk = MishrasResearchDeskFactory.Create(_alice);

        desk.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        desk.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the impulse ability and the Unearth ability");
    }

    // -----------------------------------------------------------------------
    // Impulse ability shape — {1}, {T}, Sacrifice this
    // -----------------------------------------------------------------------

    [Fact]
    public void ImpulseAbility_Has_OneMana_Tap_AndSacrifice_NoSorceryRider()
    {
        var desk = MishrasResearchDeskFactory.Create(_alice);
        var impulse = ImpulseAbility(desk);

        impulse.TargetRequests.Should().BeEmpty();
        impulse.IsSorcerySpeed.Should().BeFalse(
            "the impulse ability has no 'activate only as a sorcery' rider");

        impulse.Costs.OfType<ManaCostCost>().Single().Cost.Generic.Should().Be(1,
            "the impulse ability costs {1}");
        impulse.Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap,
                "the impulse ability taps the desk");
        impulse.Costs.OfType<SacrificeSelfCost>().Should().ContainSingle(
            "the impulse ability sacrifices the desk");
    }

    // -----------------------------------------------------------------------
    // Impulse resolve — exile two, grant play on the chosen one only
    // -----------------------------------------------------------------------

    [Fact]
    public void Impulse_AgentLess_ExilesTopTwo_GrantsFirstExiled_Only()
    {
        var top = NewCardInLibrary("Lightning Bolt", "{R}");
        var second = NewCardInLibrary("Dark Ritual", "{B}");

        var desk = MishrasResearchDeskFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(desk);
        desk.SetZone(ZoneType.Battlefield);

        foreach (var e in ImpulseAbility(desk).Effects) e.Execute();

        top.Zone.Should().Be(ZoneType.Exile, "the top two cards are exiled");
        second.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(new ICard[] { top, second });

        // Agent-less fallback grants the FIRST exiled card (deterministic).
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the chosen card may be played until end of your next turn");
        second.RuntimeExileCastAllowedCaster.Should().BeNull(
            "only ONE of the two exiled cards becomes playable");
    }

    [Fact]
    public void Impulse_Agent_ChoosesSecond_GrantsSecondOnly()
    {
        var top = NewCardInLibrary("Lightning Bolt", "{R}");
        var second = NewCardInLibrary("Dark Ritual", "{B}");

        var agent = new ScriptedAgent();
        agent.QueueFromRevealed(second); // choose the second exiled card

        var desk = MishrasResearchDeskFactory.Create(
            _alice, eventBus: null, triggers: null, zoneService: null, agent: agent);
        _alice.Zones.Battlefield.AddCard(desk);
        desk.SetZone(ZoneType.Battlefield);

        foreach (var e in ImpulseAbility(desk).Effects) e.Execute();

        second.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the agent chose the second card to play");
        top.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the unchosen card stays exiled with no play grant");
    }

    [Fact]
    public void Impulse_SingleCardInLibrary_ExilesOne_GrantsIt()
    {
        var only = NewCardInLibrary("Lightning Bolt", "{R}");

        var desk = MishrasResearchDeskFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(desk);
        desk.SetZone(ZoneType.Battlefield);

        foreach (var e in ImpulseAbility(desk).Effects) e.Execute();

        only.Zone.Should().Be(ZoneType.Exile, "CR 121.2 — 'top two' exiles whatever is there");
        only.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the sole exiled card is the chosen one");
    }

    [Fact]
    public void Impulse_EmptyLibrary_CleanNoOp()
    {
        var desk = MishrasResearchDeskFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(desk);
        desk.SetZone(ZoneType.Battlefield);

        foreach (var e in ImpulseAbility(desk).Effects) e.Execute();

        _alice.Zones.Exile.GetCards().Should().BeEmpty(
            "empty library → exile finds nothing (CR 701.20)");
    }

    [Fact]
    public void Impulse_Grant_ClearsOnControllersSecondCleanup()
    {
        var bus = new EventBus();
        var top = NewCardInLibrary("Lightning Bolt", "{R}");
        NewCardInLibrary("Dark Ritual", "{B}");

        var desk = MishrasResearchDeskFactory.Create(
            _alice, eventBus: bus, triggers: null, zoneService: null, agent: null);
        _alice.Zones.Battlefield.AddCard(desk);
        desk.SetZone(ZoneType.Battlefield);

        foreach (var e in ImpulseAbility(desk).Effects) e.Execute();

        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice);

        // CR 514.2 — first controller-owned Cleanup (current turn): grant survives.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeSameAs(_alice,
            "the first cleanup is the current turn — 'until end of your NEXT turn'");

        // Second controller-owned Cleanup (the controller's next turn): clears.
        bus.Publish(new StepStartedEvent(PhaseStateType.Cleanup, _alice));
        top.RuntimeExileCastAllowedCaster.Should().BeNull(
            "the grant clears at the end of the controller's next turn (CR 514.2)");
    }

    // -----------------------------------------------------------------------
    // Unearth {1}{R} — CR 702.85
    // -----------------------------------------------------------------------

    [Fact]
    public void Unearth_HasSorcerySpeedAbility_WithOneGenericOneRed()
    {
        var desk = MishrasResearchDeskFactory.Create(_alice);
        var unearth = UnearthAbility(desk);

        unearth.IsSorcerySpeed.Should().BeTrue("CR 702.85a — Unearth only as a sorcery");
        var mana = unearth.Costs.OfType<ManaCostCost>().Single().Cost;
        mana.Generic.Should().Be(1, "Unearth {1}{R} — one generic");
        mana.Red.Should().Be(1, "Unearth {1}{R} — one red");
        unearth.Source.Should().BeSameAs(desk);
        unearth.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Unearth_Resolve_ReturnsFromGraveyard_NoHasteRider()
    {
        var bus = new EventBus();
        var zoneService = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var desk = MishrasResearchDeskFactory.Create(
            _alice, eventBus: bus, triggers: triggers, zoneService: zoneService, agent: null);
        _alice.Zones.Graveyard.AddCard(desk);
        desk.SetZone(ZoneType.Graveyard);

        foreach (var e in UnearthAbility(desk).Effects) e.Execute();

        desk.Zone.Should().Be(ZoneType.Battlefield,
            "CR 702.85a — unearth returns the artifact from graveyard to battlefield");
        _alice.Zones.Battlefield.GetCards().Should().Contain(desk);
    }

    [Fact]
    public void Unearth_Resolve_RegistersDelayedExileTrigger()
    {
        var bus = new EventBus();
        var zoneService = new ZoneService(bus);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        var desk = MishrasResearchDeskFactory.Create(
            _alice, eventBus: bus, triggers: triggers, zoneService: zoneService, agent: null);
        _alice.Zones.Graveyard.AddCard(desk);
        desk.SetZone(ZoneType.Graveyard);

        foreach (var e in UnearthAbility(desk).Effects) e.Execute();

        // CR 702.85c — at the next end step the artifact is exiled.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));
        triggers.PendingCount.Should().BeGreaterThan(0,
            "the delayed end-step exile trigger fires at the next end step");
    }
}
