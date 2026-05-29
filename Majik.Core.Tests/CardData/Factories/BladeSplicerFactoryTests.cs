using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="BladeSplicerFactory"/> (New Phyrexia / many
/// reprints, {2}{W}). Creature — Phyrexian Human Artificer 1/1. Oracle text
/// (verified against Scryfall):
///   "When this creature enters, create a 3/3 colorless Phyrexian Golem
///    artifact creature token.
///    Golems you control have first strike."
///
/// Covers:
/// - Identity (Phyrexian Human Artificer, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - ETB <see cref="TriggeredAbility"/> shape.
/// - <see cref="BladeSplicerFactory.CreatePhyrexianGolemToken"/> builds a
///   3/3 colourless Phyrexian Golem artifact creature token.
/// - ETB-effect execution creates one Golem token on the controller's
///   battlefield.
/// - The Golem lord static grants first strike to Golems the controller
///   controls (the minted token included).
/// </summary>
public class BladeSplicerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void BladeSplicer_Identity()
    {
        var card = BladeSplicerFactory.Create(_alice);

        card.Name.Should().Be("Blade Splicer");
        card.ManaCost.Should().Be("{2}{W}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
        card.BasePower.Should().Be(1);
        card.BaseToughness.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BladeSplicer_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Blade Splicer", _alice);

        card.Should().BeOfType<Creature>("Blade Splicer is a Creature instance");
        card.Name.Should().Be("Blade Splicer");
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Artificer).Should().BeTrue();
    }

    [Fact]
    public void BladeSplicer_HasOneEtbTrigger()
    {
        var card = BladeSplicerFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB Golem-token trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Phyrexian Golem token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatePhyrexianGolemToken_Builds_3_3_Colourless_Artifact_Golem()
    {
        var golem = BladeSplicerFactory.CreatePhyrexianGolemToken(_alice);

        golem.Name.Should().Be("Phyrexian Golem");
        golem.Power.Should().Be(3);
        golem.Toughness.Should().Be(3);
        golem.IsToken.Should().BeTrue();
        golem.HasType(CardType.Creature).Should().BeTrue();
        golem.HasType(CardType.Artifact).Should().BeTrue(
            "the printed token is a 3/3 colorless Phyrexian Golem artifact creature token");
        golem.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        golem.HasSubtype(CardSubtype.Golem).Should().BeTrue();
        golem.Owner.Should().BeSameAs(_alice);
        golem.Controller.Should().BeSameAs(_alice);
        golem.Zone.Should().Be(ZoneType.Battlefield,
            "the Golem token enters the battlefield directly (CR 111.6)");
    }

    // -----------------------------------------------------------------------
    // ETB trigger effect — execute and observe the Golem landing.
    // -----------------------------------------------------------------------

    [Fact]
    public void BladeSplicer_EtbEffect_CreatesPhyrexianGolemUnderController()
    {
        var splicer = BladeSplicerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(splicer);
        splicer.SetZone(ZoneType.Battlefield);

        var trigger = splicer.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var golemsOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Phyrexian Golem")
            .ToList();

        golemsOnBoard.Should().HaveCount(1,
            "the ETB effect creates one Phyrexian Golem token");
        golemsOnBoard[0].Power.Should().Be(3);
        golemsOnBoard[0].Toughness.Should().Be(3);
        golemsOnBoard[0].IsToken.Should().BeTrue();
        golemsOnBoard[0].HasType(CardType.Artifact).Should().BeTrue();
        golemsOnBoard[0].HasSubtype(CardSubtype.Golem).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Golem lord static — "Golems you control have first strike."
    // -----------------------------------------------------------------------

    [Fact]
    public void BladeSplicer_GrantsFirstStrike_ToControlledGolems()
    {
        var continuous = new ContinuousEffectsService();
        var splicer = BladeSplicerFactory.Create(_alice, zones: null, triggers: null, continuousEffects: continuous);
        _alice.Zones.Battlefield.AddCard(splicer);
        splicer.SetZone(ZoneType.Battlefield);

        // Mint the Golem the way the ETB does.
        var golem = BladeSplicerFactory.CreatePhyrexianGolemToken(_alice);

        var chars = continuous.Compute(golem);

        chars.Keywords.Should().Contain("First strike",
            "CR 613.1f — Blade Splicer's static grants first strike to Golems you control");
    }
}
