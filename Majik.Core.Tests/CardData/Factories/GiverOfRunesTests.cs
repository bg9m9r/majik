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
/// Unit tests for <see cref="GiverOfRunesFactory"/> (Modern Horizons, {W}).
///
/// Creature — Kor Cleric 1/2. Oracle text (verified against the embedded
/// seed):
///   "{T}: Another target creature you control gains protection from
///    colorless or from the color of your choice until end of turn."
///
/// Covers:
/// - Identity (name, type, Kor + Cleric subtypes, {W} mana cost, 1/2,
///   owner/controller) materialised from the embedded JSON definition.
/// - NamedCardFactory dispatch.
/// - Activated-ability shape: {T} (tap) cost; 1..1 "another target
///   creature you control" target request.
/// - Resolution grants the chosen target a ProtectionAbility with the
///   picked quality until end of turn, registered on the target's
///   ContinuousEffectsService (CR 514.2 EOT cleanup path).
/// - The "another" gate: the ability cannot target Giver of Runes itself
///   (CR 602.1 — "another target creature").
/// </summary>
public class GiverOfRunesTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GiverOfRunes_Identity()
    {
        var c = GiverOfRunesFactory.Create(_alice);

        c.Name.Should().Be("Giver of Runes");
        c.ManaCost.Should().Be("{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Kor).Should().BeTrue("Giver of Runes is a Kor");
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue("Giver of Runes is a Cleric");
        c.GetPower().Should().Be(1);
        c.GetToughness().Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GiverOfRunes_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Giver of Runes", _alice);

        c.Should().BeOfType<Creature>("Giver of Runes is a Creature");
        c.Name.Should().Be("Giver of Runes");
        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T} protection-grant ability is wired");
    }

    // -----------------------------------------------------------------------
    // Activated-ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void GiverOfRunes_Ability_HasTapCost_AndCreatureTargetRequest()
    {
        var c = GiverOfRunesFactory.Create(_alice);

        var ability = c.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>().Should().ContainSingle()
            .Which.CostType.Should().Be(AdditionalCostType.Tap,
                "the printed activation cost is {T}");

        ability.TargetRequests.Should().ContainSingle();
        var request = ability.TargetRequests.Single();
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
        request.Description.Should().Contain("another",
            "the ability targets ANOTHER creature you control (CR 602.1)");
    }

    // -----------------------------------------------------------------------
    // Resolution — protection grant
    // -----------------------------------------------------------------------

    [Fact]
    public void GiverOfRunes_Resolve_GrantsProtectionFromChosenColor_UntilEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var giver = GiverOfRunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(giver);
        giver.SetZone(ZoneType.Battlefield);

        // Choose protection from red.
        var result = GiverOfRunesFactory.Resolve(
            _alice, bear, GiverOfRunesFactory.QualityFromColor(ManaColor.Red));

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

    [Fact]
    public void GiverOfRunes_Resolve_GrantsProtectionFromColorless()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var giver = GiverOfRunesFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(giver);
        giver.SetZone(ZoneType.Battlefield);

        var result = GiverOfRunesFactory.Resolve(
            _alice, bear, GiverOfRunesFactory.ColorlessPicker);

        result.Target.Should().BeSameAs(bear);
        result.Quality.Should().Be(GiverOfRunesFactory.QualityColorless);

        // The "colorless" protection marker rides on the bear.
        bear.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .Should().Contain(GiverOfRunesFactory.QualityColorless,
                "protection from colorless was granted");
    }

    // -----------------------------------------------------------------------
    // "Another" gate — cannot target itself (CR 602.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void GiverOfRunes_CannotTargetItself()
    {
        var svc = new ContinuousEffectsService();
        var giver = GiverOfRunesFactory.Create(_alice);
        giver.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(giver);
        giver.SetZone(ZoneType.Battlefield);

        // CR 602.1 — "another target creature" excludes the source. Resolve
        // against the source itself (passing it as the source gate) is a
        // clean no-op.
        var result = GiverOfRunesFactory.Resolve(
            _alice, giver, GiverOfRunesFactory.QualityFromColor(ManaColor.White),
            source: giver);

        result.Target.Should().BeNull("Giver of Runes cannot target itself");
        giver.Abilities.OfType<ProtectionAbility>().Should().BeEmpty(
            "no protection was granted to the illegal self-target");
    }
}
