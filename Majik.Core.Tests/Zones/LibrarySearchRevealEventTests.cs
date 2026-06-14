using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Zones;

/// <summary>
/// CR 701.18 — "Reveal" makes a card public until the revealing effect stops
/// applying. The vast majority of tutors print "search your library for X,
/// <b>reveal it</b>, put it into your hand" (Worldly Tutor, Fierce Empath,
/// Civic Wayfinder, Sylvan Scrying, Sakura-Tribe Elder, …). Until now the
/// shared <see cref="LibrarySearch"/> primitive moved the picked card without
/// publishing a <see cref="CardRevealedEvent"/>, so a "whenever you reveal a
/// card" payoff (or the portal's reveal-flash UI) never saw the tutor reveal.
///
/// These tests lock the new opt-in contract: when a tutor passes a
/// <c>revealReason</c> to <see cref="LibrarySearch.PromptOnlyAsync"/> and a
/// card is actually found, the primitive publishes exactly one
/// <see cref="CardRevealedEvent"/> for the found card, tagged
/// <see cref="ZoneType.Library"/>. Tutors that do NOT reveal (Wood Elves puts
/// the Forest straight onto the battlefield) pass no reason and emit nothing —
/// the parameter defaults to <c>null</c> so every existing caller is unchanged.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
public class LibrarySearchRevealEventTests : IDisposable
{
    private readonly Player _alice;
    private readonly EventBus _bus;
    private readonly List<CardRevealedEvent> _reveals = new();

    public LibrarySearchRevealEventTests()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
        GameRandomRegistry.SetDefault(new GameRandom(seed: 0));

        _alice = new Player("Alice");
        _bus = new EventBus();
        _bus.Subscribe<CardRevealedEvent>(_reveals.Add);
        EventBusRegistry.SetDefault(_bus);
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
        GameRandomRegistry.Clear();
        EventBusRegistry.Clear();
        EventBusRegistry.SetDefault(null);
    }

    private sealed class FixedPickAgent : IPlayerAgent
    {
        public ICard? PickToReturn { get; init; }

        public Task<ICard?> ChooseLibraryPickAsync(
            GameContext? ctx, IReadOnlyList<ICard> candidates, string kindLabel,
            CancellationToken ct = default) => Task.FromResult(PickToReturn);

        public Task<ICard?> ChooseFromRevealedAsync(GameContext? ctx, IReadOnlyList<ICard> revealed, IReadOnlyList<ICard> eligible, bool optional, string label, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotImplementedException();
    }

    private Creature SeedLibraryWith(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2) { Owner = _alice, Zone = ZoneType.Library };
        _alice.Zones.Library.AddCard(c);
        return c;
    }

    private ResolutionContext Ctx(IPlayerAgent agent) =>
        ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

    [Fact]
    public async Task PromptOnlyAsync_WithRevealReason_PublishesRevealForFoundCard()
    {
        var bear = SeedLibraryWith("Bear");
        var agent = new FixedPickAgent { PickToReturn = bear };

        var pick = await LibrarySearch.PromptOnlyAsync(
            Ctx(agent), _alice, new List<ICard> { bear }, "creature card",
            revealReason: "Worldly Tutor");

        pick.Should().Be(bear);
        _reveals.Should().ContainSingle("a found+revealed tutor card is made public (CR 701.18)");
        var ev = _reveals[0];
        ev.Card.InstanceId.Should().Be(bear.InstanceId);
        ev.Player.Should().Be(_alice);
        ev.From.Should().Be(ZoneType.Library);
        ev.Reason.Should().Be("Worldly Tutor");
    }

    [Fact]
    public async Task PromptOnlyAsync_WithoutRevealReason_PublishesNothing()
    {
        var bear = SeedLibraryWith("Bear");
        var agent = new FixedPickAgent { PickToReturn = bear };

        var pick = await LibrarySearch.PromptOnlyAsync(
            Ctx(agent), _alice, new List<ICard> { bear }, "creature card");

        pick.Should().Be(bear);
        _reveals.Should().BeEmpty("a tutor that does not reveal (e.g. Wood Elves) emits no reveal event");
    }

    [Fact]
    public async Task PromptOnlyAsync_RevealReason_ButDeclinedPick_PublishesNothing()
    {
        var bear = SeedLibraryWith("Bear");
        // Agent declines (find nothing — always legal, CR 701.18a).
        var agent = new FixedPickAgent { PickToReturn = null };

        var pick = await LibrarySearch.PromptOnlyAsync(
            Ctx(agent), _alice, new List<ICard> { bear }, "creature card",
            revealReason: "Worldly Tutor");

        pick.Should().BeNull();
        _reveals.Should().BeEmpty("no card was found, so nothing is revealed");
    }

    [Fact]
    public async Task PromptAndShuffleAsync_WithRevealReason_PublishesRevealForFoundCard()
    {
        var bear = SeedLibraryWith("Bear");
        var agent = new FixedPickAgent { PickToReturn = bear };

        var pick = await LibrarySearch.PromptAndShuffleAsync(
            Ctx(agent), _alice, new List<ICard> { bear }, "creature card",
            "worldly-tutor", revealReason: "Worldly Tutor");

        pick.Should().Be(bear);
        _reveals.Should().ContainSingle();
        _reveals[0].From.Should().Be(ZoneType.Library);
        _reveals[0].Reason.Should().Be("Worldly Tutor");
    }

    // ---------------------------------------------------------------------
    // End-to-end: an actual revealing tutor card (Sylvan Scrying — "search
    // your library for a land card, reveal it, …") surfaces the reveal
    // through the shared SearchSpellFactory path; a non-revealing tutor
    // (Profane Tutor — "search your library for a card, put it into your
    // hand, …") does NOT.
    // ---------------------------------------------------------------------

    [Fact]
    public void SylvanScrying_Resolve_PublishesRevealForFoundLand()
    {
        var forest = new Land("Forest",
            new[] { Majik.Core.Cards.Types.CardSupertype.Basic },
            new[] { Majik.Core.Cards.Types.CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Library.AddCard(forest);
        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        var spell = Majik.Core.CardData.Factories.SylvanScryingFactory.BuildSpellDefinition(_alice);
        foreach (var fx in spell.EffectFactory(new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty)))
        {
            fx.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Name.Should().Be("Forest");
        _reveals.Should().ContainSingle("CR 701.18 — Sylvan Scrying reveals the found land");
        _reveals[0].Card.Name.Should().Be("Forest");
        _reveals[0].From.Should().Be(ZoneType.Library);
        _reveals[0].Reason.Should().Be("Sylvan Scrying");
    }

    [Fact]
    public void ProfaneTutor_Resolve_DoesNotPublishReveal()
    {
        // Profane Tutor: "Search your library for a card, put that card into
        // your hand, then shuffle." — no "reveal" in oracle text.
        var bear = SeedLibraryWith("Bear");
        AgentRegistry.Set(_alice, new DeterministicBotAgent());

        var spell = Majik.Core.CardData.Factories.ProfaneTutorFactory.BuildSpellDefinition(_alice);
        foreach (var fx in spell.EffectFactory(new ChosenSpellParams(
            null, null, Array.Empty<IReadOnlyList<object>>(), ManaPayment.Empty)))
        {
            fx.Execute();
        }

        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Name.Should().Be("Bear");
        _reveals.Should().BeEmpty("Profane Tutor does not reveal the tutored card");
    }
}
