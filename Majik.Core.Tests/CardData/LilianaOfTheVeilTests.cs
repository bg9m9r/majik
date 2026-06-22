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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Liliana of the Veil (Innistrad, {1}{B}{B}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Liliana subtype, loyalty 3,
///     mana cost).
///   - Loyalty ability shape: three abilities at +1 / -2 / -6.
///   - Mechanic: +1 each player discards a card (auto-pick).
///   - Mechanic: -2 target player (auto-picked opponent) sacrifices a
///     creature.
///   - Loyalty cost is paid even when the effect body no-ops (no
///     allPlayersResolver, ultimate, etc.).
///   - NamedCardFactory dispatch.
/// </summary>
public class LilianaOfTheVeilTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    /// <summary>
    /// Resolve a loyalty ability's effects against a LIVE resolution context.
    /// The prod path now reads players / opponents off rc.Game (the
    /// resolver-null loyalty fix), so the legacy resolver-free
    /// <see cref="LoyaltyAbility.Activate"/> can't exercise the each-player /
    /// target halves. Pays the loyalty cost, builds a GameContext, then resolves
    /// each effect with rc.Controller = the activator.
    /// </summary>
    private static void ActivateWithContext(LoyaltyAbility ability, Player controller, params Player[] all)
    {
        ability.PayLoyaltyCost();
        var game = new GameContext(
            self: controller,
            allPlayers: all,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack());
        var rc = ResolutionContext.For(controller, agent: null, game: game, chosenTargets: null);
        foreach (var effect in ability.Effects)
            effect.ExecuteAsync(rc).GetAwaiter().GetResult();
    }

    [Fact]
    public void Liliana_IsLegendaryPlaneswalker_Liliana_3Loyalty_AtCost1BB()
    {
        var liliana = LilianaOfTheVeilFactory.Create(_alice);

        liliana.Name.Should().Be("Liliana of the Veil");
        liliana.ManaCost.Should().Be("{1}{B}{B}");
        liliana.HasType(CardType.Planeswalker).Should().BeTrue();
        liliana.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        liliana.HasSubtype(CardSubtype.Liliana).Should().BeTrue();
        liliana.Loyalty.Should().Be(3);
        liliana.StartingLoyalty.Should().Be(3);
        liliana.Owner.Should().BeSameAs(_alice);
        liliana.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Liliana_HasThreeLoyaltyAbilities_Plus1_Minus2_Minus6()
    {
        var liliana = LilianaOfTheVeilFactory.Create(_alice);
        var loyaltyAbilities = liliana.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(3);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2, -6 });
    }

    [Fact]
    public void Liliana_Plus1_EachPlayerDiscardsACard()
    {
        // Give each player one card in hand.
        var aliceCard = new Card("Alice spell", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Hand);

        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var liliana = LilianaOfTheVeilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        ActivateWithContext(plus1, _alice, _alice, _bob);

        // Loyalty went 3 → 4.
        liliana.Loyalty.Should().Be(4);

        // Both players discarded.
        _alice.Zones.Hand.GetCards().Should().NotContain(aliceCard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard);
        _bob.Zones.Hand.GetCards().Should().NotContain(bobCard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobCard);
    }

    [Fact]
    public void Liliana_Minus2_TargetPlayerSacrificesACreature()
    {
        // Bob has a creature on the battlefield; Liliana's -2 sacrifices it.
        var victim = new Creature("Goblin", "R", 1, 1);
        victim.SetOwner(_bob);
        victim.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(victim);
        victim.SetZone(ZoneType.Battlefield);

        var liliana = LilianaOfTheVeilFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var minus2 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        // -2 reads its opponent off the live ResolutionContext (no chosen
        // target on this path → ContextOpponents fallback).
        ActivateWithContext(minus2, _alice, _alice, _bob);

        liliana.Loyalty.Should().Be(1, "3 - 2 = 1");

        _bob.Zones.Battlefield.GetCards().Should().NotContain(victim);
        _bob.Zones.Graveyard.GetCards().Should().Contain(victim);
        victim.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void Liliana_Minus6_UltimateNoOp_StillPaysLoyaltyCost()
    {
        // Set loyalty up so -6 can legally activate.
        var liliana = LilianaOfTheVeilFactory.Create(_alice);
        liliana.AddLoyalty(5); // 3 → 8

        var ultimate = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -6);
        ultimate.CanActivate().Should().BeTrue();
        ultimate.Activate();

        liliana.Loyalty.Should().Be(2, "8 - 6 = 2; loyalty change applies even when the effect body is deferred");
    }

    [Fact]
    public void Liliana_Plus1_NoLiveGameContext_LoyaltyStillTicksUp()
    {
        // The legacy direct Activate() path resolves with no live game context
        // (ResolutionContext.Legacy → rc.Game == null), so the each-player
        // discard is a silent no-op while the loyalty change still applies
        // (CR 606.3). The prod routed build resolves with a live context and
        // does run the discard (see Liliana_Plus1_EachPlayerDiscardsACard).
        var liliana = LilianaOfTheVeilFactory.Create(_alice);

        // Give Alice a card in hand; the no-context resolve leaves it alone.
        var card = new Card("c", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(card);
        card.SetZone(ZoneType.Hand);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        liliana.Loyalty.Should().Be(4);
        _alice.Zones.Hand.GetCards().Should().Contain(card,
            "no live game context → discard effect is a silent no-op");
    }

    [Fact]
    public void Liliana_Plus1_BuiltViaProdRoutedSingleArg_StillDiscards_NotInert()
    {
        // Regression for the resolver-null-loyalty-each-player-context-read
        // deferral: the prod routed build dispatches the single-arg Create
        // (NO captured player-list resolver). The +1 each-player discard must
        // still run by reading rc.Game.AllPlayers — it used to be INERT here.
        var aliceCard = new Card("Alice spell", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(aliceCard);
        aliceCard.SetZone(ZoneType.Hand);
        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Hand);

        var liliana = (Planeswalker)NamedCardFactory.Create("Liliana of the Veil", _alice);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        ActivateWithContext(plus1, _alice, _alice, _bob);

        _alice.Zones.Graveyard.GetCards().Should().Contain(aliceCard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobCard);
    }

    [Fact]
    public void Liliana_Plus1_EachPlayerChoosesOwnCard_ViaTheirOwnAgent()
    {
        // each-player-chooses-own-discard-agent-prompt deferral pay-down:
        // CR 701.16a / CR 118.x — when "each player discards a card", EACH
        // player chooses THEIR OWN card. The prod routed build (single-arg
        // Create) must consult each affected player's OWN agent (looked up off
        // the per-game AgentRegistry seam — #2543 / #2551b pattern), NOT pick
        // the deterministic first-in-hand. Here each player's agent picks the
        // SECOND card in hand, so first-in-hand would fail this test.
        using var _ = AgentRegistry.PushScope();

        var aliceKeep = new Card("Alice keep", "B") { Owner = _alice };
        var alicePitch = new Card("Alice pitch", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(aliceKeep); aliceKeep.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(alicePitch); alicePitch.SetZone(ZoneType.Hand);

        var bobKeep = new Card("Bob keep", "U") { Owner = _bob };
        var bobPitch = new Card("Bob pitch", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobKeep); bobKeep.SetZone(ZoneType.Hand);
        _bob.Zones.Hand.AddCard(bobPitch); bobPitch.SetZone(ZoneType.Hand);

        AgentRegistry.Set(_alice, new PickSpecificDiscardAgent(alicePitch));
        AgentRegistry.Set(_bob, new PickSpecificDiscardAgent(bobPitch));

        var liliana = (Planeswalker)NamedCardFactory.Create("Liliana of the Veil", _alice);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        ActivateWithContext(plus1, _alice, _alice, _bob);

        // Each player discarded the card THEIR OWN agent chose (the 2nd one),
        // and kept the first — proving per-player agent consultation, not a
        // global first-in-hand pick.
        _alice.Zones.Graveyard.GetCards().Should().Contain(alicePitch);
        _alice.Zones.Hand.GetCards().Should().Contain(aliceKeep);
        _bob.Zones.Graveyard.GetCards().Should().Contain(bobPitch);
        _bob.Zones.Hand.GetCards().Should().Contain(bobKeep);
    }

    [Fact]
    public void Liliana_Plus1_RoutesThroughFxDiscardCard_PublishesDiscardedEvent()
    {
        // The discard must funnel through Fx.DiscardCard so DiscardedEvent fires
        // (madness / "whenever you discard …" triggers observe it) — CR 701.8.
        // The raw hand→graveyard move it replaced never published the event.
        using var _ = EventBusRegistry.PushScope();
        var aliceBus = new EventBus();
        var bobBus = new EventBus();
        var aliceDiscards = new List<DiscardedEvent>();
        var bobDiscards = new List<DiscardedEvent>();
        aliceBus.Subscribe<DiscardedEvent>(aliceDiscards.Add);
        bobBus.Subscribe<DiscardedEvent>(bobDiscards.Add);
        EventBusRegistry.Set(_alice, aliceBus);
        EventBusRegistry.Set(_bob, bobBus);

        var aliceCard = new Card("Alice spell", "B") { Owner = _alice };
        _alice.Zones.Hand.AddCard(aliceCard); aliceCard.SetZone(ZoneType.Hand);
        var bobCard = new Card("Bob spell", "U") { Owner = _bob };
        _bob.Zones.Hand.AddCard(bobCard); bobCard.SetZone(ZoneType.Hand);

        var liliana = (Planeswalker)NamedCardFactory.Create("Liliana of the Veil", _alice);
        _alice.Zones.Battlefield.AddCard(liliana);
        liliana.SetZone(ZoneType.Battlefield);

        var plus1 = liliana.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        ActivateWithContext(plus1, _alice, _alice, _bob);

        aliceDiscards.Should().ContainSingle("Alice's discard must route through Fx.DiscardCard")
            .Which.Card.Should().BeSameAs(aliceCard);
        aliceDiscards[0].WasCost.Should().BeFalse();
        bobDiscards.Should().ContainSingle("Bob's discard must route through Fx.DiscardCard")
            .Which.Card.Should().BeSameAs(bobCard);
    }

    /// <summary>Test agent that, for a discard prompt, returns a pre-chosen
    /// card from hand (used to prove each player picks their OWN card).</summary>
    private sealed class PickSpecificDiscardAgent : IPlayerAgent
    {
        private readonly ICard _pick;
        public PickSpecificDiscardAgent(ICard pick) => _pick = pick;

        public Task<ICard?> ChooseFromHandAsync(
            Player chooser, IReadOnlyList<ICard> candidates, BotIntent intent,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(_pick);

        // Unused surface.
        public Task<PriorityAction> ChoosePriorityActionAsync(GameContext ctx, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<MulliganDecision> ChooseMulliganAsync(GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<object>> ChooseTargetsAsync(GameContext ctx, TargetRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseXAsync(GameContext ctx, ICard source, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> ChooseModeAsync(GameContext ctx, IReadOnlyList<string> modes, IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ManaPayment> ChooseManaSourcesAsync(GameContext ctx, ManaCost cost, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<CombatPlan> DeclareAttackersAsync(GameContext ctx, IReadOnlyList<Permanent> eligibleAttackers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<BlockPlan> DeclareBlockersAsync(GameContext ctx, IReadOnlyList<Permanent> attackers, IReadOnlyList<Permanent> eligibleBlockers, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default) => throw new NotSupportedException();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LilianaOfTheVeil()
    {
        var card = NamedCardFactory.Create("Liliana of the Veil", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Liliana of the Veil");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Liliana).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(3);
        card.Owner.Should().Be(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(3);
    }
}
