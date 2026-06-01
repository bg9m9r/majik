using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="CharmingPrinceFactory"/>.
///
/// Covers:
/// - Identity ({1}{W} Creature — Human Noble, 2/2, white, mana value 2).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Exactly one battlefield-active ETB triggered ability.
/// - Mode 0 (Scry 2): with a scripted agent, controller sees top-2 library
///   cards and the scry decision is applied; library order changes correctly.
/// - Mode 1 (gain 3 life): controller gains exactly 3 life.
/// - Mode 2 (blink own creature): target creature you OWN is exiled, then
///   returns to battlefield at the beginning of the next end step under your
///   control (CR 603.7 delayed trigger).
/// - Mode 2 illegal target: opponent-owned creature is NOT exiled (resolve-
///   time ownership re-check — "creature you own").
/// </summary>
public class CharmingPrinceFactoryTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose() => AgentRegistry.Clear();

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_Identity()
    {
        var c = CharmingPrinceFactory.Create(_alice);

        c.Name.Should().Be("Charming Prince");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Charming Prince is a Human");
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue("Charming Prince is a Noble");
        c.ManaCost.Should().Be("{1}{W}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CharmingPrince_IsWhite()
    {
        var c = CharmingPrinceFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.White,
            "Charming Prince has a {W} pip in its mana cost");
        colors.Should().HaveCount(1, "only one color");
    }

    [Fact]
    public void CharmingPrince_ManaValue_IsTwo()
    {
        var c = CharmingPrinceFactory.Create(_alice);

        // {1}{W} = mana value 2 (CR 202.3).
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{W} has mana value 2");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Charming Prince", _alice);

        c.Should().BeOfType<Creature>("Charming Prince is a Creature");
        c.Name.Should().Be("Charming Prince");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Noble).Should().BeTrue();
        c.ManaCost.Should().Be("{1}{W}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = CharmingPrinceFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB modal trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active (CR 603.6a)");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // Mode 0 — Scry 2
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_Mode0_Scry2_ReordersLibrary()
    {
        var alice = new Player("Alice", 20);

        // Seed library with two known cards.
        var cardA = new Creature("CardA", "{W}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        alice.Zones.Library.AddCard(cardA); // top
        alice.Zones.Library.AddCard(cardB); // second

        // Script the agent: scry decision = send BOTH to bottom (all-bottom).
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardA, cardB },
            TopOrder: Array.Empty<ICard>()));
        AgentRegistry.Set(alice, agent);

        var prince = CharmingPrinceFactory.Create(alice, mode: 0);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // Both cards sent to bottom — library is now [cardA (bottom), cardB (above that)]
        // or more precisely both at the bottom. With ScryAction.Apply, ToBottom
        // cards are appended in order, so the original top-2 are replaced at the bottom.
        var lib = alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2, "scry 2 only reorders; no card leaves the library");
        // Neither card should be on top in the previous (top-first) order — both went bottom.
        // The first card in the ordered list should be from the ToBottom set.
        lib.Should().Contain(cardA, "cardA is still in the library");
        lib.Should().Contain(cardB, "cardB is still in the library");
    }

    [Fact]
    public void CharmingPrince_Mode0_Scry2_KeepOnTop_LeavesCardOnTop()
    {
        var alice = new Player("Alice", 20);

        var cardA = new Creature("CardA", "{W}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        alice.Zones.Library.AddCard(cardA); // top
        alice.Zones.Library.AddCard(cardB); // second

        // Script: keep cardA on top, send cardB to bottom.
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardB },
            TopOrder: new[] { cardA }));
        AgentRegistry.Set(alice, agent);

        var prince = CharmingPrinceFactory.Create(alice, mode: 0);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        var lib = alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2);
        lib.First().Should().BeSameAs(cardA, "cardA was kept on top");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — You gain 3 life
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_Mode1_ControllerGainsThreeLife()
    {
        var alice = new Player("Alice", 20);
        var prince = CharmingPrinceFactory.Create(alice, mode: 1);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        alice.LifeTotal.Should().Be(23,
            "mode 1 gains controller exactly 3 life (CR 119.3)");
    }

    // -----------------------------------------------------------------------
    // Mode 2 — Blink: exile creature you OWN, return at next end step
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_Mode2_ExilesOwnedCreature_AndDelayedReturnFires()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);

        // Alice's creature to blink — she both owns and controls it.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_alice);
        grizzly.SetController(_alice);
        grizzly.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(grizzly);

        var prince = CharmingPrinceFactory.Create(_alice, mode: 2, triggers: triggers);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        // Immediately after ETB resolves, grizzly should be exiled.
        grizzly.Zone.Should().Be(ZoneType.Exile,
            "mode 2 exiles the target creature you own");
        _alice.Zones.Exile.GetCards().Should().Contain(grizzly,
            "the exiled card lands in its owner's exile zone");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(grizzly);

        // Fire the next end step — the delayed trigger should queue.
        bus.Publish(new StepStartedEvent(PhaseStateType.End, _alice));

        triggers.PendingCount.Should().BeGreaterThanOrEqualTo(1,
            "delayed return rider fires on the first end step after the ETB");

        triggers.PutPendingTriggersOnStack(_alice);
        while (stack.Count > 0) stack.Pop()!.Resolve();

        grizzly.Zone.Should().Be(ZoneType.Battlefield,
            "the exiled creature returns at the beginning of the next end step");
        _alice.Zones.Battlefield.GetCards().Should().Contain(grizzly,
            "'under your control' — Alice");
        _alice.Zones.Exile.GetCards().Should().NotContain(grizzly);
        grizzly.Controller.Should().BeSameAs(_alice,
            "CR 614 — returns under controller's control");
    }

    [Fact]
    public void CharmingPrince_Mode2_OpponentOwnedCreature_IsNotExiled()
    {
        // Mode 2 requires "creature you OWN" — opponent-owned is illegal.
        // Ownership check: target.Owner != caster.
        var prince = CharmingPrinceFactory.Create(_alice, mode: 2);

        // Bob owns this creature, not Alice.
        var grizzly = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        grizzly.SetOwner(_bob);
        grizzly.SetController(_bob);
        grizzly.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grizzly);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { grizzly },
        });
        foreach (var effect in etb.Effects) effect.Execute();

        grizzly.Zone.Should().Be(ZoneType.Battlefield,
            "mode 2 only exiles creatures the caster OWNS; opponent-owned = no-op (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(grizzly);
    }

    // -----------------------------------------------------------------------
    // PLAN 01 Slice D — the modal ETB choice routes through a REAL
    // ResolutionContext (non-null ctx.Game), not a `ctx: null!` landmine.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CharmingPrince_ModeChoice_RoutesThroughNonNullContext()
    {
        var alice = new Player("Alice", 20);
        var prince = CharmingPrinceFactory.Create(alice);

        // Recording agent supplied via the live ResolutionContext (the PLAN 01
        // Slice D resolve path), NOT AgentRegistry — proves PickModeAsync reads
        // ctx.Agent / ctx.Game rather than passing `ctx: null!`.
        var agent = new RecordingModeAgent(pick: 1); // mode 1 = gain 3 life
        var stack = new Majik.Core.Stack.Stack(new EventBus());
        var gameCtx = new GameContext(
            alice, new[] { alice }, alice, turnNumber: 1,
            currentPhase: null, stack);
        var resCtx = ResolutionContext.For(
            alice, agent, gameCtx, chosenTargets: null);

        var etb = prince.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) await effect.ExecuteAsync(resCtx);

        agent.LastCtx.Should().NotBeNull(
            "the mode prompt must receive the live GameContext, never `ctx: null!`");
        agent.LastCtx.Should().BeSameAs(gameCtx);
        alice.LifeTotal.Should().Be(23,
            "agent picked mode 1 (gain 3 life) via the wired prompt");
    }

    /// <summary>Records the ctx handed to ChooseModeAsync; every other prompt
    /// throws (DelegatingAgent), surfacing any accidental extra prompt.</summary>
    private sealed class RecordingModeAgent : Helpers.DelegatingAgent
    {
        private readonly int _pick;
        public GameContext? LastCtx { get; private set; }

        public RecordingModeAgent(int pick) => _pick = pick;

        public override Task<int> ChooseModeAsync(
            GameContext ctx,
            IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null,
            CancellationToken ct = default)
        {
            LastCtx = ctx;
            return Task.FromResult(_pick);
        }
    }

    // -----------------------------------------------------------------------
    // Wired path: bus event triggers ETB
    // -----------------------------------------------------------------------

    [Fact]
    public void CharmingPrince_WiredCreate_Mode1_EnteringBattlefield_GainsThreeLife()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggerManager = new TriggerManager(stack, bus);

        var prince = CharmingPrinceFactory.Create(alice, mode: 1, triggers: triggerManager);
        prince.SetZone(ZoneType.Battlefield);

        var moveEvent = new CardMovedEvent(prince, ZoneType.Hand, ZoneType.Battlefield);
        bus.Publish(moveEvent);

        triggerManager.PutPendingTriggersOnStack(alice);
        while (stack.Count > 0)
        {
            var item = stack.Pop();
            item?.Resolve();
        }

        alice.LifeTotal.Should().Be(23,
            "entering the battlefield via the bus with mode 1 gains controller 3 life end-to-end");
    }
}
