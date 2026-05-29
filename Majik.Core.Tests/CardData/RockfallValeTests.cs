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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="RockfallValeFactory"/> — Innistrad: Crimson Vow
/// R/G slow land.
///
/// Oracle:
///   "This land enters tapped unless you control two or more other lands.
///    {T}: Add {R} or {G}."
///
/// Covers card identity, the two mana abilities ({R} + {G}), and that no
/// triggered or non-mana activated abilities ship (the conditional
/// ETB-tapped is a replacement effect handled by the binder layer in
/// production — CR 614.1c). A binder-level test locks in the load-bearing
/// difference vs the Kaladesh fastlands: the "two or MORE other lands"
/// direction (untapped iff the controller already has &gt;= 2 other lands).
/// </summary>
public class RockfallValeTests
{
    private const string OracleText =
        "Rockfall Vale enters tapped unless you control two or more other lands.";

    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RockfallVale_IsLand()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void RockfallVale_NameIsCorrect()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Name.Should().Be("Rockfall Vale");
    }

    [Fact]
    public void RockfallVale_OwnerAndControllerAreSet()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RockfallVale_IsNotLegendary()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void RockfallVale_HasTwoManaAbilities()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void RockfallVale_HasRedManaAbility()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void RockfallVale_HasGreenManaAbility()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void RockfallVale_HasNoTriggeredAbilities()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "ETB-tapped-unless-two-or-more-other-lands is a replacement effect, not a trigger");
    }

    [Fact]
    public void RockfallVale_HasNoActivatedAbilities()
    {
        var land = RockfallValeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void RockfallVale_DispatchedThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Rockfall Vale", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Rockfall Vale");
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped — "two or more other lands" (CR 614.1c).
    // Production binds this via ConditionalEntersTappedBinder; these tests
    // exercise the predicate directly so the slow-land direction is locked.
    // -----------------------------------------------------------------------

    [Fact]
    public void RockfallVale_BinderRegistersReplacement_ForTwoOrMoreClause()
    {
        var bus = new ReplacementBus();
        var land = RockfallValeFactory.Create(_alice);
        var entity = new CardEntity
        {
            Name = "Rockfall Vale",
            OracleText = OracleText,
            TypeLine = "Land",
        };

        ConditionalEntersTappedBinder.Bind(land, entity, bus).Should().BeTrue();
    }

    [Fact]
    public void RockfallVale_EntersUntapped_WhenControllerHasTwoOtherLands()
    {
        var (zones, alice, land) = SetupBound();

        // Seed two other lands already on the battlefield.
        for (int i = 0; i < 2; i++)
        {
            var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
            mountain.SetOwner(alice);
            alice.Zones.Battlefield.AddCard(mountain);
            mountain.SetZone(ZoneType.Battlefield);
        }

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeFalse(
            "Rockfall Vale enters untapped when the controller already has two or more other lands");
    }

    [Fact]
    public void RockfallVale_EntersTapped_WhenControllerHasOnlyOneOtherLand()
    {
        var (zones, alice, land) = SetupBound();

        var mountain = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        mountain.SetOwner(alice);
        alice.Zones.Battlefield.AddCard(mountain);
        mountain.SetZone(ZoneType.Battlefield);

        zones.MoveCardTo(land, ZoneType.Battlefield, controller: alice);

        ((Permanent)land).IsTapped.Should().BeTrue(
            "Rockfall Vale enters tapped when the controller has fewer than two other lands");
    }

    private static (ZoneService zones, Player alice, Land land) SetupBound()
    {
        var eventBus = new Majik.Core.Events.EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(eventBus, rep);
        var alice = new Player("Alice", 20);
        var land = RockfallValeFactory.Create(alice);
        alice.Zones.Hand.AddCard(land);
        land.SetZone(ZoneType.Hand);
        var entity = new CardEntity { Name = "Rockfall Vale", OracleText = OracleText, TypeLine = "Land" };
        ConditionalEntersTappedBinder.Bind(land, entity, rep).Should().BeTrue();
        return (zones, alice, land);
    }
}
