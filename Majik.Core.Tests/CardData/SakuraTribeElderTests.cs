using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="SakuraTribeElderFactory"/> — Creature — Snake Shaman
/// {1}{G} 1/1 (Champions of Kamigawa). Oracle:
///   "Sacrifice this creature: Search your library for a basic land card,
///    put that card onto the battlefield tapped, then shuffle."
///
/// Covers:
///   - Card identity (Creature, {1}{G}, 1/1, Snake + Shaman, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Ability shape: single <see cref="ActivatedAbility"/> with a sacrifice
///     additional cost and no target requests.
///   - Resolve: tutors a basic land to the battlefield tapped, sacrifices
///     self, leaves nonbasic lands in library.
///   - Resolve: no basics in library → still sacrifices, no land moved.
/// </summary>
public class SakuraTribeElderTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SakuraTribeElder_IsSnakeShaman_At1G_OneOne()
    {
        var c = SakuraTribeElderFactory.Create(_alice);

        c.Name.Should().Be("Sakura-Tribe Elder");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.HasSubtype(CardSubtype.Shaman).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SakuraTribeElder()
    {
        var card = NamedCardFactory.Create("Sakura-Tribe Elder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Sakura-Tribe Elder");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{G}");
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Elder_HasSingleActivatedAbility_NoManaAbilities()
    {
        var c = SakuraTribeElderFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void SacAbility_HasSacrificeCost_NoTargets_NoMana_NoTap()
    {
        var c = SakuraTribeElderFactory.Create(_alice);
        var sac = c.Abilities.OfType<ActivatedAbility>().Single();

        sac.TargetRequests.Should().BeEmpty();
        sac.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Sakura-Tribe Elder's sacrifice ability is pure sac — no mana component");
        sac.Costs.OfType<AdditionalCost>().Should().ContainSingle(
            c => c.CostType == AdditionalCostType.Sacrifice,
            "the only cost is sacrificing the elder");
        sac.Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Tap,
                "the printed cost has no {T} pip");
    }

    // -----------------------------------------------------------------------
    // Resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_Tutor_MovesBasicLandToBattlefieldTapped_AndSacrificesSelf()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest,
            "the tutored basic enters the battlefield");
        forest.Zone.Should().Be(ZoneType.Battlefield);
        forest.IsTapped.Should().BeTrue("the printed rider taps the tutored basic");

        _alice.Zones.Library.GetCards().Should().NotContain(forest);

        _alice.Zones.Graveyard.GetCards().Should().Contain(elder,
            "the elder was sacrificed as a cost");
        elder.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_LeavesNonbasicLandsInLibrary()
    {
        var forest = new Land("Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Library.AddCard(forest);
        forest.SetZone(ZoneType.Library);

        // Nonbasic — must NOT be a legal candidate.
        var bog = new Land("Bojuka Bog");
        bog.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bog);
        bog.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().Contain(forest);
        _alice.Zones.Library.GetCards().Should().Contain(bog,
            "Bojuka Bog has no Basic supertype; not a legal STE target");
    }

    [Fact]
    public void Activate_Tutor_NoBasicsInLibrary_StillSacrificesSelf_NoLandMoved()
    {
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
        sac.Resolve();

        _alice.Zones.Battlefield.GetCards().Should().NotContain(bolt);
        _alice.Zones.Library.GetCards().Should().Contain(bolt);

        // Cost was paid.
        _alice.Zones.Graveyard.GetCards().Should().Contain(elder);
        elder.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Activate_Tutor_NoBasicsInLibrary_StillPromptsAgentAndShufflesLibrary()
    {
        // (References below use the global:: namespace alias to avoid the
        // Majik.Core.Tests.Players.Agents et al. shadowing the engine
        // namespaces from inside Majik.Core.Tests.CardData.)
        // Regression: empty-candidates path used to silently no-op on the
        // agent prompt. The fix routes through LibrarySearch.PromptOnly
        // which always calls the agent so a human searcher SEES the
        // failed search in the portal modal. CR 701.20a — the search
        // happened, so the library still shuffles.
        var bolt = new Card("Lightning Bolt", "{R}");
        bolt.AddCardType(CardType.Instant);
        bolt.SetOwner(_alice);
        _alice.Zones.Library.AddCard(bolt);
        bolt.SetZone(ZoneType.Library);

        var elder = SakuraTribeElderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(elder);
        elder.SetZone(ZoneType.Battlefield);

        var agent = new EmptyPromptRecordingAgent();
        global::Majik.Core.Players.Agents.AgentRegistry.Set(_alice, agent);

        var shuffles = new List<global::Majik.Core.Events.LibraryShuffledEvent>();
        var bus = new global::Majik.Core.Events.EventBus();
        bus.Subscribe<global::Majik.Core.Events.LibraryShuffledEvent>(shuffles.Add);
        global::Majik.Core.Events.EventBusRegistry.Set(_alice, bus);

        try
        {
            var sac = elder.Abilities.OfType<ActivatedAbility>().Single();
            sac.Resolve();
        }
        finally
        {
            global::Majik.Core.Players.Agents.AgentRegistry.Clear();
            global::Majik.Core.Events.EventBusRegistry.Clear();
        }

        // Agent was prompted with empty candidates.
        agent.Calls.Should().Be(1);
        agent.LastCandidates.Should().BeEmpty();
        agent.LastLabel.Should().Be("basic land card");

        // CR 701.20a — shuffle fired with the sakura-tribe-elder reason.
        shuffles.Should().Contain(e => e.Reason == "sakura-tribe-elder");
    }

    private sealed class EmptyPromptRecordingAgent : global::Majik.Core.Players.Agents.IPlayerAgent
    {
        public int Calls { get; private set; }
        public IReadOnlyList<ICard>? LastCandidates { get; private set; }
        public string? LastLabel { get; private set; }

        public Task<ICard?> ChooseLibraryPickAsync(
            global::Majik.Core.Game.GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
        {
            Calls++;
            LastCandidates = candidates;
            LastLabel = kindLabel;
            return Task.FromResult<ICard?>(candidates.FirstOrDefault());
        }

        // Unused — throw to surface accidental calls.
        public Task<global::Majik.Core.Players.Agents.PriorityAction> ChoosePriorityActionAsync(global::Majik.Core.Game.GameContext ctx, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Players.Agents.MulliganDecision> ChooseMulliganAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(global::Majik.Core.Game.GameContext ctx, global::Majik.Core.Players.Agents.TargetRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseXAsync(global::Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseModeAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<global::Majik.Core.Cards.BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<global::Majik.Core.Abilities.ITriggeredAbility>> OrderTriggersAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<global::Majik.Core.Abilities.ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Players.Agents.ManaPayment> ChooseManaSourcesAsync(global::Majik.Core.Game.GameContext ctx, global::Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(global::Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers, IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(global::Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<global::Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(global::Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
