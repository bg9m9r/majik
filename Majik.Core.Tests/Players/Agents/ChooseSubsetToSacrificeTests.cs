using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Coverage for the first-class "sacrifice any number of permanents" agent
/// hook — <see cref="IPlayerAgent.ChooseSubsetToSacrificeAsync"/> — and its
/// use in <see cref="ScapeshiftFactory.BuildAgentResolveEffect"/> (pays down
/// the <c>pick-a-subset-to-sacrifice-agent-hook</c> v1 deferral).
///
/// Verifies:
///   1. The default interface implementation's pre-agent posture: an
///      optional ("any number", min 0) subset declines to sacrifice
///      anything; a mandatory floor (min &gt; 0) sacrifices the first N.
///   2. A subset-choosing agent's pick is routed, deduped, and clamped.
///   3. Scapeshift's agent-driven resolve path sacrifices exactly the
///      agent-chosen subset, then tutors that many lands from the library
///      (CR 701.16 + CR 701.19a).
///   4. No agent registered → Scapeshift's agent path is a clean no-op
///      (the faithful "any number" lower bound).
/// </summary>
public class ChooseSubsetToSacrificeTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land MakeLand(string name, Player owner, CardSubtype subtype)
    {
        var land = new Land(name, new[] { CardSupertype.Basic }, new[] { subtype });
        land.SetOwner(owner);
        land.SetController(owner);
        return land;
    }

    // ── Bare agent: only the mandatory abstract members, everything else
    //    falls through to the default interface implementations. ──────────
    private class BareAgent : IPlayerAgent
    {
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
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => Task.FromResult(0);
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ITriggeredAbility>>(mine.ToList());
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => Task.FromResult(ManaPayment.Empty);
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default)
            => Task.FromResult(CombatPlan.None);
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default)
            => Task.FromResult(BlockPlan.None);
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.ScryAction.ScryDecision(ToBottom: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => Task.FromResult(new Majik.Core.Keywords.SurveilAction.SurveilDecision(ToGraveyard: peeked.ToList(), TopOrder: Array.Empty<ICard>()));
    }

    // ── Agent that picks a specified subset via the high-level hook. ──────
    // Re-implements the interface (BareAgent, IPlayerAgent) so its public
    // ChooseSubsetToSacrificeAsync replaces the default interface method in
    // the interface dispatch table (you cannot `override` a default interface
    // method — you re-implement the interface on the derived type).
    private sealed class SubsetAgent : BareAgent, IPlayerAgent
    {
        private readonly Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>> _pick;
        public SubsetAgent(Func<IReadOnlyList<ICard>, IReadOnlyList<ICard>> pick) => _pick = pick;

        public Task<IReadOnlyList<ICard>> ChooseSubsetToSacrificeAsync(
            GameContext? ctx, IReadOnlyList<ICard> candidates, int minCount, int maxCount,
            BotIntent intent = BotIntent.None, CancellationToken ct = default)
            => Task.FromResult(_pick(candidates));
    }

    // -----------------------------------------------------------------------
    // 1. Default-implementation posture.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Default_AnyNumber_OptionalSubset_SacrificesNothing()
    {
        var f1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f2 = MakeLand("Forest", _alice, CardSubtype.Forest);
        IPlayerAgent agent = new BareAgent();

        var chosen = await agent.ChooseSubsetToSacrificeAsync(
            ctx: null, candidates: new ICard[] { f1, f2 }, minCount: 0, maxCount: 2);

        // "Any number" with no smart agent → sacrifice nothing.
        chosen.Should().BeEmpty();
    }

    [Fact]
    public async Task Default_MandatoryFloor_SacrificesFirstN()
    {
        var f1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f2 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f3 = MakeLand("Forest", _alice, CardSubtype.Forest);
        IPlayerAgent agent = new BareAgent();

        var chosen = await agent.ChooseSubsetToSacrificeAsync(
            ctx: null, candidates: new ICard[] { f1, f2, f3 }, minCount: 2, maxCount: 2);

        chosen.Should().HaveCount(2);
        chosen.Should().BeEquivalentTo(new ICard[] { f1, f2 });
    }

    // Agent whose declarative ChooseAsync sink returns a deliberately
    // malformed pick (duplicate + out-of-pool + over-max), so the DEFAULT
    // ChooseSubsetToSacrificeAsync sanitisation is exercised end to end.
    private sealed class JunkChooseAgent : BareAgent, IPlayerAgent
    {
        private readonly IReadOnlyList<object> _junk;
        public JunkChooseAgent(IReadOnlyList<object> junk) => _junk = junk;
        public Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
            => Task.FromResult(_junk);
    }

    [Fact]
    public async Task Default_SanitisesAgentPick_DedupedAndClampedToMax()
    {
        var f1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f2 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var stray = MakeLand("Mountain", _alice, CardSubtype.Mountain); // not in pool
        var pool = new ICard[] { f1, f2 };

        // Declarative sink hands back a duplicate, an out-of-pool card, then f2.
        IPlayerAgent agent = new JunkChooseAgent(new object[] { f1, f1, stray, f2 });
        var chosen = await agent.ChooseSubsetToSacrificeAsync(
            ctx: null, candidates: pool, minCount: 0, maxCount: 1);

        // Default sanitisation: deduped, stray filtered, clamped to max=1.
        chosen.Should().ContainSingle().Which.Should().BeSameAs(f1);
    }

    // -----------------------------------------------------------------------
    // 2. Scapeshift agent-driven resolve path.
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Scapeshift_AgentPath_SacrificesChosenSubset_AndTutorsThatMany()
    {
        // Three Forests on the battlefield; the agent elects to sac two.
        var f1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f2 = MakeLand("Forest", _alice, CardSubtype.Forest);
        var f3 = MakeLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(f1);
        _alice.Zones.Battlefield.AddCard(f2);
        _alice.Zones.Battlefield.AddCard(f3);

        var m1 = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        var m2 = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(m1);
        _alice.Zones.Library.AddCard(m2);

        // Agent sacrifices f1 + f2 (leaving f3), and tutors the two Mountains
        // via the default library-pick (first-candidate) loop.
        var agent = new SubsetAgent(c => c.Take(2).ToList());
        var ctx = ResolutionContext.For(
            _alice, agent, game: null, chosenTargets: null);

        foreach (var fx in ScapeshiftFactory.BuildAgentResolveEffect(_alice))
            await fx.ExecuteAsync(ctx);

        // Exactly the two chosen Forests went to the graveyard; f3 survives.
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(new ICard[] { f1, f2 });
        _alice.Zones.Battlefield.GetCards().Should().Contain(f3);

        // N = 2 lands fetched onto the battlefield.
        _alice.Zones.Battlefield.GetCards().Should().Contain(new ICard[] { m1, m2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public async Task Scapeshift_AgentPath_NoAgent_IsCleanNoOp()
    {
        var f1 = MakeLand("Forest", _alice, CardSubtype.Forest);
        _alice.Zones.Battlefield.AddCard(f1);
        var m1 = MakeLand("Mountain", _alice, CardSubtype.Mountain);
        _alice.Zones.Library.AddCard(m1);

        // No agent on the context, none in the registry → sacrifice nothing.
        var ctx = ResolutionContext.For(
            _alice, agent: null, game: null, chosenTargets: null);

        foreach (var fx in ScapeshiftFactory.BuildAgentResolveEffect(_alice))
            await fx.ExecuteAsync(ctx);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle().Which.Should().BeSameAs(f1);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(m1);
    }
}
