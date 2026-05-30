using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrowbeatFactory"/> (Onslaught, {2}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Any player may have Browbeat deal 5 damage to them. If no one does,
///    target player draws three cards."
///
/// Covers:
///   - Identity ({2}{R} Sorcery, red, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - SpellDefinition shape: one "target player" request (1..1).
///   - No player accepts → target player draws three cards (CR 121.1).
///   - Caster's own decline does not gate the draw (the caster is in
///     "any player" and may decline alongside the opponent).
///   - A player accepts → that player takes 5 damage (CR 119) AND the
///     target does NOT draw ("if no one does").
///   - Multiple acceptors each take 5; draw still suppressed.
///   - Absent AllPlayers (all-decline default) → target draws three.
/// </summary>
public class BrowbeatFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams ChosenTargeting(
        Player target, params Player[] allPlayers) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: allPlayers.Length > 0 ? allPlayers : null);

    private static void FillLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var card = new Instant($"Filler {i}", "{U}") { Owner = p };
            p.Zones.Library.AddCard(card);
        }
    }

    // ── Shape / identity ─────────────────────────────────────────────────────

    [Fact]
    public void Browbeat_Identity_SorceryAtTwoRed()
    {
        var card = BrowbeatFactory.Create(_alice);

        card.Name.Should().Be("Browbeat");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{2}{R} = mana value 3");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Browbeat_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Browbeat", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Browbeat");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
    }

    [Fact]
    public void Browbeat_SpellDefinition_HasSingleTargetPlayerRequest()
    {
        var def = BrowbeatFactory.BuildSpellDefinition(o => o);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target player");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolution: no one accepts → draw three ──────────────────────────────

    [Fact]
    public void Browbeat_NoOneAccepts_TargetDrawsThreeCards()
    {
        // Bob is the target; both players decline the damage.
        FillLibrary(_alice, 5);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(false);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false);
        AgentRegistry.Set(_alice, aliceAgent);
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            // Target Alice (the caster) so she draws three.
            var def = BrowbeatFactory.BuildSpellDefinition(o => o);
            var chosen = ChosenTargeting(_alice, _alice, _bob);

            foreach (var e in def.EffectFactory(chosen)) e.Execute();

            _alice.Zones.Hand.GetCards().Should().HaveCount(3,
                "no player accepted the 5 damage, so the target draws three (CR 121.1)");
            _alice.Zones.Library.GetCards().Should().HaveCount(2);
            _alice.LifeTotal.Should().Be(20, "no one accepted the damage");
            _bob.LifeTotal.Should().Be(20);
        }
        finally
        {
            AgentRegistry.Remove(_alice);
            AgentRegistry.Remove(_bob);
        }
    }

    // ── Resolution: a player accepts → 5 damage, no draw ─────────────────────

    [Fact]
    public void Browbeat_OpponentAccepts_TakesFiveDamage_AndTargetDoesNotDraw()
    {
        FillLibrary(_alice, 5);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(false);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true); // Bob accepts the damage to deny the draw.
        AgentRegistry.Set(_alice, aliceAgent);
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var def = BrowbeatFactory.BuildSpellDefinition(o => o);
            var chosen = ChosenTargeting(_alice, _alice, _bob);

            foreach (var e in def.EffectFactory(chosen)) e.Execute();

            _bob.LifeTotal.Should().Be(15,
                "Bob accepted, so Browbeat deals 5 damage to him (CR 119)");
            _alice.Zones.Hand.GetCards().Should().BeEmpty(
                "a player accepted the damage, so 'if no one does' fails — no draw");
            _alice.Zones.Library.GetCards().Should().HaveCount(5);
        }
        finally
        {
            AgentRegistry.Remove(_alice);
            AgentRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void Browbeat_MultipleAccept_EachTakesFive_NoDraw()
    {
        FillLibrary(_alice, 5);
        var aliceAgent = new ScriptedAgent();
        aliceAgent.QueueYesNo(true);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true);
        AgentRegistry.Set(_alice, aliceAgent);
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var def = BrowbeatFactory.BuildSpellDefinition(o => o);
            var chosen = ChosenTargeting(_alice, _alice, _bob);

            foreach (var e in def.EffectFactory(chosen)) e.Execute();

            _alice.LifeTotal.Should().Be(15, "Alice accepted → 5 damage");
            _bob.LifeTotal.Should().Be(15, "Bob accepted → 5 damage");
            _alice.Zones.Hand.GetCards().Should().BeEmpty(
                "someone accepted, so no draw");
        }
        finally
        {
            AgentRegistry.Remove(_alice);
            AgentRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void Browbeat_NoAllPlayers_DefaultsToAllDecline_TargetDrawsThree()
    {
        // No AllPlayers snapshot → no player can accept → "if no one does"
        // fires and the target draws three (the all-decline default).
        FillLibrary(_bob, 5);

        var def = BrowbeatFactory.BuildSpellDefinition(o => o);
        var chosen = ChosenTargeting(_bob); // no AllPlayers

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Hand.GetCards().Should().HaveCount(3);
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
        _alice.LifeTotal.Should().Be(20);
        _bob.LifeTotal.Should().Be(20);
    }
}
