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

namespace Majik.Core.Tests.Costs;

/// <summary>
/// CR 702.127 — Improvise. "Your artifacts can help cast this spell. Each
/// artifact you tap after you're done activating mana abilities pays for {1}."
///
/// Unit + cast-flow integration tests covering:
///   - Cost-shape primitives (CanPay / Pay / ApplyTo / AvailableArtifacts).
///   - Cast-flow tap-then-reduce wiring against Kappa Cannoneer.
///   - Untapped non-selected artifacts stay untapped after cast.
///   - Coloured pips preserved (only generic reduces).
///   - Bot probe surfaces Kappa with the post-improvise effective cost.
/// </summary>
public class ImproviseAdditionalCostTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ImproviseAdditionalCostTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Artifact SeedArtifactOnBattlefield(Player p, string name, bool tapped = false)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(p);
        a.SetController(p);
        a.SetZone(ZoneType.Battlefield);
        p.Zones.Battlefield.AddCard(a);
        if (tapped) a.Tap();
        return a;
    }

    private Creature SeedKappaInHand(Player p)
    {
        var kappa = KappaCannoneerFactory.Create(p);
        kappa.SetZone(ZoneType.Hand);
        p.Zones.Hand.AddCard(kappa);
        return kappa;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, turnNumber: 1,
            PhaseStateType.Main, _stack);

    private static SpellDefinition NoOpDef() =>
        new(Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => Array.Empty<IEffect>());

    // ── primitive tests ──────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullSource_Throws()
    {
        var act = () => new ImproviseAdditionalCost(null!, Array.Empty<Permanent>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullChosen_Throws()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var act = () => new ImproviseAdditionalCost(kappa, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReductionAmount_EqualsChosenCount()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "Sol Ring");
        var b = SeedArtifactOnBattlefield(_alice, "Mox Opal");

        var cost = new ImproviseAdditionalCost(kappa, new Permanent[] { a, b });

        cost.ReductionAmount.Should().Be(2);
        cost.Chosen.Should().BeEquivalentTo(new[] { a, b });
    }

    [Fact]
    public void CanPay_AllUntappedArtifactsControlled_True()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "Sol Ring");
        var b = SeedArtifactOnBattlefield(_alice, "Mox Opal");

        new ImproviseAdditionalCost(kappa, new Permanent[] { a, b })
            .CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void CanPay_ArtifactAlreadyTapped_False()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var tapped = SeedArtifactOnBattlefield(_alice, "Sol Ring", tapped: true);

        new ImproviseAdditionalCost(kappa, new Permanent[] { tapped })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_OpponentControlled_False()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var foreign = SeedArtifactOnBattlefield(_bob, "Foreign Sol Ring");

        new ImproviseAdditionalCost(kappa, new Permanent[] { foreign })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_NonArtifactPermanent_False()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        new ImproviseAdditionalCost(kappa, new Permanent[] { bear })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void CanPay_DuplicateArtifact_False()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "Sol Ring");

        new ImproviseAdditionalCost(kappa, new Permanent[] { a, a })
            .CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void Pay_TapsAllChosenArtifacts()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "Sol Ring");
        var b = SeedArtifactOnBattlefield(_alice, "Mox Opal");

        var cost = new ImproviseAdditionalCost(kappa, new Permanent[] { a, b });
        cost.Pay(_alice).Should().BeTrue();

        a.IsTapped.Should().BeTrue();
        b.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Pay_IllegalSelection_ReturnsFalse_AndDoesNotTap()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var foreign = SeedArtifactOnBattlefield(_bob, "Foreign");

        new ImproviseAdditionalCost(kappa, new Permanent[] { foreign })
            .Pay(_alice).Should().BeFalse();

        foreign.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void ApplyTo_ReducesGenericOnly_PreservesColored()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "A");
        var b = SeedArtifactOnBattlefield(_alice, "B");
        var cost = new ImproviseAdditionalCost(kappa, new Permanent[] { a, b });

        var reduced = cost.ApplyTo(ManaCost.Parse("{5}{U}"));

        reduced.Generic.Should().Be(3);
        reduced.Blue.Should().Be(1);
    }

    [Fact]
    public void ApplyTo_ExceedsGeneric_FloorsAtZero_NotNegative()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        var cards = Enumerable.Range(0, 10)
            .Select(i => SeedArtifactOnBattlefield(_alice, $"A{i}"))
            .Cast<Permanent>().ToList();
        var cost = new ImproviseAdditionalCost(kappa, cards);

        var reduced = cost.ApplyTo(ManaCost.Parse("{2}{U}"));

        reduced.Generic.Should().Be(0);
        reduced.Blue.Should().Be(1);
    }

    [Fact]
    public void AvailableArtifacts_IncludesArtifactCreatures_ExcludesTapped_OpponentsAndNonArtifacts()
    {
        var solRing = SeedArtifactOnBattlefield(_alice, "Sol Ring");
        var tappedRock = SeedArtifactOnBattlefield(_alice, "Tapped Rock", tapped: true);

        // Artifact creature — selected via HasType(Artifact), not OfType<Artifact>.
        var artifactCreature = KappaCannoneerFactory.Create(_alice);
        artifactCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(artifactCreature);

        var foreign = SeedArtifactOnBattlefield(_bob, "Foreign Sol Ring");

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bear.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(bear);

        var pool = ImproviseAdditionalCost.AvailableArtifacts(_alice);

        pool.Should().Contain(solRing);
        pool.Should().Contain(artifactCreature);
        pool.Should().NotContain(tappedRock);
        pool.Should().NotContain(foreign);
        pool.Should().NotContain(bear);
    }

    // ── cast-flow integration ───────────────────────────────────────────────

    [Fact]
    public async Task Cast_KappaCannoneer_With3Artifacts_EffectiveCostIs2U_AndArtifactsTapped()
    {
        var kappa = SeedKappaInHand(_alice);
        var a = SeedArtifactOnBattlefield(_alice, "Sol Ring");
        var b = SeedArtifactOnBattlefield(_alice, "Mox Opal");
        var c = SeedArtifactOnBattlefield(_alice, "Lotus Petal");

        var improvise = KappaCannoneerFactory.BuildAdditionalCost(
            kappa, new Permanent[] { a, b, c });

        var agent = new CapturingManaAgent();

        var spell = await _flow.CastAsync(
            _alice, kappa, NoOpDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        // Printed {5}{U} → {2}{U} after tapping 3 artifacts.
        agent.LastPromptedCost.Should().NotBeNull();
        agent.LastPromptedCost!.Generic.Should().Be(2);
        agent.LastPromptedCost!.Blue.Should().Be(1);

        a.IsTapped.Should().BeTrue();
        b.IsTapped.Should().BeTrue();
        c.IsTapped.Should().BeTrue();

        kappa.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public async Task Cast_KappaCannoneer_With4Artifacts_EffectiveCostIs1U()
    {
        var kappa = SeedKappaInHand(_alice);
        var artifacts = Enumerable.Range(0, 4)
            .Select(i => SeedArtifactOnBattlefield(_alice, $"A{i}"))
            .Cast<Permanent>().ToList();

        var improvise = KappaCannoneerFactory.BuildAdditionalCost(kappa, artifacts);
        var agent = new CapturingManaAgent();

        await _flow.CastAsync(
            _alice, kappa, NoOpDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        agent.LastPromptedCost!.Generic.Should().Be(1);
        agent.LastPromptedCost!.Blue.Should().Be(1);
    }

    [Fact]
    public async Task Cast_KappaCannoneer_UntappedNonSelectedArtifacts_NotConsumed()
    {
        var kappa = SeedKappaInHand(_alice);
        var picked = SeedArtifactOnBattlefield(_alice, "Picked");
        var spectator = SeedArtifactOnBattlefield(_alice, "Spectator");

        var improvise = KappaCannoneerFactory.BuildAdditionalCost(
            kappa, new Permanent[] { picked });

        var agent = new CapturingManaAgent();
        await _flow.CastAsync(
            _alice, kappa, NoOpDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        picked.IsTapped.Should().BeTrue();
        spectator.IsTapped.Should().BeFalse(
            "artifacts not chosen for Improvise are untouched (CR 702.127)");
    }

    [Fact]
    public async Task Cast_KappaCannoneer_ManaAbilityTimingGate_ImproviseAfterManaSettled()
    {
        // CR 605.1 + CR 702.127 — Improvise's tap-for-{1} fires AFTER the
        // mana-payment prompt has been satisfied. SpellCastFlow's order is:
        //   1. additional-cost loop (taps the chosen artifacts)
        //   2. fold improvise reduction into totalCost
        //   3. agent.ChooseManaSourcesAsync(reducedCost)
        // So by the time the mana-source prompt fires, the artifacts are
        // already tapped — meaning an artifact that ALSO has a mana ability
        // cannot have been tapped for mana on this cast AND counted for
        // improvise. We assert the order by observing that the mana prompt
        // receives the post-improvise cost.
        var kappa = SeedKappaInHand(_alice);
        var artifact = SeedArtifactOnBattlefield(_alice, "Rock");

        var improvise = KappaCannoneerFactory.BuildAdditionalCost(
            kappa, new Permanent[] { artifact });
        var agent = new CapturingManaAgent();

        await _flow.CastAsync(
            _alice, kappa, NoOpDef(), agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        // Tap happened BEFORE the mana prompt — and the prompt saw {4}{U}.
        agent.LastPromptedCost!.Generic.Should().Be(4);
        agent.LastPromptedCost!.Blue.Should().Be(1);
        artifact.IsTapped.Should().BeTrue(
            "artifact tapped during the additional-cost step, before mana-source prompt (CR 605.1)");
    }

    // ── bot probe ───────────────────────────────────────────────────────────

    [Fact]
    public void ImproviseAltCostProbe_KappaWithUntappedArtifacts_YieldsCandidate()
    {
        var kappa = SeedKappaInHand(_alice);
        SeedArtifactOnBattlefield(_alice, "Sol Ring");
        SeedArtifactOnBattlefield(_alice, "Mox Opal");

        var probe = new ImproviseAltCostProbe();
        var candidates = probe.CandidatesFor(kappa, _alice, Ctx()).ToList();

        candidates.Should().HaveCount(1);
        var c = candidates[0].Should().BeOfType<ImproviseAlternativeCost>().Subject;
        c.AdditionalCost.ReductionAmount.Should().Be(2,
            "default chooser picks min(generic, available) = min(5, 2)");
        c.AlternativeManaCost.Generic.Should().Be(3,
            "{5}{U} - {2} improvise = {3}{U}");
        c.AlternativeManaCost.Blue.Should().Be(1);
    }

    [Fact]
    public void ImproviseAltCostProbe_NoArtifacts_YieldsNothing()
    {
        var kappa = SeedKappaInHand(_alice);

        var probe = new ImproviseAltCostProbe();
        probe.CandidatesFor(kappa, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void ImproviseAltCostProbe_NonImproviseCard_YieldsNothing()
    {
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);
        bolt.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(bolt);
        SeedArtifactOnBattlefield(_alice, "Sol Ring");

        var probe = new ImproviseAltCostProbe();
        probe.CandidatesFor(bolt, _alice, Ctx()).Should().BeEmpty();
    }

    [Fact]
    public void ImproviseAltCostProbe_RegisteredInDefaultRegistry()
    {
        var reg = AlternativeCostProbeRegistry.CreateDefault();

        reg.Probes.OfType<ImproviseAltCostProbe>().Should().HaveCount(1,
            "the default registry must include the Improvise probe so the bot's " +
            "alt-cost stream surfaces Kappa Cannoneer-style cards");
    }

    [Fact]
    public void DefaultIsImproviseCard_DetectsKappaCannoneerKeywordMarker()
    {
        var kappa = KappaCannoneerFactory.Create(_alice);
        ImproviseAltCostProbe.DefaultIsImproviseCard(kappa).Should().BeTrue();

        var bolt = new Instant("Lightning Bolt", "{R}");
        ImproviseAltCostProbe.DefaultIsImproviseCard(bolt).Should().BeFalse();
    }

    // ── support: capture cost passed to the mana prompt ─────────────────────

    /// <summary>
    /// Minimal IPlayerAgent impl that captures the cost handed to the
    /// mana-source prompt — used to assert post-improvise effective cost
    /// without depending on ScriptedAgent (which is sealed).
    /// </summary>
    private sealed class CapturingManaAgent : IPlayerAgent
    {
        public ManaCost? LastPromptedCost { get; private set; }

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            LastPromptedCost = cost;
            return Task.FromResult(ManaPayment.Empty);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default)
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
