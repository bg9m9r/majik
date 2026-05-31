using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Moq;
using Xunit;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 408 — wish-tutor primitive. Tests for
/// <see cref="WishTutorEffect"/>:
///
/// <list type="bullet">
///   <item>Finds an eligible card in the wishboard → moves it to hand.</item>
///   <item>No eligible card (empty wishboard or all filtered out) → no-op.</item>
///   <item>Agent decline (returns null) → no-op even with eligible cards.</item>
///   <item>Agent pick illegal-on-resolution → falls back to first
///         candidate (defensive).</item>
///   <item>Predicate filters correctly (artifact-only sees only artifacts).</item>
///   <item>Wishboard is the same pile as Sideboard.</item>
/// </list>
///
/// AgentRegistry is process-global → tests Clear() on dispose so they
/// don't leak agents into neighbouring suites.
/// </summary>
public class WishTutorEffectTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);

    public WishTutorEffectTests()
    {
        AgentRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
    }

    [Fact]
    public void Wishboard_IsAliasFor_Sideboard()
    {
        // Same underlying zone reference — wishboard is a semantic
        // accessor over the Sideboard pile (CR 408 / CR 100.4).
        ReferenceEquals(_alice.Wishboard, _alice.Sideboard).Should().BeTrue(
            "Player.Wishboard is the wishboard surface over the same sideboard pile");
    }

    [Fact]
    public void Resolve_FindsEligibleCard_MovesItToHand()
    {
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        _alice.Wishboard.AddCard(wurmcoil);

        var tutor = new WishTutorEffect(
            WishTutorEffect.Predicates.ArtifactCard,
            "an artifact card from outside the game");

        var picked = tutor.Resolve(_alice);

        picked.Should().BeSameAs(wurmcoil);
        _alice.Wishboard.GetCards().Should().NotContain(wurmcoil);
        _alice.Zones.Hand.GetCards().Should().Contain(wurmcoil);
        wurmcoil.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Resolve_NoEligibleCard_EmptyWishboard_NoOp()
    {
        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);

        var picked = tutor.Resolve(_alice);

        picked.Should().BeNull();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_NoEligibleCard_PredicateFiltersAllOut_NoOp()
    {
        // Bolt sits in wishboard, but the predicate wants artifact only —
        // no candidate matches.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);

        var picked = tutor.Resolve(_alice);

        picked.Should().BeNull();
        _alice.Wishboard.GetCards().Should().Contain(bolt);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AgentDeclines_NoOp()
    {
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        _alice.Wishboard.AddCard(wurmcoil);

        var agent = new ScriptedAgent();
        agent.QueueFromPile((ICard?)null); // decline
        AgentRegistry.Set(_alice, agent);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);

        var picked = tutor.Resolve(_alice);

        picked.Should().BeNull();
        _alice.Wishboard.GetCards().Should().Contain(wurmcoil);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_AgentPicksFromCandidates_HonoursPick()
    {
        // Two artifacts available; agent picks the second one.
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        _alice.Wishboard.AddCard(solRing);
        _alice.Wishboard.AddCard(wurmcoil);

        var agent = new ScriptedAgent();
        agent.QueueFromPile(candidates => candidates.Last());
        AgentRegistry.Set(_alice, agent);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);

        var picked = tutor.Resolve(_alice);

        picked.Should().BeSameAs(wurmcoil);
        _alice.Zones.Hand.GetCards().Should().Contain(wurmcoil);
        _alice.Wishboard.GetCards().Should().Contain(solRing);
    }

    [Fact]
    public void Resolve_PredicateGatesByType_ArtifactsOnly()
    {
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(solRing);

        // Test ChooseFromPileAsync candidates argument by capturing what
        // the agent sees — should contain only the artifact.
        IReadOnlyList<ICard>? observedCandidates = null;
        var agent = new ScriptedAgent();
        agent.QueueFromPile(candidates =>
        {
            observedCandidates = candidates;
            return candidates[0];
        });
        AgentRegistry.Set(_alice, agent);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);
        tutor.Resolve(_alice);

        observedCandidates.Should().NotBeNull();
        observedCandidates!.Should().HaveCount(1, "the predicate filters out non-artifacts");
        observedCandidates![0].Should().BeSameAs(solRing);
    }

    [Fact]
    public void Resolve_AnyCardPredicate_AcceptsEverything()
    {
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(bears);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.AnyCard);

        var picked = tutor.Resolve(_alice);

        picked.Should().NotBeNull();
        // Deterministic first-pick fallback when no agent — bolt is first.
        picked.Should().BeSameAs(bolt);
    }

    [Fact]
    public void Resolve_AgentReturnsIllegalPick_FallsBackToFirstCandidate()
    {
        // Two artifacts in the wishboard pool; agent returns a card that
        // isn't in the candidate list (e.g. a stale reference from outside
        // the eligible filter). Defensive guard demotes to the first
        // candidate — same posture Liliana / Annihilator use.
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        var ghostCard = new Artifact("Mox Opal", "{0}") { Owner = _alice };
        _alice.Wishboard.AddCard(solRing);
        _alice.Wishboard.AddCard(wurmcoil);

        var agent = new ScriptedAgent();
        agent.QueueFromPile(_ => ghostCard); // not in candidates
        AgentRegistry.Set(_alice, agent);

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);
        var picked = tutor.Resolve(_alice);

        picked.Should().BeSameAs(solRing, "illegal pick demotes to first candidate");
        _alice.Zones.Hand.GetCards().Should().Contain(solRing);
    }

    [Fact]
    public void AsEffect_WrapsResolveAsIEffect()
    {
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Wishboard.AddCard(solRing);

        var effect = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard)
            .AsEffect(_alice);

        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(solRing);
        _alice.Wishboard.GetCards().Should().NotContain(solRing);
    }

    // ----- PLAN 01 (Slice D) — async prompt path -----

    [Fact]
    public async Task ResolveAsync_GenuinelyPromptsSuppliedAgent_NotAutoPick()
    {
        // Two artifacts; the agent (passed explicitly, NOT via AgentRegistry)
        // is consulted and its pick — the SECOND candidate — is honoured.
        // Proves the migrated path prompts rather than auto-picking [0].
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        _alice.Wishboard.AddCard(solRing);
        _alice.Wishboard.AddCard(wurmcoil);

        IReadOnlyList<ICard>? observed = null;
        var agent = new Mock<IPlayerAgent>();
        agent.Setup(a => a.ChooseFromPileAsync(
                _alice, It.IsAny<IReadOnlyList<ICard>>(), It.IsAny<string>(),
                It.IsAny<BotIntent>(), It.IsAny<CancellationToken>()))
            .Returns((Player _, IReadOnlyList<ICard> cands, string _, BotIntent _, CancellationToken _) =>
            {
                observed = cands;
                return Task.FromResult<ICard?>(cands[1]);
            });

        var tutor = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard);
        var picked = await tutor.ResolveAsync(_alice, agent.Object, game: null);

        picked.Should().BeSameAs(wurmcoil, "the agent's pick must be honoured, not candidates[0]");
        observed.Should().HaveCount(2);
        _alice.Zones.Hand.GetCards().Should().Contain(wurmcoil);
        agent.Verify(a => a.ChooseFromPileAsync(
            _alice, It.IsAny<IReadOnlyList<ICard>>(), It.IsAny<string>(),
            BotIntent.Tutor, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AsEffect_ExecuteAsync_PromptsAgentOffResolutionContext()
    {
        // The IEffect built by AsEffect reads ctx.Agent off the
        // ResolutionContext and prompts it — honouring a scripted pick.
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        _alice.Wishboard.AddCard(solRing);
        _alice.Wishboard.AddCard(wurmcoil);

        var agent = new ScriptedAgent();
        agent.QueueFromPile(cands => cands.Last()); // pick the second

        var effect = new WishTutorEffect(WishTutorEffect.Predicates.ArtifactCard)
            .AsEffect(_alice);

        var rc = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);
        await effect.ExecuteAsync(rc);

        _alice.Zones.Hand.GetCards().Should().Contain(wurmcoil);
        _alice.Wishboard.GetCards().Should().Contain(solRing);
    }
}
