using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 702.51 — Convoke. "Each creature you tap while casting this spell
/// pays for {1} or one mana of that creature's color."
///
/// Unit + cast-flow integration tests for <see cref="ConvokeAdditionalCost"/>:
///   - Cost-shape primitives (CanPay / Pay / ApplyTo / AvailableCreatures).
///   - Cast-flow tap-then-reduce wiring against Chord of Calling.
///   - Coloured pips reduced only by matching-colour creatures (CR 702.51b).
///   - Untapped non-selected creatures stay untapped after cast.
///   - Bot probe surfaces Chord with the post-convoke effective cost.
///   - Floor at zero (CR 117.7c).
/// </summary>
public class ConvokeAdditionalCostTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ConvokeAdditionalCostTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Creature SeedCreatureOnBattlefield(
        Player p, string name, string manaCost, bool tapped = false)
    {
        var c = new Creature(name, manaCost, 2, 2);
        c.SetOwner(p);
        c.SetController(p);
        c.SetZone(ZoneType.Battlefield);
        p.Zones.Battlefield.AddCard(c);
        if (tapped) c.Tap();
        return c;
    }

    private Instant SeedChordInHand(Player p)
    {
        var chord = ChordOfCallingFactory.Create(p);
        chord.SetZone(ZoneType.Hand);
        p.Zones.Hand.AddCard(chord);
        return chord;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, turnNumber: 1,
            PhaseStateType.Main, _stack);

    private static SpellDefinition NoOpDef() =>
        new(Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

    private static SpellDefinition XSorceryDef() =>
        new(Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

    // ── primitive tests ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        var act = () => new ConvokeAdditionalCost(null!, Array.Empty<Creature>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullChosen_Throws()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var act = () => new ConvokeAdditionalCost(chord, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReductionAmount_EqualsChosenCount()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "Bear", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "Bear2", "{1}{G}");

        var cost = new ConvokeAdditionalCost(chord, new[] { bear1, bear2 });

        cost.ReductionAmount.Should().Be(2);
        cost.Chosen.Should().BeEquivalentTo(new[] { bear1, bear2 });
    }

    [Fact]
    public void CanPay_AllUntappedCreaturesControlled_True()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "Bear1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "Bear2", "{1}{G}");

        new ConvokeAdditionalCost(chord, new[] { bear1, bear2 })
            .CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_CreatureAlreadyTapped_False()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var tapped = SeedCreatureOnBattlefield(_alice, "Tapped Bear", "{1}{G}", tapped: true);

        new ConvokeAdditionalCost(chord, new[] { tapped })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_OpponentControlled_False()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var foreign = SeedCreatureOnBattlefield(_bob, "Bob's Bear", "{1}{G}");

        new ConvokeAdditionalCost(chord, new[] { foreign })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_DuplicateCreature_False()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear = SeedCreatureOnBattlefield(_alice, "Bear", "{1}{G}");

        new ConvokeAdditionalCost(chord, new[] { bear, bear })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_TapsAllChosenCreatures()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "Bear1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "Bear2", "{1}{G}");

        new ConvokeAdditionalCost(chord, new[] { bear1, bear2 })
            .Pay(_alice).Should().BeTrue();

        bear1.IsTapped.Should().BeTrue();
        bear2.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Pay_IllegalSelection_ReturnsFalse_AndDoesNotTap()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        var foreign = SeedCreatureOnBattlefield(_bob, "Foreign", "{1}{G}");

        new ConvokeAdditionalCost(chord, new[] { foreign })
            .Pay(_alice).Should().BeFalse();

        foreign.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_GenericConsumedFirst()
    {
        // Printed {3}{G}, two green bears tap → both eat generic → {1}{G}.
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "Bear1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "Bear2", "{1}{G}");

        var cost = new ConvokeAdditionalCost(chord, new[] { bear1, bear2 });
        var reduced = cost.ApplyTo(ManaCost.Parse("3G"));

        reduced.Generic.Should().Be(1);
        reduced.Green.Should().Be(1);
    }

    [Fact]
    public void ApplyTo_ColouredPipConsumed_ByMatchingColour()
    {
        // Printed {G}{G}{G}, three green bears tap → all green pips peeled.
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "B1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "B2", "{1}{G}");
        var bear3 = SeedCreatureOnBattlefield(_alice, "B3", "{1}{G}");

        var cost = new ConvokeAdditionalCost(chord, new[] { bear1, bear2, bear3 });
        var reduced = cost.ApplyTo(ManaCost.Parse("GGG"));

        reduced.Generic.Should().Be(0);
        reduced.Green.Should().Be(0);
    }

    [Fact]
    public void ApplyTo_ColouredPipNotConsumed_ByMismatchedColour()
    {
        // Printed {G}{G}{G}, three RED bears tap → no generic to peel,
        // and red bears can't pay green pips per CR 702.51b → cost
        // unchanged (taps still happen but contribute nothing).
        var chord = ChordOfCallingFactory.Create(_alice);
        var rbear1 = SeedCreatureOnBattlefield(_alice, "R1", "{1}{R}");
        var rbear2 = SeedCreatureOnBattlefield(_alice, "R2", "{1}{R}");
        var rbear3 = SeedCreatureOnBattlefield(_alice, "R3", "{1}{R}");

        var cost = new ConvokeAdditionalCost(chord, new[] { rbear1, rbear2, rbear3 });
        var reduced = cost.ApplyTo(ManaCost.Parse("GGG"));

        reduced.Green.Should().Be(3);
        reduced.Generic.Should().Be(0);
    }

    [Fact]
    public void ApplyTo_ColourlessCreature_OnlyContributesToGeneric()
    {
        // Printed {1}{G}, colourless eldrazi spawn taps → eats {1} generic.
        // A second colourless creature has nothing to peel (no generic
        // left, no matching coloured pip) → green stays.
        var chord = ChordOfCallingFactory.Create(_alice);
        var spawn1 = SeedCreatureOnBattlefield(_alice, "Spawn1", "");
        var spawn2 = SeedCreatureOnBattlefield(_alice, "Spawn2", "");

        var cost = new ConvokeAdditionalCost(chord, new[] { spawn1, spawn2 });
        var reduced = cost.ApplyTo(ManaCost.Parse("1G"));

        reduced.Generic.Should().Be(0);
        reduced.Green.Should().Be(1, "colourless creatures can't pay coloured pips per CR 702.51b");
    }

    [Fact]
    public void ApplyTo_MultiColourCreature_ChoosesMatchingPipInWubrgOrder()
    {
        // Printed {W}{U}{B}{R}{G}, one Niv-Mizzet ({2}{U}{R} — Blue+Red).
        // No generic to peel. Tap Niv → first matching colour in WUBRG
        // order = U → consumes {U} → leaves {W}{B}{R}{G}.
        var chord = ChordOfCallingFactory.Create(_alice);
        var niv = SeedCreatureOnBattlefield(_alice, "Niv-Mizzet", "{2}{U}{R}");

        var cost = new ConvokeAdditionalCost(chord, new[] { niv });
        var reduced = cost.ApplyTo(ManaCost.Parse("WUBRG"));

        reduced.White.Should().Be(1);
        reduced.Blue.Should().Be(0);
        reduced.Black.Should().Be(1);
        reduced.Red.Should().Be(1);
        reduced.Green.Should().Be(1);
    }

    [Fact]
    public void ApplyTo_FlooredAtZero_NotNegative()
    {
        // Printed {1}{G}, five green bears tap → consumes 1 generic + 1
        // green, remaining 3 bears contribute nothing (no pips left).
        var chord = ChordOfCallingFactory.Create(_alice);
        var bears = Enumerable.Range(0, 5)
            .Select(i => SeedCreatureOnBattlefield(_alice, $"Bear{i}", "{1}{G}"))
            .ToList();

        var cost = new ConvokeAdditionalCost(chord, bears);
        var reduced = cost.ApplyTo(ManaCost.Parse("1G"));

        reduced.Generic.Should().Be(0);
        reduced.Green.Should().Be(0);
        reduced.TotalValue.Should().Be(0);
    }

    [Fact]
    public void ApplyTo_PreservesXMarker()
    {
        // Printed {X}{G}, the X marker survives convoke reduction.
        var chord = ChordOfCallingFactory.Create(_alice);
        var bear = SeedCreatureOnBattlefield(_alice, "Bear", "{1}{G}");

        var cost = new ConvokeAdditionalCost(chord, new[] { bear });
        var reduced = cost.ApplyTo(ManaCost.Parse("XG"));

        reduced.HasX.Should().BeTrue();
        reduced.Green.Should().Be(0);
    }

    [Fact]
    public void AvailableCreatures_ExcludesTapped_OpponentsAndNonCreatures()
    {
        var bear = SeedCreatureOnBattlefield(_alice, "Bear", "{1}{G}");
        var tappedBear = SeedCreatureOnBattlefield(_alice, "Tapped Bear", "{1}{G}", tapped: true);
        var foreign = SeedCreatureOnBattlefield(_bob, "Bob's Bear", "{1}{G}");

        // Non-creature: a vanilla artifact.
        var art = new Artifact("Sol Ring", "{1}");
        art.SetOwner(_alice);
        art.SetController(_alice);
        art.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(art);

        var pool = ConvokeAdditionalCost.AvailableCreatures(_alice);

        pool.Should().Contain(bear);
        pool.Should().NotContain(tappedBear);
        pool.Should().NotContain(foreign);
    }

    // ── cast-flow integration ───────────────────────────────────────────────

    [Fact]
    public async Task Cast_ChordOfCalling_With3GreenCreatures_EffectiveCostIsGGG_At_XEquals3()
    {
        // Chord of Calling {X}{G}{G}{G}. With X=3 the cost is {3}{G}{G}{G}.
        // Tap 3 green creatures → consume 3 generic → final {G}{G}{G}.
        var chord = SeedChordInHand(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "B1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "B2", "{1}{G}");
        var bear3 = SeedCreatureOnBattlefield(_alice, "B3", "{1}{G}");

        var convoke = ChordOfCallingFactory.BuildAdditionalCost(
            chord, new[] { bear1, bear2, bear3 });
        var agent = new XAndManaCapturingAgent(xValue: 3);

        await _flow.CastAsync(
            _alice, chord, XSorceryDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { convoke });

        agent.LastPromptedCost.Should().NotBeNull();
        // Note: HasX is stripped on cast-flow reduction (X is bound into
        // generic before the convoke fold). Three bears consume 3 generic
        // → 0 generic + 3 green pips remaining.
        agent.LastPromptedCost!.Generic.Should().Be(0);
        agent.LastPromptedCost!.Green.Should().Be(3);

        bear1.IsTapped.Should().BeTrue();
        bear2.IsTapped.Should().BeTrue();
        bear3.IsTapped.Should().BeTrue();

        chord.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public async Task Cast_ChordOfCalling_With2CreaturesAt_XEquals3_EffectiveGenericIsOne()
    {
        // Spec example: X=3 + 2 creatures tapped → pays effective {1}{G}{G}{G}
        // (3 generic - 2 from creatures = 1 generic; coloured pips unchanged
        // because generic is consumed first).
        var chord = SeedChordInHand(_alice);
        var bear1 = SeedCreatureOnBattlefield(_alice, "B1", "{1}{G}");
        var bear2 = SeedCreatureOnBattlefield(_alice, "B2", "{1}{G}");

        var convoke = ChordOfCallingFactory.BuildAdditionalCost(
            chord, new[] { bear1, bear2 });
        var agent = new XAndManaCapturingAgent(xValue: 3);

        await _flow.CastAsync(
            _alice, chord, XSorceryDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { convoke });

        agent.LastPromptedCost!.Generic.Should().Be(1);
        agent.LastPromptedCost!.Green.Should().Be(3);
    }

    [Fact]
    public async Task Cast_ChordOfCalling_UntappedNonSelectedCreatures_NotConsumed()
    {
        var chord = SeedChordInHand(_alice);
        var picked = SeedCreatureOnBattlefield(_alice, "Picked", "{1}{G}");
        var spectator = SeedCreatureOnBattlefield(_alice, "Spectator", "{1}{G}");

        var convoke = ChordOfCallingFactory.BuildAdditionalCost(
            chord, new[] { picked });
        var agent = new XAndManaCapturingAgent(xValue: 0);

        await _flow.CastAsync(
            _alice, chord, XSorceryDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { convoke });

        picked.IsTapped.Should().BeTrue();
        spectator.IsTapped.Should().BeFalse(
            "creatures not chosen for Convoke are untouched (CR 702.51)");
    }

    [Fact]
    public async Task Cast_RejectsAlreadyTappedCreature_Throws()
    {
        var chord = SeedChordInHand(_alice);
        var tapped = SeedCreatureOnBattlefield(_alice, "Tapped Bear", "{1}{G}", tapped: true);

        var convoke = new ConvokeAdditionalCost(chord, new[] { tapped });
        var agent = new XAndManaCapturingAgent(xValue: 0);

        var act = async () => await _flow.CastAsync(
            _alice, chord, XSorceryDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { convoke });

        await act.Should().ThrowAsync<InvalidOperationException>();
        // The card never made it to the stack (cost-payment failure → cast
        // is illegal per CR 601.2g).
        chord.Zone.Should().Be(ZoneType.Hand);
    }

    // ── bot probe ───────────────────────────────────────────────────────────

    [Fact]
    public void ConvokeAltCostProbe_ChordWithUntappedCreatures_YieldsCandidate()
    {
        var chord = SeedChordInHand(_alice);
        SeedCreatureOnBattlefield(_alice, "B1", "{1}{G}");
        SeedCreatureOnBattlefield(_alice, "B2", "{1}{G}");

        var probe = new ConvokeAltCostProbe();
        var candidates = probe.CandidatesFor(chord, _alice, Ctx()).ToList();

        candidates.Should().HaveCount(1);
        var c = candidates[0].Should().BeOfType<ConvokeAlternativeCost>().Subject;
        c.AdditionalCost.Should().NotBeNull();
        c.AdditionalCost!.ReductionAmount.Should().Be(2,
            "default chooser picks min(payable-pip-count, available)");
    }

    [Fact]
    public void ConvokeAltCostProbe_NoCreatures_YieldsNothing()
    {
        var chord = SeedChordInHand(_alice);

        var probe = new ConvokeAltCostProbe();
        probe.CandidatesFor(chord, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void ConvokeAltCostProbe_NonConvokeCard_YieldsNothing()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);
        SeedCreatureOnBattlefield(_alice, "B1", "{1}{G}");

        var probe = new ConvokeAltCostProbe();
        probe.CandidatesFor(bolt, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void ConvokeAltCostProbe_RegisteredInDefaultRegistry()
    {
        var reg = AlternativeCostProbeRegistry.CreateDefault();

        reg.Probes.OfType<ConvokeAltCostProbe>().Should().HaveCount(1,
            "the default registry must include the Convoke probe so the bot's " +
            "alt-cost stream surfaces Chord of Calling-style cards");
    }

    [Fact]
    public void DefaultIsConvokeCard_DetectsChordOfCallingKeywordMarker()
    {
        var chord = ChordOfCallingFactory.Create(_alice);
        ConvokeAltCostProbe.DefaultIsConvokeCard(chord).Should().BeTrue();

        var bolt = new Instant("Lightning Bolt", "{R}");
        ConvokeAltCostProbe.DefaultIsConvokeCard(bolt).Should().BeFalse();
    }

    // ── support: capture cost passed to the mana prompt + X ──────────────────

    /// <summary>
    /// Minimal IPlayerAgent impl that returns a scripted X value and
    /// captures the cost handed to the mana-source prompt.
    /// </summary>
    private sealed class XAndManaCapturingAgent : IPlayerAgent
    {
        private readonly int _xValue;
        public ManaCost? LastPromptedCost { get; private set; }

        public XAndManaCapturingAgent(int xValue) { _xValue = xValue; }

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            LastPromptedCost = cost;
            return Task.FromResult(ManaPayment.Empty);
        }

        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(_xValue);

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
