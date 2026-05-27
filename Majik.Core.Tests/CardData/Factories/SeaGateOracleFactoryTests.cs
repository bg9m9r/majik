using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SeaGateOracleFactory"/>.
///
/// Card: Sea Gate Oracle — Creature — Human Wizard {2}{U} 1/3.
/// Oracle text:
///   "When this creature enters, look at the top two cards of your
///    library. Put one of them into your hand and the other on the
///    bottom of your library."
///
/// Covers:
/// - Identity ({2}{U}, 1/3, Creature — Human Wizard, blue).
/// - NamedCardFactory dispatch.
/// - Exactly one battlefield-active ETB TriggeredAbility.
/// - ETB: stocked library (≥2) — 1 card to hand, 1 to bottom.
/// - ETB: stocked library (≥2) with agent — agent's chosen card goes to hand.
/// - ETB: library has exactly 1 card — that card goes to hand, nothing to bottom.
/// - ETB: empty library — no-op, no crash.
/// </summary>
public class SeaGateOracleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_Identity()
    {
        var c = SeaGateOracleFactory.Create(_alice);

        c.Name.Should().Be("Sea Gate Oracle");
        c.ManaCost.Should().Be("{2}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Sea Gate Oracle is a Human");
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue("Sea Gate Oracle is a Wizard");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Blue color identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_IsBlue()
    {
        var c = SeaGateOracleFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Blue,
            "Sea Gate Oracle has {U} in its mana cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Sea Gate Oracle", _alice);

        c.Should().BeOfType<Creature>("Sea Gate Oracle is a Creature");
        c.Name.Should().Be("Sea Gate Oracle");
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.ManaCost.Should().Be("{2}{U}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_ExactlyOneBattlefieldActiveEtbTrigger()
    {
        var c = SeaGateOracleFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();

        triggers.Should().HaveCount(1,
            "Sea Gate Oracle has exactly one triggered ability — the ETB look");

        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are active while the permanent is on the battlefield (CR 603.6a)");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability — look at top 2, one to hand, other to bottom
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_EtbTrigger_StockedLibrary_OneToHand_OtherToBottom()
    {
        var alice = new Player("Alice", 20);

        // Seed library with three cards so we can verify 'bottom' placement.
        var c1 = new Card("TopCard", "");
        var c2 = new Card("SecondCard", "");
        var c3 = new Card("ThirdCard", "");
        foreach (var (card, i) in new[] { c1, c2, c3 }.Select((c, i) => (c, i)))
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var oracle = SeaGateOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        // Deterministic fallback: first card (c1) goes to hand,
        // second card (c2) goes to the BOTTOM of the library.
        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "exactly one of the two peeked cards goes to hand");
        alice.Zones.Hand.GetCards().Should().Contain(c1,
            "deterministic fallback puts the first (top) card into hand");

        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "two cards remain in library — c3 (was already there) + c2 (to bottom)");

        // c2 should be at the bottom of the library (last position).
        alice.Zones.Library.GetCards().Last().Should().BeSameAs(c2,
            "the other peeked card goes to the bottom of the library");

        // c3 should still be above c2.
        alice.Zones.Library.GetCards().Should().ContainInOrder(c3, c2);
    }

    [Fact]
    public void SeaGateOracle_EtbTrigger_LibrarySizeReducesByOne()
    {
        var alice = new Player("Alice", 20);

        var c1 = new Card("Top", "");
        var c2 = new Card("Second", "");
        var c3 = new Card("Third", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var oracle = SeaGateOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "net -1 to library: one of the top-two moved to hand");
        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "net +1 to hand");
    }

    // -----------------------------------------------------------------------
    // ETB — agent-driven selection
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_EtbTrigger_AgentChoosesSecondCard_GoesToHand()
    {
        var alice = new Player("Alice", 20);

        var c1 = new Card("TopCard", "");
        var c2 = new Card("SecondCard", "");
        foreach (var card in new[] { c1, c2 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        // Register an agent that always picks c2 (the second card).
        var agent = new PickSpecificCardAgent(c2);
        AgentRegistry.Set(alice, agent);

        try
        {
            var oracle = SeaGateOracleFactory.Create(alice);
            var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();
            foreach (var effect in etb.Effects) effect.Execute();

            alice.Zones.Hand.GetCards().Should().Contain(c2,
                "agent chose c2 → c2 goes to hand");
            alice.Zones.Library.GetCards().Should().Contain(c1,
                "c1 (not chosen) goes to the bottom of the library");
            alice.Zones.Hand.GetCards().Should().HaveCount(1);
        }
        finally
        {
            AgentRegistry.Clear();
        }
    }

    // -----------------------------------------------------------------------
    // ETB — graceful edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public void SeaGateOracle_EtbTrigger_ExactlyOneCardInLibrary_ThatCardGoesToHand()
    {
        var alice = new Player("Alice", 20);

        var only = new Card("OnlyCard", "");
        only.SetOwner(alice);
        alice.Zones.Library.AddCard(only);
        only.SetZone(ZoneType.Library);

        var oracle = SeaGateOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("fewer than 2 cards is a graceful short-circuit");

        alice.Zones.Hand.GetCards().Should().Contain(only,
            "the single available card goes to hand");
        alice.Zones.Library.GetCards().Should().BeEmpty(
            "library is empty after the one card moved to hand");
    }

    [Fact]
    public void SeaGateOracle_EtbTrigger_EmptyLibrary_NoCrash_NothingMoves()
    {
        var alice = new Player("Alice", 20);
        // Library is intentionally empty.

        var oracle = SeaGateOracleFactory.Create(alice);
        var etb = oracle.Abilities.OfType<TriggeredAbility>().Single();

        var act = () => { foreach (var effect in etb.Effects) effect.Execute(); };
        act.Should().NotThrow("empty library is a valid no-op (no forced draw)");

        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "nothing in library → nothing moves to hand");
    }

    // -----------------------------------------------------------------------
    // Helper: scripted agent that picks a specific card (delegates all other
    // decisions to DeterministicBotAgent).
    // -----------------------------------------------------------------------

    private sealed class PickSpecificCardAgent : IPlayerAgent
    {
        private readonly ICard _pick;
        private readonly DeterministicBotAgent _base = new();

        public PickSpecificCardAgent(ICard pick) => _pick = pick;

        public Task<ICard?> ChooseLibraryPickAsync(
            Majik.Core.Game.GameContext? ctx,
            IReadOnlyList<ICard> candidates,
            string kindLabel,
            CancellationToken ct = default)
            => Task.FromResult<ICard?>(_pick);

        // Delegate all other decisions to the deterministic bot.
        public Task<Majik.Core.Players.Agents.PriorityAction> ChoosePriorityActionAsync(
            Majik.Core.Game.GameContext ctx, CancellationToken ct = default)
            => _base.ChoosePriorityActionAsync(ctx, ct);

        public Task<Majik.Core.Players.Agents.MulliganDecision> ChooseMulliganAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int mulligansTaken, CancellationToken ct = default)
            => _base.ChooseMulliganAsync(ctx, hand, mulligansTaken, ct);

        public Task<IReadOnlyList<ICard>> ChooseCardsToBottomAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ICard> hand, int countToBottom, CancellationToken ct = default)
            => _base.ChooseCardsToBottomAsync(ctx, hand, countToBottom, ct);

        public Task<IReadOnlyList<object>> ChooseTargetsAsync(
            Majik.Core.Game.GameContext ctx, TargetRequest request, CancellationToken ct = default)
            => _base.ChooseTargetsAsync(ctx, request, ct);

        public Task<int> ChooseXAsync(
            Majik.Core.Game.GameContext ctx, ICard source, CancellationToken ct = default)
            => _base.ChooseXAsync(ctx, source, ct);

        public Task<int> ChooseModeAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<string> modes,
            IReadOnlyList<BotIntent>? modeIntents = null, CancellationToken ct = default)
            => _base.ChooseModeAsync(ctx, modes, modeIntents, ct);

        public Task<IReadOnlyList<ITriggeredAbility>> OrderTriggersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<ITriggeredAbility> mine, CancellationToken ct = default)
            => _base.OrderTriggersAsync(ctx, mine, ct);

        public Task<ManaPayment> ChooseManaSourcesAsync(
            Majik.Core.Game.GameContext ctx, Majik.Core.ValueObjects.ManaCost cost, CancellationToken ct = default)
            => _base.ChooseManaSourcesAsync(ctx, cost, ct);

        public Task<Majik.Core.Players.Agents.CombatPlan> DeclareAttackersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> eligibleAttackers, CancellationToken ct = default)
            => _base.DeclareAttackersAsync(ctx, eligibleAttackers, ct);

        public Task<Majik.Core.Players.Agents.BlockPlan> DeclareBlockersAsync(
            Majik.Core.Game.GameContext ctx, IReadOnlyList<Creature> attackers,
            IReadOnlyList<Creature> eligibleBlockers, CancellationToken ct = default)
            => _base.DeclareBlockersAsync(ctx, attackers, eligibleBlockers, ct);

        public Task<Majik.Core.Keywords.ScryAction.ScryDecision> ChooseScryDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _base.ChooseScryDecisionAsync(ctx, peeked, ct);

        public Task<Majik.Core.Keywords.SurveilAction.SurveilDecision> ChooseSurveilDecisionAsync(
            Majik.Core.Game.GameContext? ctx, IReadOnlyList<ICard> peeked, CancellationToken ct = default)
            => _base.ChooseSurveilDecisionAsync(ctx, peeked, ct);
    }
}
