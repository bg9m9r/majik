using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Coverage for the first-class "choose a card name" agent surface —
/// <see cref="IPlayerAgent.ChooseCardNameAsync"/> — plus the shared
/// <see cref="CardNameChoice"/> resolver and its production wiring into the
/// name-choosing hate pieces (pays down the
/// <c>choose-card-name-agent-surface</c> v1 deferral).
///
/// CR 614.12 / CR 201.4 — "as this enters, choose a card name." Before this
/// surface, the name-choosing cards relied on a test-only multi-arg factory
/// overload that handed in the chosen name directly; the production single-arg
/// build attached the static structurally with no live name (the restriction
/// was inert). Now the single-arg build prompts the controller's agent at
/// resolution via <see cref="CardNameChoice"/>.
///
/// Verifies:
///   1. The default interface implementation routes through the declarative
///      <see cref="IPlayerAgent.ChooseAsync"/> sink and respects the agent's
///      pick / suggestion fallback / no-pool fallback.
///   2. <see cref="CardNameChoice.SuggestNames"/> ranks the opponents' visible
///      cards most-threatening-first and honours the nonland filter.
///   3. A name-supplying agent registered for the controller drives the
///      Pithing Needle / Meddling Mage single-arg production build (the
///      restriction now activates with no test-only name parameter).
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class ChooseCardNameTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public void Dispose()
    {
        ActivatedAbilityRestrictions.Clear();
        CastingRestrictions.Clear();
    }

    private GameContext MakeGame() => new GameContext(
        self: _alice,
        allPlayers: new[] { _alice, _bob },
        activePlayer: _alice,
        turnNumber: 1,
        currentPhase: null,
        stack: new Majik.Core.Stack.Stack());

    // ── Bare agent: only the mandatory abstract members; everything else
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

    // Agent whose ChooseAsync over a string-candidate PickOne returns a
    // chosen name (re-implements IPlayerAgent so its ChooseAsync replaces the
    // default interface method in dispatch).
    private sealed class NamePickingAgent : BareAgent, IPlayerAgent
    {
        private readonly Func<IReadOnlyList<object>, IReadOnlyList<object>> _pick;
        public NamePickingAgent(Func<IReadOnlyList<object>, IReadOnlyList<object>> pick) => _pick = pick;

        public Task<IReadOnlyList<object>> ChooseAsync(GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
            => Task.FromResult(_pick(req.Candidates ?? Array.Empty<object>()));
    }

    // ────────────────────────────────────────────────────────────────────
    // 1. Default-implementation posture.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Default_WithSuggestions_RoutesAgentPickThroughChooseAsync()
    {
        // Agent picks the SECOND suggested name.
        IPlayerAgent agent = new NamePickingAgent(c => new[] { c[1] });

        var chosen = await agent.ChooseCardNameAsync(
            ctx: null,
            suggested: new[] { "Lightning Bolt", "Tarmogoyf" },
            constraintLabel: "a card name");

        chosen.Should().Be("Tarmogoyf");
    }

    [Fact]
    public async Task Default_WithSuggestions_AgentDeclines_FallsBackToTopSuggestion()
    {
        // Bare agent's default ChooseAsync over a non-optional string PickOne
        // returns the first candidate, so the top suggestion is named.
        IPlayerAgent agent = new BareAgent();

        var chosen = await agent.ChooseCardNameAsync(
            ctx: null,
            suggested: new[] { "Griselbrand", "Emrakul" },
            constraintLabel: "a card name");

        chosen.Should().Be("Griselbrand");
    }

    [Fact]
    public async Task Default_NoSuggestions_ReturnsFallback()
    {
        IPlayerAgent agent = new BareAgent();

        var chosen = await agent.ChooseCardNameAsync(
            ctx: null,
            suggested: Array.Empty<string>(),
            constraintLabel: "a card name",
            fallback: "");

        chosen.Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // 2. CardNameChoice.SuggestNames — ranked known-threat survey.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void SuggestNames_RanksOpponentsBattlefield_MostThreateningFirst()
    {
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob); goyf.SetController(_bob); goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var grim = new Creature("Griselbrand", "{4}{B}{B}{B}{B}", 7, 7);
        grim.SetOwner(_bob); grim.SetController(_bob); grim.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(grim);

        var game = MakeGame();

        var names = CardNameChoice.SuggestNames(game, _alice);

        // Griselbrand (MV 8) ranks above Tarmogoyf (MV 2).
        names.Should().ContainInOrder("Griselbrand", "Tarmogoyf");
    }

    [Fact]
    public void SuggestNames_NonlandFilter_DropsLands()
    {
        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob); goyf.SetController(_bob); goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var land = new Land("Wasteland", null, null);
        land.SetOwner(_bob); land.SetController(_bob); land.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(land);

        var game = MakeGame();

        var nonland = CardNameChoice.SuggestNames(game, _alice, nonlandOnly: true);
        nonland.Should().Contain("Tarmogoyf").And.NotContain("Wasteland");

        var all = CardNameChoice.SuggestNames(game, _alice, nonlandOnly: false);
        all.Should().Contain("Wasteland");
    }

    [Fact]
    public void SuggestNames_NullGame_ReturnsEmpty()
    {
        CardNameChoice.SuggestNames(null, _alice).Should().BeEmpty();
    }

    // ────────────────────────────────────────────────────────────────────
    // 3. Production wiring — single-arg builds prompt the registered agent.
    // ────────────────────────────────────────────────────────────────────

    [Fact]
    public void PithingNeedle_ProductionBuild_PromptsAgent_AndRegistersChosenName()
    {
        var bus = new EventBus();

        // Bob has Walking Ballista on the battlefield — the known threat the
        // Needle should name. Alice's agent picks the top suggestion.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ballista);

        var game = MakeGame();
        AgentRegistry.Set(_alice, new BareAgent()); // default ChooseAsync → top suggestion

        try
        {
            // Production-shaped overload: NO test-only name parameter.
            var needle = PithingNeedleFactory.Create(_alice, game, bus);
            needle.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

            ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
                .Should().BeTrue("the agent named the most-threatening known card via the new surface");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void PithingNeedle_ProductionBuild_NoAgent_StaysInert()
    {
        var bus = new EventBus();
        // No agent registered, no game → empty suggestion pool, empty name.
        var needle = PithingNeedleFactory.Create(_alice);
        needle.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(needle, ZoneType.Hand, ZoneType.Battlefield));

        ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista").Should().BeFalse();
    }

    [Fact]
    public void SorcerousSpyglass_ProductionBuild_PromptsAgent_AndRegistersChosenName()
    {
        var bus = new EventBus();

        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ballista.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(ballista);

        var game = MakeGame();
        AgentRegistry.Set(_alice, new BareAgent());

        try
        {
            var spyglass = SorcerousSpyglassFactory.Create(_alice, game, bus);
            spyglass.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(spyglass, ZoneType.Hand, ZoneType.Battlefield));

            ActivatedAbilityRestrictions.IsNameRestricted("Walking Ballista")
                .Should().BeTrue("the agent named the most-threatening known card via the new surface");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    [Fact]
    public void MeddlingMage_ProductionBuild_PromptsAgent_AndBlocksNamedSpell()
    {
        var bus = new EventBus();

        var goyf = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        goyf.SetOwner(_bob); goyf.SetController(_bob); goyf.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(goyf);

        var game = MakeGame();
        AgentRegistry.Set(_alice, new BareAgent());

        try
        {
            var mage = MeddlingMageFactory.Create(_alice, game, bus);

            // Move onto the battlefield so the lifecycle registers the block.
            mage.SetZone(ZoneType.Battlefield);
            bus.Publish(new CardMovedEvent(mage, ZoneType.Hand, ZoneType.Battlefield));

            CastingRestrictions.IsCardNameBlocked("Tarmogoyf")
                .Should().BeTrue("the agent named the most-threatening visible nonland card");
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }
}
