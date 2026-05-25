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
/// Tests for <see cref="PinnacleEmissaryFactory"/> (Edge of Eternities,
/// {1}{U}{R}). Artifact Creature — Robot 3/3:
///   "Whenever you cast an artifact spell, create a 1/1 colorless Drone
///    artifact creature token with flying and \"This token can block only
///    creatures with flying.\"
///    Warp {U/R} (You may cast this card from your hand for its warp cost.
///    Exile this creature at the beginning of the next end step, then you
///    may cast it from exile on a later turn.)"
///
/// Covers:
/// - Identity (Robot Artifact Creature, mana cost, power/toughness,
///   owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Cast-trigger shape: <see cref="TriggeredAbility"/> over
///   <see cref="Domain.DomainEvents.SpellCastEvent"/>.
/// - Warp keyword marker is attached as <see cref="KeywordAbility"/> for
///   card-text inspection (mechanic deferred — see factory xmldoc).
/// - <see cref="PinnacleEmissaryFactory.CreateDroneToken"/> builds a 1/1
///   colourless Drone artifact creature with Flying.
/// </summary>
public class PinnacleEmissaryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PinnacleEmissary_Identity()
    {
        var card = PinnacleEmissaryFactory.Create(_alice);

        card.Name.Should().Be("Pinnacle Emissary");
        card.ManaCost.Should().Be("{1}{U}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue(
            "Pinnacle Emissary is an Artifact Creature (CR 301.1 / 302.1)");
        card.Power.Should().Be(3);
        card.Toughness.Should().Be(3);
        card.Subtypes.Should().Contain(CardSubtype.Robot);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PinnacleEmissary()
    {
        var card = NamedCardFactory.Create("Pinnacle Emissary", _alice);

        card.Should().BeOfType<Creature>("Pinnacle Emissary is a Creature instance");
        card.Name.Should().Be("Pinnacle Emissary");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeTrue();
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "the cast-artifact-spell trigger is attached");
        card.Abilities.OfType<KeywordAbility>().Where(k => k.Keyword == "Warp")
            .Should().HaveCount(1, "Warp keyword marker is attached");
    }

    // -----------------------------------------------------------------------
    // Drone token shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CreateDroneToken_Builds_1_1_Colourless_Artifact_Drone_WithFlying()
    {
        var drone = PinnacleEmissaryFactory.CreateDroneToken(_alice);

        drone.Name.Should().Be("Drone");
        drone.Power.Should().Be(1);
        drone.Toughness.Should().Be(1);
        drone.IsToken.Should().BeTrue();
        drone.HasType(CardType.Creature).Should().BeTrue();
        drone.HasType(CardType.Artifact).Should().BeTrue(
            "Drones are artifact creature tokens");
        drone.Subtypes.Should().Contain(CardSubtype.Drone);
        drone.Abilities.OfType<KeywordAbility>().Where(k => k.Keyword == "Flying")
            .Should().HaveCount(1, "Flying keyword marker attached on the token");
        drone.Controller.Should().BeSameAs(_alice);
        drone.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Cast-trigger effect — create a Drone token on artifact-spell cast
    // -----------------------------------------------------------------------

    [Fact]
    public void PinnacleEmissary_CastTrigger_EffectCreatesDroneTokenForAliceController()
    {
        var pinnacle = PinnacleEmissaryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(pinnacle);
        pinnacle.SetZone(ZoneType.Battlefield);

        var trigger = pinnacle.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var dronesOnBoard = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.Name == "Drone")
            .ToList();

        dronesOnBoard.Should().HaveCount(1,
            "the cast-trigger effect creates one Drone token");
        dronesOnBoard[0].Power.Should().Be(1);
        dronesOnBoard[0].Toughness.Should().Be(1);
        dronesOnBoard[0].IsToken.Should().BeTrue();
        dronesOnBoard[0].HasType(CardType.Artifact).Should().BeTrue();
        dronesOnBoard[0].Subtypes.Should().Contain(CardSubtype.Drone);
    }
}
