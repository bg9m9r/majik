using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ZofConsumptionFactory"/> and
/// <see cref="ZofBloodbogFactory"/> — the front + back faces of the Zendikar
/// Rising modal double-faced card Zof Consumption // Zof Bloodbog.
///
/// Front face (Zof Consumption, {4}{B}{B}):
///   Sorcery. "Each opponent loses 4 life and you gain 4 life."
///
/// Back face (Zof Bloodbog):
///   Land. "This land enters tapped." "{T}: Add {B}."
///
/// Covers:
/// - Identity for both faces (name, cost, type, colour, owner).
/// - MDFC face-tracker (front starts on front; back pre-flipped).
/// - Front: resolution — each supplied opponent loses 4 life; controller
///   gains 4 life (drain body, CR 119.3 life loss — NOT damage).
/// - Front: the controller is never drained (only opponents).
/// - Front: with no opponents supplied, the controller still gains 4 life.
/// - Back: Land type, non-basic, {T}: Add {B} mana ability.
/// </summary>
[Trait("Color", "B")]
public class ZofConsumptionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private static void RunEffects(System.Collections.Generic.IReadOnlyList<IEffect> effects)
    {
        foreach (var e in effects)
        {
            e.Execute();
        }
    }

    // =========================================================================
    // Front face — identity + MDFC
    // =========================================================================

    [Fact]
    public void ZofConsumption_Identity_4BB_Sorcery()
    {
        var card = ZofConsumptionFactory.Create(_alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Zof Consumption");
        card.ManaCost.Should().Be("{4}{B}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.HasType(CardType.Land).Should().BeFalse();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ZofConsumption_IsBlack()
    {
        var card = ZofConsumptionFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(ManaColor.Black, "the {B}{B} pips make it black");
        colors.Should().NotContain(ManaColor.Red);
        colors.Should().NotContain(ManaColor.Green);
        colors.Should().NotContain(ManaColor.White);
        colors.Should().NotContain(ManaColor.Blue);
    }

    [Fact]
    public void ZofConsumption_CarriesMdfcState_FrontFace()
    {
        var card = ZofConsumptionFactory.Create(_alice);

        card.MdfcState.Should().NotBeNull("Zof Consumption is the front face of an MDFC");
        card.MdfcState!.FrontFaceName.Should().Be("Zof Consumption");
        card.MdfcState!.BackFaceName.Should().Be("Zof Bloodbog");
        card.MdfcState!.IsBackFace.Should().BeFalse("front-face card starts on the front face");
        card.MdfcState!.ActiveFaceName.Should().Be("Zof Consumption");
    }

    // =========================================================================
    // Front face — drain resolution
    // =========================================================================

    [Fact]
    public void Resolve_EachOpponentLoses4_ControllerGains4()
    {
        var effects = ZofConsumptionFactory.BuildResolveEffect(
            _alice, new[] { _bob, _carol });

        RunEffects(effects);

        _bob.LifeTotal.Should().Be(16, "each opponent loses 4 life (CR 119.3)");
        _carol.LifeTotal.Should().Be(16, "each opponent loses 4 life (CR 119.3)");
        _alice.LifeTotal.Should().Be(24, "you gain 4 life (CR 119.3)");
    }

    [Fact]
    public void Resolve_ControllerNeverDrained_EvenIfInOpponentList()
    {
        // Defensive: the controller must never lose life to its own drain,
        // even if a buggy resolver hands its own player back in the list.
        var effects = ZofConsumptionFactory.BuildResolveEffect(
            _alice, new[] { _alice, _bob });

        RunEffects(effects);

        _bob.LifeTotal.Should().Be(16, "the opponent loses 4 life");
        _alice.LifeTotal.Should().Be(24,
            "the controller only gains 4 life — never drains itself (CR 119.3)");
    }

    [Fact]
    public void Resolve_NoOpponents_StillGains4Life()
    {
        var effects = ZofConsumptionFactory.BuildResolveEffect(
            _alice, System.Array.Empty<Player>());

        RunEffects(effects);

        _alice.LifeTotal.Should().Be(24,
            "the 'you gain 4 life' half always fires even with no opponents");
    }

    // =========================================================================
    // Back face — identity + dispatch
    // =========================================================================

    [Fact]
    public void ZofBloodbog_Identity_Land()
    {
        var land = ZofBloodbogFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.Name.Should().Be("Zof Bloodbog");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Zof Bloodbog is non-basic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ZofBloodbog_CarriesMdfcState_PreFlippedToBackFace()
    {
        var land = ZofBloodbogFactory.Create(_alice);

        land.MdfcState.Should().NotBeNull("Zof Bloodbog is the back face of an MDFC");
        land.MdfcState!.FrontFaceName.Should().Be("Zof Consumption");
        land.MdfcState!.BackFaceName.Should().Be("Zof Bloodbog");
        land.MdfcState!.IsBackFace.Should().BeTrue(
            "the back-face card is constructed pre-flipped to the back face");
        land.MdfcState!.ActiveFaceName.Should().Be("Zof Bloodbog");
    }

    [Fact]
    public void ZofBloodbog_HasSingleManaAbility_AddingBlack()
    {
        var land = ZofBloodbogFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one {T}: Add {B} ability");
        manaAbilities[0].ManaGenerated.Black.Should().BeGreaterThan(0, "produces black mana");
        manaAbilities[0].ManaGenerated.Red.Should().Be(0);
        manaAbilities[0].ManaGenerated.Blue.Should().Be(0);
        manaAbilities[0].ManaGenerated.Green.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }
}
