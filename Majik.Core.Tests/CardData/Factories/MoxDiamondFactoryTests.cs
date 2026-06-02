using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MoxDiamondFactory"/> (Stronghold).
///
/// Covers:
/// - Identity ({0} Artifact).
/// - Five "any color" mana abilities (one per WUBRG).
/// - ETB printed replacement (CR 614) — present when wired through
///   <see cref="ReplacementBus"/>; absent on shape-only path.
/// - Yes path: discards chosen land, Mox enters battlefield.
/// - No path: Mox redirected to graveyard (never enters).
/// - No-land-in-hand: skips prompt, Mox redirected to graveyard.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
[Trait("Color", "C")]
public class MoxDiamondFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MoxDiamond_Identity_ArtifactAtZero()
    {
        var mox = MoxDiamondFactory.Create(_alice);

        mox.Name.Should().Be("Mox Diamond");
        mox.HasType(CardType.Artifact).Should().BeTrue();
        mox.ManaCost.ToString().Should().Be("{0}");
        mox.Owner.Should().BeSameAs(_alice);
        mox.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void MoxDiamond_HasFiveAnyColorManaAbilities()
    {
        var mox = MoxDiamondFactory.Create(_alice);

        var manaAbilities = mox.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5,
            "one ManaAbility per WUBRG colour (modal pick at activation)");

        // Each one produces 1 mana.
        foreach (var ma in manaAbilities)
        {
            ma.ManaGenerated.TotalValue.Should().Be(1);
        }
    }

    // -----------------------------------------------------------------------
    // ETB replacement — CR 614
    // -----------------------------------------------------------------------

    /// <summary>
    /// CR 614 — when wired via a <see cref="ReplacementBus"/> and the
    /// controller says "yes" to discarding a land, Mox Diamond enters
    /// the battlefield and the chosen land moves to the graveyard.
    /// </summary>
    [Fact]
    public async Task MoxDiamond_EtbYes_DiscardsLand_EntersBattlefield()
    {
        var (zones, bus) = BuildEngine();

        // Alice's hand: a Forest (the land to discard) + Mox Diamond.
        var forest = new Land("Forest", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        // Scripted agent: yes to discard, picks the Forest.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        agent.QueueFromHand(forest);

        var mox = MoxDiamondFactory.Create(_alice, bus, _ => agent);
        // Mox starts in the library; cast it (simulate by putting on Stack).
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        // CR 614.6 prompting now happens on the async resolution path.
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);
        await zones.MoveCardAsync(mox, ZoneType.Stack, ZoneType.Battlefield, ctx, _alice);

        mox.Zone.Should().Be(ZoneType.Battlefield,
            "yes to discard → Mox Diamond enters normally");
        _alice.Zones.Battlefield.GetCards().Should().Contain(mox);

        forest.Zone.Should().Be(ZoneType.Graveyard,
            "the chosen land card moved hand → graveyard as the alt cost");
        _alice.Zones.Hand.GetCards().Should().NotContain(forest);
        _alice.Zones.Graveyard.GetCards().Should().Contain(forest);
    }

    /// <summary>
    /// CR 614 — when the controller says "no", Mox Diamond is sacrificed
    /// (never actually enters). The would-enter intent is rewritten so
    /// Mox lands in the graveyard.
    /// </summary>
    [Fact]
    public async Task MoxDiamond_EtbNo_SacrificedToGraveyard_NeverEnters()
    {
        var (zones, bus) = BuildEngine();

        // Land in hand exists (so the prompt fires), but agent says no.
        var forest = new Land("Forest", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        var mox = MoxDiamondFactory.Create(_alice, bus, _ => agent);
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);
        await zones.MoveCardAsync(mox, ZoneType.Stack, ZoneType.Battlefield, ctx, _alice);

        mox.Zone.Should().Be(ZoneType.Graveyard,
            "no to discard → Mox Diamond is sacrificed (redirected to graveyard)");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(mox);
        _alice.Zones.Graveyard.GetCards().Should().Contain(mox);

        forest.Zone.Should().Be(ZoneType.Hand,
            "land stayed in hand — declined to discard");
    }

    /// <summary>
    /// The synchronous zone-move path (ReplacementBus.Apply) does NOT prompt —
    /// CR 614.6 choices must be awaited, never bridged sync-over-async. On the
    /// sync path Mox Diamond takes the conservative "sacrifice" branch even
    /// when a land + willing agent are available.
    /// </summary>
    [Fact]
    public void MoxDiamond_SyncMoveCard_NoPrompt_Sacrifices()
    {
        var (zones, bus) = BuildEngine();

        var forest = new Land("Forest", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        // Agent would say yes — but the sync path never consults it.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var mox = MoxDiamondFactory.Create(_alice, bus, _ => agent);
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        zones.MoveCard(mox, ZoneType.Stack, ZoneType.Battlefield, _alice);

        mox.Zone.Should().Be(ZoneType.Graveyard, "sync path → no prompt → sacrifice");
        forest.Zone.Should().Be(ZoneType.Hand, "land untouched on the sync path");
    }

    /// <summary>
    /// No land in hand: the "discard a land" branch is illegal (the
    /// alternative cost cannot be paid), so the "sacrifice" tail runs
    /// directly without prompting the agent.
    /// </summary>
    [Fact]
    public void MoxDiamond_EtbNoLandInHand_SacrificedToGraveyard()
    {
        var (zones, bus) = BuildEngine();

        // Hand is empty of lands. Agent would say yes but no chance to use it.
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        var mox = MoxDiamondFactory.Create(_alice, bus, _ => agent);
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        zones.MoveCard(mox, ZoneType.Stack, ZoneType.Battlefield, _alice);

        mox.Zone.Should().Be(ZoneType.Graveyard,
            "no land in hand → no discard available → sacrifice tail runs");
        _alice.Zones.Graveyard.GetCards().Should().Contain(mox);
    }

    // -----------------------------------------------------------------------
    // Shape-only path — no replacement bus wired
    // -----------------------------------------------------------------------

    [Fact]
    public void MoxDiamond_ShapeOnly_NoReplacementBus_EntersBattlefieldUnobstructed()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var mox = MoxDiamondFactory.Create(_alice);
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        zones.MoveCard(mox, ZoneType.Stack, ZoneType.Battlefield, _alice);

        mox.Zone.Should().Be(ZoneType.Battlefield,
            "shape-only path skips the ETB replacement");
    }

    // -----------------------------------------------------------------------
    // PLAN 08 — async replacement path (ZoneService.MoveCardAsync). The
    // discard-a-land choice prompts for real off the ResolutionContext;
    // no sync-over-async bridge.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MoxDiamond_Async_DiscardChoice_GenuinelyAwaitsHuman_NoSyncBridge()
    {
        var (zones, bus) = BuildEngine();

        var forest = new Land("Forest", supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        // Human agent parks on the yes/no prompt until Answer() is called.
        var human = new DeferredDiscardAgent(forest);
        var mox = MoxDiamondFactory.Create(_alice, bus, _ => human);
        _alice.Zones.Stack.AddCard(mox);
        mox.SetZone(ZoneType.Stack);

        var ctx = ResolutionContext.For(_alice, human, game: null, chosenTargets: null);
        var moveTask = zones.MoveCardAsync(mox, ZoneType.Stack, ZoneType.Battlefield, ctx, _alice);

        human.WasPrompted.Should().BeTrue("the replacement awaited the discard prompt");
        moveTask.IsCompleted.Should().BeFalse(
            "the human has not answered yet — no sync-over-async bridge");
        mox.Zone.Should().Be(ZoneType.Stack, "Mox has not moved while the human thinks");

        human.Answer(true); // human chooses to discard a land
        await moveTask;

        mox.Zone.Should().Be(ZoneType.Battlefield, "human paid → Mox enters");
        forest.Zone.Should().Be(ZoneType.Graveyard, "human discarded the land");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, ReplacementBus replacements) BuildEngine()
    {
        var eventBus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        return (zones, rep);
    }

    /// <summary>
    /// Human-think-time agent: the discard yes/no parks on a TCS; the land
    /// pick returns the supplied forest. Proves the Mox Diamond replacement
    /// genuinely awaits the agent.
    /// </summary>
    private sealed class DeferredDiscardAgent : IPlayerAgent
    {
        private readonly TaskCompletionSource<bool> _yesNo =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly ICard _landPick;

        public DeferredDiscardAgent(ICard landPick) => _landPick = landPick;
        public bool WasPrompted { get; private set; }
        public void Answer(bool yes) => _yesNo.SetResult(yes);

        public Task<bool> ChooseYesNoAsync(string question, BotIntent intent, CancellationToken ct = default)
        {
            WasPrompted = true;
            return _yesNo.Task;
        }

        public Task<ICard?> ChooseFromHandAsync(Player chooser, IReadOnlyList<ICard> candidates, BotIntent intent, CancellationToken ct = default)
            => Task.FromResult<ICard?>(_landPick);

        // Unused surface.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
