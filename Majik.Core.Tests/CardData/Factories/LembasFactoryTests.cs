using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="LembasFactory"/> — Artifact — Food {2} (The Lord of the
/// Rings: Tales of Middle-earth). Oracle:
///   "When this artifact enters, scry 1, then draw a card.
///    {2}, {T}, Sacrifice this artifact: You gain 3 life.
///    When this artifact is put into a graveyard from the battlefield, its
///    owner shuffles it into their library."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity: Artifact + Food subtype, {2}, colourless.
///   - Ability shape: two TriggeredAbilities (ETB scry+draw, LTB shuffle) plus
///     the JSON {2},{T},Sac: gain 3 life activated ability.
///   - ETB resolve: scry 1 (reorder top card) THEN draw 1 (CR 701.20 / 121.1).
///   - JSON sac ability: {2},{T},Sacrifice costs + gains 3 life (CR 119.3).
///   - LTB resolve: card moves graveyard → owner's library (CR 603.6c).
///
/// Colourless card → shard under Color = C.
/// </summary>
[Trait("Color", "C")]
public class LembasFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Lembas_IsFoodArtifact_AtTwo_Colourless()
    {
        var c = LembasFactory.Create(_alice);

        c.Name.Should().Be("Lembas");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Food).Should().BeTrue("Lembas is a Food artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Lembas_HasTwoTriggers_AndOneSacrificeActivatedAbility()
    {
        var c = LembasFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB scry+draw trigger and LTB shuffle trigger");
        c.Abilities.OfType<ManaAbility>().Should().BeEmpty();

        // The {2},{T},Sacrifice: You gain 3 life activated ability (from JSON).
        c.Abilities.OfType<ActivatedAbility>().Should().ContainSingle(a =>
            a.Costs.OfType<AdditionalCost>()
                .Any(ac => ac.CostType == AdditionalCostType.Sacrifice));
    }

    [Fact]
    public void Triggers_HaveNoTargetRequests()
    {
        var c = LembasFactory.Create(_alice);

        foreach (var trig in c.Abilities.OfType<TriggeredAbility>())
        {
            trig.TargetRequests.Should().BeEmpty();
        }
    }

    // ── ETB: scry 1, then draw a card ────────────────────────────────────────

    [Fact]
    public void Etb_Scry1ThenDraw_KeepOnTop_DrawsTheKeptCard()
    {
        // Two cards in the library; cardA on top.
        var cardA = new Creature("CardA", "{U}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(cardA); // top
        _alice.Zones.Library.AddCard(cardB); // second

        // Scry 1: keep cardA on top (do not bottom it). The subsequent draw
        // then takes cardA.
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: System.Array.Empty<ICard>(),
            TopOrder: new[] { cardA }));
        AgentRegistry.Set(_alice, agent);

        var lembas = LembasFactory.Create(_alice);
        var etb = lembas.Abilities.OfType<TriggeredAbility>().Single(IsEtb);

        var startHand = _alice.Zones.Hand.GetCards().Count();
        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startHand + 1,
            "scry 1 then DRAW a card (CR 701.20 then CR 121.1)");
        _alice.Zones.Hand.GetCards().Should().Contain(cardA,
            "the kept-on-top card is the one drawn");
    }

    [Fact]
    public void Etb_Scry1_BottomTopCard_DrawsTheOtherCard()
    {
        var cardA = new Creature("CardA", "{U}", 1, 1);
        var cardB = new Creature("CardB", "{G}", 1, 1);
        _alice.Zones.Library.AddCard(cardA); // top
        _alice.Zones.Library.AddCard(cardB); // second

        // Scry 1: send cardA (the only peeked card) to the bottom; the draw
        // then takes cardB (now on top).
        var agent = new ScriptedAgent();
        agent.QueueScryDecision(new ScryAction.ScryDecision(
            ToBottom: new[] { cardA },
            TopOrder: System.Array.Empty<ICard>()));
        AgentRegistry.Set(_alice, agent);

        var lembas = LembasFactory.Create(_alice);
        var etb = lembas.Abilities.OfType<TriggeredAbility>().Single(IsEtb);

        foreach (var effect in etb.Effects) effect.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(cardB,
            "cardA was scryed to the bottom, so the draw takes cardB");
    }

    // ── {2},{T},Sacrifice: You gain 3 life (from JSON) ───────────────────────

    [Fact]
    public void SacrificeAbility_HasCorrectCosts_GainsThreeLife()
    {
        var c = LembasFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var sacAbility = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<AdditionalCost>()
                .Any(ac => ac.CostType == AdditionalCostType.Sacrifice));

        sacAbility.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the activation cost includes {2}");
        sacAbility.Costs.OfType<AdditionalCost>()
            .Should().Contain(ac => ac.CostType == AdditionalCostType.Tap,
                "{T} is part of the cost");

        var before = _alice.LifeTotal;
        foreach (var effect in sacAbility.Effects) effect.Execute();

        (_alice.LifeTotal - before).Should().Be(3,
            "the printed effect gains 3 life (CR 119.3)");
    }

    // ── LTB: put into graveyard from battlefield → shuffle into library ──────

    [Fact]
    public void Ltb_ShufflesIntoOwnersLibrary()
    {
        var lembas = LembasFactory.Create(_alice);

        // Simulate Lembas having been put into the graveyard from the
        // battlefield: it now lives in the owner's graveyard when the LTB
        // trigger resolves.
        _alice.Zones.Graveyard.AddCard(lembas);
        lembas.SetZone(ZoneType.Graveyard);

        var startLib = _alice.Zones.Library.GetCards().Count();

        var ltb = lembas.Abilities.OfType<TriggeredAbility>().Single(t => !IsEtb(t));
        foreach (var effect in ltb.Effects) effect.Execute();

        _alice.Zones.Library.GetCards().Should().Contain(lembas,
            "the LTB trigger shuffles Lembas into its owner's library (CR 603.6c)");
        _alice.Zones.Library.GetCards().Count().Should().Be(startLib + 1);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(lembas,
            "Lembas left the graveyard");
        lembas.Zone.Should().Be(ZoneType.Library);
    }

    // The ETB trigger is battlefield-only; the LTB trigger is active in
    // Battlefield + Graveyard. Disambiguate by active zones.
    private static bool IsEtb(TriggeredAbility t) =>
        !t.ActiveZones.Contains(ZoneType.Graveyard);
}
