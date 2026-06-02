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
/// Tests for <see cref="CodexShredderFactory"/> (Return to Ravnica, {1}).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Target player mills a card. (They put the top card of their
///    library into their graveyard.)
///    {5}, {T}, Sacrifice this artifact: Return target card from your
///    graveyard to your hand."
///
/// Coverage:
///   - Identity (Artifact, {1}, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch for "Codex Shredder".
///   - Two activated abilities with the printed cost shapes:
///       * {T}: mill ability — tap only, no mana, no sacrifice.
///       * {5}, {T}, Sacrifice this artifact: graveyard return — mana + tap +
///         sacrifice.
///   - Mill activation: target player mills exactly ONE card (CR 701.13),
///     honouring an agent-set target and falling back to the controller.
///   - Graveyard-return activation: sacrifices this artifact + returns a card
///     of any type from graveyard to hand.
/// </summary>
public class CodexShredderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void CodexShredder_Identity_Artifact_AtCost1()
    {
        var card = CodexShredderFactory.Create(_alice);

        card.Name.Should().Be("Codex Shredder");
        card.ManaCost.Should().Be("{1}");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CodexShredder()
    {
        var card = NamedCardFactory.Create("Codex Shredder", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Codex Shredder");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
    }

    [Fact]
    public void CodexShredder_HasTwoActivatedAbilities_WithPrintedCostShapes()
    {
        var card = CodexShredderFactory.Create(_alice);

        var abilities = card.Abilities.OfType<ActivatedAbility>().ToList();
        abilities.Should().HaveCount(2,
            "the printed {T} mill activation + the printed {5},{T},Sac graveyard-return activation");

        // Ability #1: {T} only — no mana, no sacrifice.
        abilities[0].Costs.OfType<AdditionalCost>()
            .Should().ContainSingle(c => c.CostType == AdditionalCostType.Tap);
        abilities[0].Costs.OfType<AdditionalCost>()
            .Should().NotContain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the mill ability does not sacrifice the artifact");
        abilities[0].Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "the mill ability has no mana pip");
        abilities[0].TargetRequests.Should().ContainSingle(
            "the mill ability targets a player");

        // Ability #2: {5}, {T}, Sacrifice this artifact.
        abilities[1].Costs.OfType<ManaCostCost>().Should().HaveCount(1);
        abilities[1].Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap);
        abilities[1].Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice);
        abilities[1].TargetRequests.Should().ContainSingle(
            "the return ability targets a card in your graveyard");
    }

    [Fact]
    public void Mill_Activation_TargetPlayerMillsExactlyOneCard()
    {
        var card = CodexShredderFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Bob's library: two cards; only the TOP one should be milled.
        var top = new Instant("Top", "{R}") { Owner = _bob };
        var second = new Instant("Second", "{R}") { Owner = _bob };
        _bob.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        _bob.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        // Agent-set target: Bob.
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        foreach (var effect in ability.Effects)
            effect.Execute();

        // Exactly one card milled — the top card → Bob's graveyard.
        top.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Graveyard.GetCards().Should().Contain(top);

        // The second card stays in the library (CR 701.13 — mill ONE).
        second.Zone.Should().Be(ZoneType.Library);
        _bob.Zones.Library.GetCards().Should().Contain(second);
    }

    [Fact]
    public void Mill_Activation_FallsBackToControllerWhenNoTargetChosen()
    {
        var card = CodexShredderFactory.Create(_alice);
        SeatOnBattlefield(card);

        var top = new Instant("Top", "{R}") { Owner = _alice };
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        // No SetChosenTargets — deterministic controller fallback.
        foreach (var effect in ability.Effects)
            effect.Execute();

        top.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(top);
    }

    [Fact]
    public void Mill_Activation_EmptyLibrary_IsCleanNoOp()
    {
        var card = CodexShredderFactory.Create(_alice);
        SeatOnBattlefield(card);

        var ability = card.Abilities.OfType<ActivatedAbility>().First();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { _bob } });

        // Bob has an empty library — milling does nothing (CR 701.13).
        var act = () =>
        {
            foreach (var effect in ability.Effects)
                effect.Execute();
        };

        act.Should().NotThrow();
        _bob.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void GraveyardReturn_Activation_SacrificesSelfAndReturnsAnyCard()
    {
        var card = CodexShredderFactory.Create(_alice);
        SeatOnBattlefield(card);

        // Seat a NON-creature card in Alice's graveyard — Codex Shredder
        // returns ANY card type (unlike The Underworld Cookbook's creatures).
        var graveSpell = new Instant("Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(graveSpell);
        graveSpell.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { graveSpell } });

        foreach (var effect in ability.Effects)
            effect.Execute();

        // This artifact was sacrificed (battlefield -> graveyard, CR 701.16).
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);

        // The targeted spell card returned to hand.
        graveSpell.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Hand.GetCards().Should().Contain(graveSpell);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(graveSpell);
    }

    [Fact]
    public void GraveyardReturn_Fallback_DoesNotReturnSacrificedSelf()
    {
        var card = CodexShredderFactory.Create(_alice);
        SeatOnBattlefield(card);

        // A real graveyard card other than Codex Shredder.
        var graveSpell = new Instant("Bolt", "{R}") { Owner = _alice };
        _alice.Zones.Graveyard.AddCard(graveSpell);
        graveSpell.SetZone(ZoneType.Graveyard);

        var ability = card.Abilities.OfType<ActivatedAbility>().Skip(1).First();
        // No target chosen — deterministic fallback. The just-sacrificed
        // Codex Shredder must NOT be the pick.
        foreach (var effect in ability.Effects)
            effect.Execute();

        graveSpell.Zone.Should().Be(ZoneType.Hand);
        card.Zone.Should().Be(ZoneType.Graveyard,
            "the artifact sacrificed itself and must not return itself to hand");
    }

    private void SeatOnBattlefield(Artifact card)
    {
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }
}
