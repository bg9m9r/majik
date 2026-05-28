using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Stack;
using Majik.Core.StateMachine;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="DevourerOfDestinyFactory"/> (The Brothers'
/// War, {5}{C}{C}).
///
/// Creature — Eldrazi 6/6. Oracle text:
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first upkeep, look at the top four cards of your
///    library. You may put one of those cards back on top of your library.
///    Exile the rest.
///    When you cast this spell, exile target permanent that's one or more
///    colors."
///
/// Covers:
///   - Identity (Creature — Eldrazi, {5}{C}{C}, 6/6, owner / controller).
///   - Colourlessness (CR 105 — only {C} pips, no W/U/B/R/G).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Carries the opening-hand reveal marker keyword.
///   - Opening-hand reveal subscriber:
///       * Yes-answer schedules a delayed first-upkeep trigger.
///       * No-answer schedules nothing.
///       * Delayed trigger fires on the revealer's FIRST upkeep only.
///       * On fire: top 4 peeked; chosen card stays on top; rest exiled.
///       * On fire with null pick: all 4 exiled.
///   - Cast trigger fires on self-cast, requests one colored permanent,
///     active on the stack.
///   - Cast-trigger effect exiles the chosen permanent.
///   - Cast-trigger illegality: colorless permanent not in candidate pool.
/// </summary>
public class DevourerOfDestinyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Devourer_Identity()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);

        devourer.Name.Should().Be("Devourer of Destiny");
        devourer.ManaCost.Should().Be("{5}{C}{C}");
        devourer.HasType(CardType.Creature).Should().BeTrue();
        devourer.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Devourer of Destiny is non-Legendary");
        devourer.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        devourer.BasePower.Should().Be(6);
        devourer.BaseToughness.Should().Be(6);
        devourer.Owner.Should().BeSameAs(_alice);
        devourer.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Devourer_IsColorless()
    {
        // CR 105.2a — {C} pips are colorless mana; they don't make the
        // card a color. Devourer's cost is {5}{C}{C} — only generic and
        // colorless pips, no W/U/B/R/G — so the card is colorless.
        var devourer = DevourerOfDestinyFactory.Create(_alice);

        CardColors.GetColors(devourer).Should().BeEmpty(
            "{5}{C}{C} has no colored pips — Devourer is colorless");
    }

    [Fact]
    public void Devourer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Devourer of Destiny", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Devourer of Destiny");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(6);
        ((Creature)card).BaseToughness.Should().Be(6);
        card.ManaCost.Should().Be("{5}{C}{C}");
    }

    // -----------------------------------------------------------------------
    // Opening-hand reveal marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Devourer_CarriesOpeningHandRevealKeywordMarker()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);

        devourer.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword ==
                OpeningHandRevealLook4Trigger.RevealKeyword,
                "the shared OpeningHandRevealLook4Trigger subscriber " +
                "scans for this marker on game start");
    }

    // -----------------------------------------------------------------------
    // Opening-hand reveal subscriber — direct (no event bus)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RevealSubscriber_AcceptedPrompt_RegistersDelayedFirstUpkeepTrigger()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        PlaceInHand(devourer, _alice);

        var (subscriber, triggers, bus, stack) =
            BuildRevealSubscriber(_alice, YesAgent());

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // Drive Alice's upkeep — the scheduled delayed trigger should
        // become pending exactly once.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1,
            "yes-revealing must register a one-shot first-upkeep trigger");
    }

    [Fact]
    public async Task RevealSubscriber_DeclinedPrompt_RegistersNothing()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        PlaceInHand(devourer, _alice);

        var (subscriber, triggers, bus, _) =
            BuildRevealSubscriber(_alice, NoAgent());
        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(0,
            "declined reveal must not schedule any first-upkeep trigger");
    }

    [Fact]
    public async Task RevealSubscriber_DelayedTrigger_OnlyFires_OnRevealersOwnUpkeep()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        PlaceInHand(devourer, _alice);

        var (subscriber, triggers, bus, _) =
            BuildRevealSubscriber(_alice, YesAgent());
        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // Bob's upkeep doesn't fire it — CR 500.2 each player has their own
        // beginning-of-upkeep step, and the printed text is "YOUR first upkeep".
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _bob));
        triggers.PendingCount.Should().Be(0,
            "trigger is scoped to the revealer's own upkeep, not the opponent's");

        // Alice's draw step doesn't fire it either.
        bus.Publish(new StepStartedEvent(PhaseStateType.Draw, _alice));
        triggers.PendingCount.Should().Be(0,
            "trigger is scoped to upkeep, not draw");

        // Alice's upkeep does fire it.
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PendingCount.Should().Be(1);
    }

    [Fact]
    public async Task RevealSubscriber_DelayedTrigger_FiresOnce_ExilesThreeAndKeepsOne()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        PlaceInHand(devourer, _alice);

        // Seed Alice's library top-down with four distinct cards.
        var top4 = new List<ICard>();
        for (var i = 0; i < 4; i++)
        {
            var c = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            c.SetOwner(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
            top4.Add(c);
        }
        // And a fifth card BELOW the top 4 — must not be touched.
        var below = new Creature("Untouched", "{1}{G}", 2, 2);
        below.SetOwner(_alice);
        below.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(below);

        // Agent picks index 2 of the peeked list to keep on top.
        var pickAgent = new PickIndexAgent(yesNo: true, pickIndex: 2);
        var (subscriber, triggers, bus, stack) =
            BuildRevealSubscriber(_alice, pickAgent);

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        // Drive Alice's first upkeep — trigger goes pending, then resolves
        // off the stack (the DelayedTriggeredAbilityTests pattern).
        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // top4[2] is back on top of the library; the others are exiled;
        // the below-card is still in the library below the kept card.
        _alice.Zones.Library.GetCards().First().Should().BeSameAs(top4[2]);
        top4[2].Zone.Should().Be(ZoneType.Library);

        var exiledNames = _alice.Zones.Exile.GetCards()
            .Select(c => c.Name).ToList();
        exiledNames.Should().BeEquivalentTo(new[] { "Bear0", "Bear1", "Bear3" });
        foreach (var idx in new[] { 0, 1, 3 })
        {
            top4[idx].Zone.Should().Be(ZoneType.Exile);
        }

        // The card below the top-4 wasn't touched.
        below.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Library.GetCards().Should().Contain(below);
    }

    [Fact]
    public async Task RevealSubscriber_DelayedTrigger_NullPick_ExilesAllFour()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        PlaceInHand(devourer, _alice);

        var top4 = new List<ICard>();
        for (var i = 0; i < 4; i++)
        {
            var c = new Creature($"Bear{i}", "{1}{G}", 2, 2);
            c.SetOwner(_alice);
            c.SetZone(ZoneType.Library);
            _alice.Zones.Library.AddCard(c);
            top4.Add(c);
        }

        // Agent declines to keep anything (null pick).
        var pickAgent = new PickIndexAgent(yesNo: true, pickIndex: null);
        var (subscriber, triggers, bus, stack) =
            BuildRevealSubscriber(_alice, pickAgent);

        await subscriber.HandleAsync(new OpeningHandCheckEvent(
            _alice, _alice.Zones.Hand.GetCards().ToList()));

        bus.Publish(new StepStartedEvent(PhaseStateType.Upkeep, _alice));
        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        _alice.Zones.Exile.GetCards().Should().HaveCount(4);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
        foreach (var c in top4) c.Zone.Should().Be(ZoneType.Exile);
    }

    // -----------------------------------------------------------------------
    // Cast trigger — self-cast detection + targeting shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Devourer_CastTrigger_Matches_OnSelfCast()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        var spell = new StubSpell(devourer, _alice);
        var ev = new SpellCastEvent(spell);

        castTrigger.Condition.Matches(ev, castTrigger).Should().BeTrue();
    }

    [Fact]
    public void Devourer_CastTrigger_DoesNotMatch_OnOtherSpellCast()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        var other = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        other.SetOwner(_alice);
        var spell = new StubSpell(other, _alice);
        var ev = new SpellCastEvent(spell);

        castTrigger.Condition.Matches(ev, castTrigger).Should().BeFalse();
    }

    [Fact]
    public void Devourer_CastTrigger_RequestsOneTarget_ActiveOnStack()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        castTrigger.ActiveZones.Should().Contain(ZoneType.Stack);
        castTrigger.TargetRequests.Should().HaveCount(1);
        var req = castTrigger.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Intent.HasAny(BotIntent.Removal).Should().BeTrue();
    }

    [Fact]
    public void Devourer_CastTrigger_CandidateGatherer_OnlyColoredPermanents()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        // Bob has a colored creature, a colorless artifact, and a colored land.
        var coloredCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        coloredCreature.SetOwner(_bob);
        coloredCreature.SetController(_bob);
        coloredCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(coloredCreature);

        var colorlessArtifact = new Artifact("Sol Ring", "{1}");
        colorlessArtifact.SetOwner(_bob);
        colorlessArtifact.SetController(_bob);
        colorlessArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(colorlessArtifact);

        var colorlessEldrazi = new Creature("Endless One", "{X}", 0, 0,
            subtypes: new[] { CardSubtype.Eldrazi });
        colorlessEldrazi.SetOwner(_bob);
        colorlessEldrazi.SetController(_bob);
        colorlessEldrazi.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(colorlessEldrazi);

        // Build a GameContext to drive the gatherer.
        var ctx = new GameContext(
            _alice,
            new[] { _alice, _bob },
            _alice,
            turnNumber: 0,
            currentPhase: PhaseStateType.PreCombatMain,
            stack: new Majik.Core.Stack.Stack());

        var pool = castTrigger.TargetRequests[0].ResolveCandidates(ctx);

        pool.Should().Contain(coloredCreature,
            "Grizzly Bears has {G} — colored");
        pool.Should().NotContain(colorlessArtifact,
            "Sol Ring is colorless — not a legal target");
        pool.Should().NotContain(colorlessEldrazi,
            "colorless Eldrazi are not legal targets for a 'colored permanent' clause");
    }

    [Fact]
    public void Devourer_CastTriggerEffect_ExilesChosenColoredPermanent()
    {
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        var coloredCreature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        coloredCreature.SetOwner(_bob);
        coloredCreature.SetController(_bob);
        coloredCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(coloredCreature);

        castTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { coloredCreature },
        });

        foreach (var ef in castTrigger.Effects) ef.Execute();

        coloredCreature.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(coloredCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(coloredCreature);
    }

    [Fact]
    public void Devourer_CastTriggerEffect_SkipsTarget_IfNoLongerColoredAtResolution()
    {
        // CR 608.2b — illegal-on-resolution check. If the chosen target
        // lost its colors before resolution (e.g. via a Mutavault-style
        // type-changing effect or a colour-stripping aura), the exile
        // should fizzle silently rather than exile a colorless permanent.
        var devourer = DevourerOfDestinyFactory.Create(_alice);
        var castTrigger = GetCastTrigger(devourer);

        // A "colorless permanent" target — colors set is empty.
        var colorless = new Artifact("Sol Ring", "{1}");
        colorless.SetOwner(_bob);
        colorless.SetController(_bob);
        colorless.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(colorless);

        castTrigger.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { colorless },
        });

        foreach (var ef in castTrigger.Effects) ef.Execute();

        colorless.Zone.Should().Be(ZoneType.Battlefield,
            "illegal target at resolution — exile fizzles");
        _bob.Zones.Exile.GetCards().Should().NotContain(colorless);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static TriggeredAbility GetCastTrigger(ICard card) =>
        card.Abilities.OfType<TriggeredAbility>()
            .First(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    private (OpeningHandRevealLook4Trigger Subscriber,
             TriggerManager Triggers,
             EventBus Bus,
             Majik.Core.Stack.Stack Stack)
        BuildRevealSubscriber(Player player, IPlayerAgent agent)
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        var agents = new Dictionary<Player, IPlayerAgent> { [player] = agent };
        return (new OpeningHandRevealLook4Trigger(agents, triggers), triggers, bus, stack);
    }

    private static ScriptedAgent YesAgent()
    {
        var a = new ScriptedAgent();
        a.QueueYesNo(true);
        return a;
    }

    private static ScriptedAgent NoAgent()
    {
        var a = new ScriptedAgent();
        a.QueueYesNo(false);
        return a;
    }

    private static void PlaceInHand(ICard card, Player owner)
    {
        owner.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);
    }

    /// <summary>Minimal agent that always answers <paramref name="yesNo"/>
    /// to <see cref="IPlayerAgent.ChooseYesNoAsync"/> and picks the
    /// candidate at <paramref name="pickIndex"/> (or returns null when
    /// pickIndex is null / out of range) for
    /// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>. Other surfaces
    /// fall through to the IPlayerAgent default implementations / throw.
    /// Used for the look-4 keep/exile coverage tests where ScriptedAgent
    /// (sealed; ChooseLibraryPickAsync isn't queue-driven there) doesn't
    /// expose enough hooks.</summary>
    private sealed class PickIndexAgent : IPlayerAgent
    {
        private readonly bool _yesNo;
        private readonly int? _pickIndex;

        public PickIndexAgent(bool yesNo, int? pickIndex)
        {
            _yesNo = yesNo;
            _pickIndex = pickIndex;
        }

        public Task<bool> ChooseYesNoAsync(
            string question, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult(_yesNo);

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            if (_pickIndex is null || _pickIndex >= candidates.Count)
                return Task.FromResult<ICard?>(null);
            return Task.FromResult<ICard?>(candidates[_pickIndex.Value]);
        }

        // Surfaces unused by the Devourer reveal flow — throw to surface
        // any accidental coupling regressions in future tests.
        public Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<MulliganDecision> ChooseMulliganAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> ChooseXAsync(
            GameContext ctx, ICard source, CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            GameContext ctx, IReadOnlyList<ITriggeredAbility> mine,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx,
            Majik.Core.ValueObjects.ManaCost cost,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<CombatPlan> DeclareAttackersAsync(
            GameContext ctx, IReadOnlyList<Creature> eligibleAttackers,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<BlockPlan> DeclareBlockersAsync(
            GameContext ctx, IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> eligibleBlockers,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => throw new NotImplementedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => throw new NotImplementedException();
    }

    private sealed class StubSpell : ISpell
    {
        public StubSpell(ICard card, Player controller)
        {
            Card = card;
            Controller = controller;
        }

        public ICard Card { get; }
        public Player Controller { get; }
        public Guid Id { get; } = Guid.NewGuid();
        public DateTime Timestamp { get; } = DateTime.UtcNow;
        public bool IsResolving => false;
        public IReadOnlyList<ITarget> Targets { get; } =
            Array.Empty<ITarget>();
        public IReadOnlyList<Majik.Core.Costs.ICost> Costs { get; } =
            Array.Empty<Majik.Core.Costs.ICost>();
        public bool CannotBeCountered => false;
        public void Resolve() { }
    }
}
