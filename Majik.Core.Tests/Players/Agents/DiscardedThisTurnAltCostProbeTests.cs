using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.CardData.Factories;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players.Agents;

/// <summary>
/// Unit tests for <see cref="DiscardedThisTurnAltCostProbe"/> — the live
/// engine seam that surfaces Asmoranomardicadaistinaculdacar's discard-gated
/// {B/R} alternative cast cost (CR 118.9) to the bot's spell-cast
/// enumeration. Without this probe the {B/R} permission existed only as a
/// caller-built cost (<see cref="AsmoranomardicadaistinaculdacarFactory.BuildAlternativeCost"/>)
/// that nothing in the live dispatch path ever discovered.
///
/// Validates: hand-zone + owner filtering, the per-turn discard gate read
/// off <see cref="GameContext.TurnState"/>, and default-registry membership.
/// </summary>
public class DiscardedThisTurnAltCostProbeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Majik.Core.Stack.Stack _stack;

    public DiscardedThisTurnAltCostProbeTests()
    {
        _stack = new Majik.Core.Stack.Stack(new Majik.Core.Events.EventBus());
    }

    [Fact]
    public void CandidatesFor_AfterDiscarding_EmitsDiscardGatedAltCost()
    {
        var asmo = InHand(_alice, AsmoranomardicadaistinaculdacarFactory.Create(_alice));
        var turnState = new TurnState();
        turnState.RecordCardDiscarded(_alice);

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        var candidates = probe.CandidatesFor(asmo, _alice, Ctx(turnState)).ToList();

        candidates.Should().HaveCount(1);
        var alt = candidates[0].Should().BeOfType<DiscardedThisTurnAlternativeCost>().Subject;
        alt.CanCastFor(asmo, _alice).Should().BeTrue(
            "a card has been discarded this turn (CR 118.9)");
        alt.AlternativeManaCost.HybridPips.Should().ContainSingle(
            "Asmoran's alternative cost is a single {B/R} hybrid pip");
    }

    [Fact]
    public void CandidatesFor_NoDiscardThisTurn_NoCandidates()
    {
        var asmo = InHand(_alice, AsmoranomardicadaistinaculdacarFactory.Create(_alice));
        var turnState = new TurnState(); // nobody discarded yet

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        probe.CandidatesFor(asmo, _alice, Ctx(turnState)).Should().BeEmpty(
            "the {B/R} alternative is unavailable until you've discarded a card this turn");
    }

    [Fact]
    public void CandidatesFor_OnlyOpponentDiscarded_NoCandidates()
    {
        var asmo = InHand(_alice, AsmoranomardicadaistinaculdacarFactory.Create(_alice));
        var turnState = new TurnState();
        turnState.RecordCardDiscarded(_bob); // opponent's discard doesn't count

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        probe.CandidatesFor(asmo, _alice, Ctx(turnState)).Should().BeEmpty(
            "CR 118.9 — the gate reads the CASTER's own discards this turn");
    }

    [Fact]
    public void CandidatesFor_CardNotInHand_NoCandidates()
    {
        var asmo = AsmoranomardicadaistinaculdacarFactory.Create(_alice);
        asmo.SetOwner(_alice);
        asmo.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(asmo);
        var turnState = new TurnState();
        turnState.RecordCardDiscarded(_alice);

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        probe.CandidatesFor(asmo, _alice, Ctx(turnState)).Should().BeEmpty(
            "a spell is cast from hand (CR 601.2)");
    }

    [Fact]
    public void CandidatesFor_OpponentOwnsCard_NoCandidates()
    {
        var asmo = InHand(_bob, AsmoranomardicadaistinaculdacarFactory.Create(_bob));
        var turnState = new TurnState();
        turnState.RecordCardDiscarded(_alice);

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        probe.CandidatesFor(asmo, _alice, Ctx(turnState)).Should().BeEmpty(
            "Alice cannot cast a card sitting in Bob's hand");
    }

    [Fact]
    public void CandidatesFor_NonAsmoranCard_NoCandidates()
    {
        var random = InHand(_alice, new Instant("Random Card", "{R}"));
        var turnState = new TurnState();
        turnState.RecordCardDiscarded(_alice);

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        probe.CandidatesFor(random, _alice, Ctx(turnState)).Should().BeEmpty(
            "only cards with a discard-gated alternative cost should match");
    }

    [Fact]
    public void CandidatesFor_NullTurnState_NoCandidates()
    {
        var asmo = InHand(_alice, AsmoranomardicadaistinaculdacarFactory.Create(_alice));

        var probe = new DiscardedThisTurnAltCostProbe(DiscardedThisTurnAltCostProbe.DefaultLookup);
        // GameContext without a threaded TurnState — the gate is closed.
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, _stack);
        probe.CandidatesFor(asmo, _alice, ctx).Should().BeEmpty(
            "with no live TurnState the discard ledger is unreadable, so the gate stays closed");
    }

    [Fact]
    public void DefaultLookup_KnowsAsmoran()
    {
        DiscardedThisTurnAltCostProbe.DefaultLookup(
            AsmoranomardicadaistinaculdacarFactory.Create(_alice))
            .Should().Be(AsmoranomardicadaistinaculdacarFactory.AlternativeManaCost);

        DiscardedThisTurnAltCostProbe.DefaultLookup(new Instant("Random", "{R}"))
            .Should().BeNull();
    }

    [Fact]
    public void Registry_CreateDefault_IncludesDiscardedThisTurnProbe()
    {
        var registry = AlternativeCostProbeRegistry.CreateDefault();
        registry.Probes.OfType<DiscardedThisTurnAltCostProbe>().Should().HaveCount(1,
            "the default registry must ship with the discard-gated alt-cost probe (CR 118.9)");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static T InHand<T>(Player owner, T card) where T : Card
    {
        card.SetOwner(owner);
        card.SetZone(ZoneType.Hand);
        owner.Zones.Hand.AddCard(card);
        return card;
    }

    private GameContext Ctx(TurnState turnState) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1,
            StepStateType.PreCombatMain, _stack, landPlayAvailable: true, turnState: turnState);
}
