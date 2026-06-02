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
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Metallic Rebuke (Aether Revolt, {2}{U}).
///
/// Oracle:
///   "Improvise (Your artifacts can help cast this spell. Each artifact you
///    tap after you're done activating mana abilities pays for {1}.)
///    Counter target spell unless its controller pays {3}."
///
/// Coverage:
///   * Identity: {2}{U} Blue Instant, mana value 3.
///   * <see cref="NamedCardFactory"/> dispatch.
///   * Improvise keyword marker present.
///   * <see cref="SpellDefinition"/> shape: 1 "target spell" request.
///   * Improvise reduces effective generic cost — 2 artifacts tapped → {U}+0 generic.
///   * Resolve: controller can't pay {3} → target spell countered to graveyard.
///   * Resolve: controller pays {3} → target spell NOT countered (survives).
/// </summary>
[Trait("Color", "U")]
public class MetallicRebukeFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public MetallicRebukeFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private Artifact SeedArtifact(Player p, string name)
    {
        var a = new Artifact(name, "{0}");
        a.SetOwner(p);
        a.SetController(p);
        a.SetZone(ZoneType.Battlefield);
        p.Zones.Battlefield.AddCard(a);
        return a;
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue_AtTwoU()
    {
        var rebuke = MetallicRebukeFactory.Create(_alice);

        rebuke.Name.Should().Be("Metallic Rebuke");
        rebuke.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(rebuke).Should().Contain(ManaColor.Blue);
        rebuke.ManaCost.Should().Be("{2}{U}");
        rebuke.ManaCostValue.TotalValue.Should().Be(3,
            "Metallic Rebuke has mana value 3 ({2}{U})");
        rebuke.Owner.Should().BeSameAs(_alice);
        rebuke.Controller.Should().BeSameAs(_alice);
    }
    // ── Improvise keyword marker ──────────────────────────────────────────────

    [Fact]
    public void Create_HasImproviseKeywordMarker()
    {
        var rebuke = MetallicRebukeFactory.Create(_alice);
        var keywords = rebuke.Abilities.OfType<KeywordAbility>().ToList();

        keywords.Should().Contain(k => k.Keyword == "Improvise",
            "CR 702.127 — Improvise marker surfaces card to bot's ImproviseAltCostProbe");
    }

    // ── SpellDefinition shape ─────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetSpellRequest()
    {
        var def = MetallicRebukeFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target spell");
    }

    // ── Improvise cost reduction ──────────────────────────────────────────────

    [Fact]
    public async Task Improvise_TwoArtifactsTapped_EffectiveCostIsBlueOnly()
    {
        // Alice has Metallic Rebuke in hand + 2 untapped artifacts.
        // With Improvise tapping both: printed {2}{U} → effective {U} + 0 generic.
        var rebuke = MetallicRebukeFactory.Create(_alice);
        rebuke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rebuke);

        var a1 = SeedArtifact(_alice, "Sol Ring");
        var a2 = SeedArtifact(_alice, "Mox Opal");

        // Rebuke targets a spell — put one on the stack to satisfy targeting.
        var targetCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var targetSpell = new Majik.Core.Spells.Spell(targetCard, _bob);
        _stack.Push(targetSpell);

        var improvise = MetallicRebukeFactory.BuildAdditionalCost(
            rebuke, new Permanent[] { a1, a2 });

        var agent = new CapturingManaAgent(targetSpell);

        await _flow.CastAsync(
            _alice, rebuke,
            MetallicRebukeFactory.BuildSpellDefinition(o => o, _stack),
            agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        // Printed {2}{U} − {2} improvise = {0}{U}.
        agent.LastPromptedCost.Should().NotBeNull();
        agent.LastPromptedCost!.Generic.Should().Be(0,
            "both generic pips paid by tapping 2 artifacts via Improvise");
        agent.LastPromptedCost!.Blue.Should().Be(1,
            "coloured pip is preserved — Improvise only reduces generic (CR 702.127)");

        a1.IsTapped.Should().BeTrue();
        a2.IsTapped.Should().BeTrue();
        rebuke.Zone.Should().Be(ZoneType.Stack);
    }

    [Fact]
    public async Task Improvise_OneArtifactTapped_EffectiveCostIsOneGenericPlusBlue()
    {
        var rebuke = MetallicRebukeFactory.Create(_alice);
        rebuke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rebuke);

        var a1 = SeedArtifact(_alice, "Sol Ring");

        var targetCard = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var targetSpell = new Majik.Core.Spells.Spell(targetCard, _bob);
        _stack.Push(targetSpell);

        var improvise = MetallicRebukeFactory.BuildAdditionalCost(
            rebuke, new Permanent[] { a1 });
        var agent = new CapturingManaAgent(targetSpell);

        await _flow.CastAsync(
            _alice, rebuke,
            MetallicRebukeFactory.BuildSpellDefinition(o => o, _stack),
            agent, Ctx(),
            additionalCosts: new IAdditionalCost[] { improvise });

        agent.LastPromptedCost!.Generic.Should().Be(1,
            "{2}{U} − {1} improvise = {1}{U}");
        agent.LastPromptedCost!.Blue.Should().Be(1);
        a1.IsTapped.Should().BeTrue();
    }

    // ── Resolve: counter unless pay {3} ──────────────────────────────────────

    [Fact]
    public async Task Resolve_ControllerCantPayThree_TargetSpellCountered()
    {
        // Alice casts Metallic Rebuke targeting Bob's Lightning Bolt.
        // Bob has no mana — can't pay {3} — so the Bolt is countered.
        var rebuke = MetallicRebukeFactory.Create(_alice);
        rebuke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rebuke);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has no mana in pool — cannot pay {3}.
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(
            _alice, rebuke,
            MetallicRebukeFactory.BuildSpellDefinition(o => o, _stack),
            agent, Ctx(),
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            "Bob couldn't pay {3}; Metallic Rebuke counters the spell (CR 701.5)");
    }

    [Fact]
    public async Task Resolve_ControllerPaysThree_TargetSpellSurvives()
    {
        // Bob has {3} generic available. Alice casts Metallic Rebuke.
        // Bob pays {3} — the counter no-ops, Bolt survives on the stack /
        // resolves; we assert it was NOT sent to the graveyard by Rebuke.
        var rebuke = MetallicRebukeFactory.Create(_alice);
        rebuke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rebuke);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        // Bob has {3} in his mana pool — enough to pay the unless rider.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(3));

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        await _flow.CastAsync(
            _alice, rebuke,
            MetallicRebukeFactory.BuildSpellDefinition(o => o, _stack),
            agent, Ctx(),
            alternativeCost: null);

        _resolver.ResolveTop(_stack);

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "Bob paid {3}; Metallic Rebuke's counter no-ops, Lightning Bolt survives (CR 118.4)");
    }

    // ── Bot probe surfaces Metallic Rebuke via ImproviseAltCostProbe ─────────

    [Fact]
    public void ImproviseAltCostProbe_MetallicRebukeWithArtifacts_YieldsCandidate()
    {
        var rebuke = MetallicRebukeFactory.Create(_alice);
        rebuke.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(rebuke);

        SeedArtifact(_alice, "Sol Ring");
        SeedArtifact(_alice, "Mox Opal");

        var probe = new ImproviseAltCostProbe();
        var candidates = probe.CandidatesFor(rebuke, _alice, Ctx()).ToList();

        candidates.Should().HaveCount(1);
        var c = candidates[0].Should().BeOfType<ImproviseAlternativeCost>().Subject;
        // Printed {2}{U}: min(generic=2, available=2) = 2 tapped → {0}{U}
        c.AdditionalCost.ReductionAmount.Should().Be(2,
            "default chooser picks min(generic, available) = min(2, 2) = 2");
        c.AlternativeManaCost.Generic.Should().Be(0,
            "{2}{U} − {2} improvise = {U}");
        c.AlternativeManaCost.Blue.Should().Be(1);
    }

    // ── support: capture the mana cost handed to the mana-source prompt ───────

    private sealed class CapturingManaAgent : IPlayerAgent
    {
        private readonly object? _target;

        /// <param name="target">Optional pre-selected target returned for
        /// the "target spell" request — allows Improvise cast tests to
        /// satisfy the TargetRequest without using ScriptedAgent.</param>
        public CapturingManaAgent(object? target = null) { _target = target; }

        public ManaCost? LastPromptedCost { get; private set; }

        public Task<ManaPayment> ChooseManaSourcesAsync(
            GameContext ctx, ManaCost cost, CancellationToken ct = default)
        {
            LastPromptedCost = cost;
            return Task.FromResult(ManaPayment.Empty);
        }

        public Task<PriorityAction> ChoosePriorityActionAsync(
            GameContext ctx, CancellationToken ct = default)
            => Task.FromResult(PriorityAction.Pass);

        public Task<MulliganDecision> ChooseMulliganAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken,
            CancellationToken ct = default)
            => Task.FromResult(MulliganDecision.Keep);

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ICard>>(Array.Empty<ICard>());

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            GameContext ctx, TargetRequest request,
            CancellationToken ct = default)
        {
            if (_target != null)
                return Task.FromResult<IReadOnlyList<object>>(new[] { _target });
            return Task.FromResult<IReadOnlyList<object>>(Array.Empty<object>());
        }

        public Task<int> ChooseXAsync(
            GameContext ctx, ICard source, CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<int> ChooseModeAsync(
            GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<Majik.Core.Cards.BotIntent>? modeIntents = null,
            CancellationToken ct = default)
            => Task.FromResult(0);

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            GameContext ctx, IReadOnlyList<ITriggeredAbility> mine,
            CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine);

        public Task<CombatPlan> DeclareAttackersAsync(
            GameContext ctx, IReadOnlyList<Creature> eligibleAttackers,
            CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);

        public Task<BlockPlan> DeclareBlockersAsync(
            GameContext ctx, IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> eligibleBlockers,
            CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);

        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(
                ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));

        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            GameContext? ctx, IReadOnlyList<ICard> peeked,
            CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(
                ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }
}
