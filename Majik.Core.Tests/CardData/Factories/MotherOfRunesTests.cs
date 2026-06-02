using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MotherOfRunesFactory"/> (Urza's Saga, {W}).
///
/// Creature — Human Cleric 1/1. Oracle text (verified against Scryfall
/// 2026-06-01):
///   "{T}: Target creature you control gains protection from the color of
///    your choice until end of turn."
///
/// Covers:
/// - Identity (name, type, Human + Cleric subtypes, {W} mana cost, 1/1,
///   owner/controller) materialised from the embedded JSON definition.
/// - NamedCardFactory dispatch.
/// - Activated-ability shape: {T} (tap) cost; 1..1 "target creature you
///   control" target request.
/// - Resolution grants the chosen target a ProtectionAbility with the
///   picked colour until end of turn, registered on the target's
///   ContinuousEffectsService (CR 514.2 EOT cleanup path).
/// - Mother of Runes CAN target itself (no "another" gate, unlike Giver of
///   Runes).
/// </summary>
public class MotherOfRunesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MotherOfRunes_Identity()
    {
        var c = MotherOfRunesFactory.Create(_alice);

        c.Name.Should().Be("Mother of Runes");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue("Mother of Runes is a Human");
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue("Mother of Runes is a Cleric");
        c.GetPower().Should().Be(1);
        c.GetToughness().Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MotherOfRunes_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mother of Runes", _alice);

        c.Should().BeOfType<Creature>("Mother of Runes is a Creature");
        c.Name.Should().Be("Mother of Runes");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T} protection-grant ability is wired");
    }

    // -----------------------------------------------------------------------
    // Activated-ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void MotherOfRunes_Ability_HasTapCost_AndCreatureTargetRequest()
    {
        var c = MotherOfRunesFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle()
            .Which.CostType.Should().Be(AdditionalCostType.Tap,
                "the printed activation cost is {T}");

        ability.TargetRequests.Should().ContainSingle();
        var request = ability.TargetRequests.Single();
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
        request.Description.Should().Contain("creature you control",
            "the ability targets a creature you control (CR 602.1)");
    }

    // -----------------------------------------------------------------------
    // Resolution — protection grant
    // -----------------------------------------------------------------------

    [Fact]
    public void MotherOfRunes_Resolve_GrantsProtectionFromChosenColor_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var mother = MotherOfRunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mother);
        mother.SetZone(ZoneType.Battlefield);

        // Choose protection from red.
        var result = MotherOfRunesFactory.Resolve(
            _alice, bear, MotherOfRunesFactory.QualityFromColor(ManaColor.Red));

        result.Target.Should().BeSameAs(bear);
        result.Quality.Should().Be("red");

        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeTrue(
            "the bear gained protection from red");
        Protection.HasProtectionFromColor(bear, ManaColor.Blue).Should().BeFalse(
            "only the chosen colour was granted");

        // CR 514.2 — the grant expires at end of turn.
        svc.ExpireEndOfTurn();
        Protection.HasProtectionFromColor(bear, ManaColor.Red).Should().BeFalse(
            "the protection grant lifts during the cleanup step");
    }

    // -----------------------------------------------------------------------
    // Can target itself — no "another" gate (CR 602.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void MotherOfRunes_CanTargetItself()
    {
        var svc = new ContinuousEffectsService();
        var mother = MotherOfRunesFactory.Create(_alice);
        mother.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(mother);
        mother.SetZone(ZoneType.Battlefield);

        // Unlike Giver of Runes, "target creature you control" includes the
        // source itself — protecting Mother of Runes is fully legal.
        var result = MotherOfRunesFactory.Resolve(
            _alice, mother, MotherOfRunesFactory.QualityFromColor(ManaColor.White));

        result.Target.Should().BeSameAs(mother,
            "Mother of Runes can target itself");
        result.Quality.Should().Be("white");
        Protection.HasProtectionFromColor(mother, ManaColor.White).Should().BeTrue(
            "Mother of Runes gained protection from white");
    }

    // -----------------------------------------------------------------------
    // Candidate gatherer includes the source itself
    // -----------------------------------------------------------------------

    [Fact]
    public void MotherOfRunes_CandidateGatherer_IncludesItself()
    {
        var mother = MotherOfRunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(mother);
        mother.SetZone(ZoneType.Battlefield);

        var ability = mother.Abilities.OfType<ActivatedAbility>().Single();
        var candidates = ability.TargetRequests.Single()
            .CandidateGatherer!(null!);

        candidates.Should().Contain(mother,
            "'target creature you control' includes Mother of Runes itself");
    }
}
