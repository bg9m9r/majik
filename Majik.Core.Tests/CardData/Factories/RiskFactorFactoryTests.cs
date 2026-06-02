using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RiskFactorFactory"/> (Guilds of Ravnica, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target opponent may have Risk Factor deal 4 damage to them. If that
///    player doesn't, you draw three cards.
///    Jump-start (You may cast this card from your graveyard by discarding a
///    card in addition to paying its other costs. Then exile this card.)"
///
/// Covers:
///   - Identity ({2}{R} Instant, red, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - SpellDefinition shape: one "target opponent" request (1..1).
///   - Opponent declines → caster draws three cards (CR 121.1), no damage.
///   - Opponent accepts → opponent takes 4 damage (CR 119) AND the caster
///     does NOT draw ("if that player doesn't").
///   - No agent for the opponent → defaults to decline → caster draws three.
///   - Jump-start cost pair: graveyard-cast flashback at printed cost +
///     discard-a-card additional cost; flashback exiles after resolution.
/// </summary>
[Trait("Color", "R")]
public class RiskFactorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static ChosenSpellParams ChosenTargeting(Player target) =>
        new(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: null);

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
    public void RiskFactor_Identity_InstantAtTwoRed()
    {
        var card = RiskFactorFactory.Create(_alice);

        card.Name.Should().Be("Risk Factor");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.ManaCostValue.TotalValue.Should().Be(3, because: "{2}{R} = mana value 3");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RiskFactor_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Risk Factor", _alice);

        c.Should().BeOfType<Instant>();
        c.Name.Should().Be("Risk Factor");
    }

    [Fact]
    public void RiskFactor_SpellDefinition_HasSingleTargetOpponentRequest()
    {
        var def = RiskFactorFactory.BuildSpellDefinition(_alice, o => o);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target opponent");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolution: opponent declines → caster draws three ───────────────────

    [Fact]
    public void RiskFactor_OpponentDeclines_CasterDrawsThreeCards()
    {
        FillLibrary(_alice, 5);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(false); // Bob declines the 4 damage.
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var def = RiskFactorFactory.BuildSpellDefinition(_alice, o => o);
            var chosen = ChosenTargeting(_bob);

            foreach (var e in def.EffectFactory(chosen)) e.Execute();

            _alice.Zones.Hand.GetCards().Should().HaveCount(3,
                "the opponent declined the 4 damage, so the caster draws three (CR 121.1)");
            _alice.Zones.Library.GetCards().Should().HaveCount(2);
            _alice.LifeTotal.Should().Be(20);
            _bob.LifeTotal.Should().Be(20, "Bob declined the damage");
        }
        finally
        {
            AgentRegistry.Remove(_bob);
        }
    }

    // ── Resolution: opponent accepts → 4 damage, no draw ─────────────────────

    [Fact]
    public void RiskFactor_OpponentAccepts_TakesFourDamage_AndCasterDoesNotDraw()
    {
        FillLibrary(_alice, 5);
        var bobAgent = new ScriptedAgent();
        bobAgent.QueueYesNo(true); // Bob accepts the damage to deny the draw.
        AgentRegistry.Set(_bob, bobAgent);

        try
        {
            var def = RiskFactorFactory.BuildSpellDefinition(_alice, o => o);
            var chosen = ChosenTargeting(_bob);

            foreach (var e in def.EffectFactory(chosen)) e.Execute();

            _bob.LifeTotal.Should().Be(16,
                "Bob accepted, so Risk Factor deals 4 damage to him (CR 119)");
            _alice.Zones.Hand.GetCards().Should().BeEmpty(
                "the opponent accepted the damage, so 'if that player doesn't' fails — no draw");
            _alice.Zones.Library.GetCards().Should().HaveCount(5);
        }
        finally
        {
            AgentRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void RiskFactor_NoOpponentAgent_DefaultsToDecline_CasterDrawsThree()
    {
        // No agent for the opponent → the "may" choice defaults to decline →
        // the caster draws three (CR 121.1).
        FillLibrary(_alice, 5);

        var def = RiskFactorFactory.BuildSpellDefinition(_alice, o => o);
        var chosen = ChosenTargeting(_bob);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.GetCards().Should().HaveCount(3);
        _alice.Zones.Library.GetCards().Should().HaveCount(2);
        _bob.LifeTotal.Should().Be(20);
    }

    // ── Jump-start (CR 702.133) ──────────────────────────────────────────────

    [Fact]
    public void RiskFactor_JumpStart_GraveyardCastAtPrintedCost_PlusDiscard()
    {
        var (graveyardCast, discard) = RiskFactorFactory.BuildJumpStartCost();

        // CR 702.133a — Jump-start pays the card's PRINTED mana cost.
        graveyardCast.AlternativeManaCost.TotalValue.Should().Be(3, "{2}{R} = mana value 3");
        discard.Description.Should().Be("discard a card");
    }

    [Fact]
    public void RiskFactor_JumpStart_OnlyLegalFromGraveyard_OwnedByCaster()
    {
        var (graveyardCast, _) = RiskFactorFactory.BuildJumpStartCost();
        var card = RiskFactorFactory.Create(_alice);

        // Hand / battlefield → not legal.
        card.SetZone(ZoneType.Hand);
        graveyardCast.CanCastFor(card, _alice).Should().BeFalse(
            "Jump-start is only castable from the graveyard (CR 702.133a)");

        // Graveyard + owner → legal.
        card.SetZone(ZoneType.Graveyard);
        graveyardCast.CanCastFor(card, _alice).Should().BeTrue();

        // Graveyard but a non-owner → not legal.
        graveyardCast.CanCastFor(card, _bob).Should().BeFalse(
            "only the card's owner may Jump-start it");
    }

    [Fact]
    public void RiskFactor_JumpStart_ExilesCardAfterResolution()
    {
        var (graveyardCast, _) = RiskFactorFactory.BuildJumpStartCost();
        var card = RiskFactorFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        // CR 702.133b — after resolution, exile the card.
        graveyardCast.OnResolved(card, _alice);

        _alice.Zones.Exile.GetCards().Should().Contain(card);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(card);
        card.Zone.Should().Be(ZoneType.Exile);
    }
}
