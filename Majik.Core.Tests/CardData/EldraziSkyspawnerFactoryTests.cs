using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="EldraziSkyspawnerFactory"/> (Battle for Zendikar,
/// {2}{U}). Creature — Eldrazi Drone 2/2:
///   "Flying
///    When this creature enters, create a 1/1 colorless Eldrazi Scion
///    creature token with \"Sacrifice this creature: Add {C}.\""
///
/// Covers:
/// - Identity (Eldrazi Drone, mana cost, P/T, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Flying keyword marker attached.
/// - ETB <see cref="TriggeredAbility"/> shape.
/// - <see cref="EldraziSkyspawnerFactory.CreateEldraziScionToken"/> builds
///   a 1/1 colourless Eldrazi Scion with a colourless-producing
///   <see cref="ManaAbility"/>.
/// - ETB-effect execution creates one Scion token on the controller's
///   battlefield.
/// </summary>
public class EldraziSkyspawnerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziSkyspawner_Identity()
    {
        var card = EldraziSkyspawnerFactory.Create(_alice);

        card.Name.Should().Be("Eldrazi Skyspawner");
        card.ManaCost.Should().Be("{2}{U}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        card.BasePower.Should().Be(2);
        card.BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EldraziSkyspawner_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Eldrazi Skyspawner", _alice);

        card.Should().BeOfType<Creature>("Eldrazi Skyspawner is a Creature instance");
        card.Name.Should().Be("Eldrazi Skyspawner");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    [Fact]
    public void EldraziSkyspawner_HasFlyingKeywordMarker()
    {
        var card = EldraziSkyspawnerFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Where(k => k.Keyword == "Flying")
            .Should().HaveCount(1,
                "CR 702.9 — Flying is attached as a keyword marker");
    }

    [Fact]
    public void EldraziSkyspawner_HasOneEtbTrigger()
    {
        var card = EldraziSkyspawnerFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the ETB Scion-token trigger is attached");
    }

    // -----------------------------------------------------------------------
    // Eldrazi Scion token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateEldraziScionToken_Builds_1_1_Colourless_Eldrazi_Scion()
    {
        var scion = EldraziSkyspawnerFactory.CreateEldraziScionToken(_alice);

        scion.Name.Should().Be("Eldrazi Scion");
        scion.Power.Should().Be(1);
        scion.Toughness.Should().Be(1);
        scion.IsToken.Should().BeTrue();
        scion.HasType(CardType.Creature).Should().BeTrue();
        scion.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        scion.HasSubtype(CardSubtype.Scion).Should().BeTrue();
        scion.Owner.Should().BeSameAs(_alice);
        scion.Controller.Should().BeSameAs(_alice);
        scion.Zone.Should().Be(ZoneType.Battlefield,
            "the Scion token enters the battlefield directly (CR 111.6)");
    }

    [Fact]
    public void EldraziScionToken_HasManaAbility_ProducingColourless()
    {
        var scion = EldraziSkyspawnerFactory.CreateEldraziScionToken(_alice);

        var manaAbilities = scion.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1,
            "the Scion ships with one mana ability — \"Sacrifice this creature: Add {C}.\"" +
            " (sac cost rider is deferred — see factory xmldoc)");
        // The produced ManaCost is one colourless ({C}). v1 — ManaCost.Parse
        // folds {C} into the Generic bucket (same posture as Eldrazi Spawn /
        // Urza's Saga; see ManaCost.cs comment on the 'C' case).
        manaAbilities[0].ManaGenerated.Generic.Should().Be(1);
        manaAbilities[0].ManaGenerated.TotalValue.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB trigger effect — execute and observe the Scion landing.
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziSkyspawner_EtbEffect_CreatesEldraziScionUnderController()
    {
        var skyspawner = EldraziSkyspawnerFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(skyspawner);
        skyspawner.SetZone(ZoneType.Battlefield);

        var trigger = skyspawner.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var scionsOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Eldrazi Scion")
            .ToList();

        scionsOnBoard.Should().HaveCount(1,
            "the ETB effect creates one Eldrazi Scion token");
        scionsOnBoard[0].Power.Should().Be(1);
        scionsOnBoard[0].Toughness.Should().Be(1);
        scionsOnBoard[0].IsToken.Should().BeTrue();
        scionsOnBoard[0].HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        scionsOnBoard[0].HasSubtype(CardSubtype.Scion).Should().BeTrue();
        scionsOnBoard[0].Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }
}
