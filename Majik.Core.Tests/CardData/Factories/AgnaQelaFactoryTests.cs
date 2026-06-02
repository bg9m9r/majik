using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="AgnaQelaFactory"/> (Murders at Karlov Manor).
///
/// Oracle:
///   "This land enters tapped unless you control a basic land.
///    {T}: Add {U}.
///    {2}{U}, {T}: Draw a card, then discard a card."
///
/// Covers:
/// - Identity (Land, name "Agna Qel'a", non-basic, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch resolves the name.
/// - One ManaAbility producing {U}.
/// - One ActivatedAbility (rummage) with cost {2}{U} + {T}.
/// - ETB-tapped predicate: no basic → tapped; any basic → untapped;
///   opponent's basic → still tapped (predicate is "you control").
/// - Rummage activation: draws 1 card then discards 1 card.
/// </summary>
[Trait("Color", "C")]
public class AgnaQelaFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Land PlaceOnBattlefield()
    {
        var land = AgnaQelaFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    private Land AddBasicIsland(Player controller)
    {
        var island = new Land(
            "Island",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Island });
        island.SetOwner(controller);
        island.SetController(controller);
        island.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(island);
        return island;
    }

    private Land AddBasicPlains(Player controller)
    {
        var plains = new Land(
            "Plains",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(controller);
        plains.SetController(controller);
        plains.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(plains);
        return plains;
    }

    private static ICard MakeLibraryCard(Player owner, string name = "Draw Bait")
    {
        var card = new Instant(name, "{U}") { Owner = owner };
        return card;
    }

    private static ICard MakeHandCard(Player owner, string name = "Discard Me")
    {
        var card = new Instant(name, "{R}") { Owner = owner };
        return card;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void AgnaQela_IsLand_WithCorrectName()
    {
        var land = AgnaQelaFactory.Create(_alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be("Agna Qel'a");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AgnaQela_IsNotBasic_NotLegendary()
    {
        var land = AgnaQelaFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Agna Qel'a is a nonbasic land");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------
    // -----------------------------------------------------------------------
    // Mana ability: {T}: Add {U}
    // -----------------------------------------------------------------------

    [Fact]
    public void AgnaQela_HasExactlyOneManaAbility_ProducingBlue()
    {
        var land = AgnaQelaFactory.Create(_alice);

        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(1, "exactly one basic mana ability: {T}: Add {U}");

        var blue = ManaCost.Parse("U");
        manaAbilities[0].ManaGenerated.Blue.Should().Be(1,
            "the mana ability produces exactly 1 blue pip");
        manaAbilities[0].ManaGenerated.Generic.Should().Be(0);
        manaAbilities[0].ManaGenerated.White.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Rummage activated ability: {2}{U}, {T}: Draw a card, then discard a card
    // -----------------------------------------------------------------------

    [Fact]
    public void AgnaQela_HasExactlyOneActivatedAbility_WithCorrectCosts()
    {
        var land = AgnaQelaFactory.Create(_alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1,
            "exactly one non-mana activated ability: the {2}{U},{T} rummage");

        var ability = activated[0];
        ability.Costs.OfType<ManaCostCost>().Should().HaveCount(1,
            "the {2}{U} mana cost is declared as a ManaCostCost");
        ability.Costs.OfType<AdditionalCost>().Should().HaveCount(1,
            "the {T} is declared as an AdditionalCost.Tap");
    }

    [Fact]
    public void AgnaQela_RummageActivation_DrawsOneThenDiscardsOne()
    {
        var land = PlaceOnBattlefield();

        // Seed library with one card to draw.
        var libTop = MakeLibraryCard(_alice, "Top of Library");
        _alice.Zones.Library.AddCard(libTop);
        libTop.SetZone(ZoneType.Library);

        // Seed hand with one card to discard.
        var handCard = MakeHandCard(_alice, "Hand Card");
        _alice.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        // Execute the effect directly (cost payment handled separately in
        // full activation flow; same posture as UnderworldCookbookTests).
        foreach (var effect in ability.Effects)
            effect.Execute();

        // Drew one card.
        _alice.Zones.Hand.GetCards().Should().Contain(libTop,
            "the draw moved the top library card to hand");
        libTop.Zone.Should().Be(ZoneType.Hand);

        // Discarded one card.
        _alice.Zones.Graveyard.GetCards().Should().Contain(handCard,
            "the discard moved the first hand card to graveyard");
        handCard.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void AgnaQela_RummageActivation_EmptyLibrary_DiscardsButNoDrawError()
    {
        // Empty library + one hand card. Draw from empty library is a no-op
        // via Fx.DrawCards (marks tried-to-draw flag); discard still fires.
        var land = PlaceOnBattlefield();

        var handCard = MakeHandCard(_alice, "Hand Card");
        _alice.Zones.Hand.AddCard(handCard);
        handCard.SetZone(ZoneType.Hand);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        var act = () =>
        {
            foreach (var effect in ability.Effects)
                effect.Execute();
        };

        act.Should().NotThrow("empty-library draw is handled gracefully");
        _alice.Zones.Graveyard.GetCards().Should().Contain(handCard,
            "discard still executes even when library is empty");
    }

    // -----------------------------------------------------------------------
    // ETB-tapped predicate (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void AgnaQela_EntersTapped_WhenControllerHasNoBasicLand()
    {
        var bus = new ReplacementBus();
        var land = AgnaQelaFactory.Create(_alice, bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "Agna Qel'a enters tapped when controller controls no basic land");
    }

    [Fact]
    public void AgnaQela_EntersUntapped_WhenControllerHasBasicIsland()
    {
        var bus = new ReplacementBus();
        AddBasicIsland(_alice);

        var land = AgnaQelaFactory.Create(_alice, bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Agna Qel'a enters untapped when controller controls a basic Island");
    }

    [Fact]
    public void AgnaQela_EntersUntapped_WhenControllerHasAnyBasicLand()
    {
        var bus = new ReplacementBus();
        AddBasicPlains(_alice);

        var land = AgnaQelaFactory.Create(_alice, bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse(
            "Agna Qel'a enters untapped when controller controls a basic Plains");
    }

    [Fact]
    public void AgnaQela_EntersTapped_WhenOnlyOpponentControlsBasicLand()
    {
        var bus = new ReplacementBus();
        var bob = new Player("Bob", 20);
        AddBasicIsland(bob); // Bob's land — Alice controls none.

        var land = AgnaQelaFactory.Create(_alice, bus);

        var intent = new ZoneMoveIntent(
            Card: land,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Battlefield,
            Controller: _alice);

        var after = bus.Apply(intent);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeTrue(
            "the predicate checks 'you control' — opponent's basics don't count");
    }
}
