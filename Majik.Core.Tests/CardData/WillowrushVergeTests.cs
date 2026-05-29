using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="WillowrushVergeFactory"/>.
///
/// Willowrush Verge — Tarkir: Dragonstorm, GU Verge cycle.
///
/// Oracle text:
///   "{T}: Add {U}.
///    {T}: Add {G}. Activate only if you control a Forest or an Island."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Willowrush Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {U} ability is always activable (untapped land, no restriction).
/// - {G} ability is legal only when controller's battlefield contains a
///   permanent with Forest or Island subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class WillowrushVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WillowrushVerge_IsLand()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void WillowrushVerge_NameIsCorrect()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Name.Should().Be("Willowrush Verge");
    }

    [Fact]
    public void WillowrushVerge_IsNotLegendary()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void WillowrushVerge_OwnerAndControllerAreSet()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void WillowrushVerge_HasExactlyTwoManaAbilities()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {U} and one for {G}");
    }

    [Fact]
    public void WillowrushVerge_HasBlueManaAbility()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0,
                "must have exactly one {U} mana ability");
    }

    [Fact]
    public void WillowrushVerge_HasGreenManaAbility()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0,
                "must have exactly one {G} mana ability");
    }

    [Fact]
    public void WillowrushVerge_BlueManaAbility_ProducesOnlyBlue()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.ManaGenerated.Generic.Should().Be(0);
        blue.ManaGenerated.White.Should().Be(0);
        blue.ManaGenerated.Black.Should().Be(0);
        blue.ManaGenerated.Red.Should().Be(0);
        blue.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void WillowrushVerge_GreenManaAbility_ProducesOnlyGreen()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.ManaGenerated.Generic.Should().Be(0);
        green.ManaGenerated.White.Should().Be(0);
        green.ManaGenerated.Blue.Should().Be(0);
        green.ManaGenerated.Black.Should().Be(0);
        green.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void WillowrushVerge_HasNoTriggeredAbilities()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Willowrush Verge has no triggered abilities");
    }

    [Fact]
    public void WillowrushVerge_HasNoNonManaActivatedAbilities()
    {
        var land = WillowrushVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Willowrush Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {U} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void WillowrushVerge_BlueAbility_CanActivate_WhenUntapped_NoForestOrIsland()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        // No Forest or Island on battlefield — {U} is still legal.
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "{T}: Add {U} has no 'activate only if' restriction");
    }

    [Fact]
    public void WillowrushVerge_BlueAbility_CanActivate_WhenUntapped_WithForest()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {G} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void WillowrushVerge_GreenAbility_CannotActivate_WhenNoForestOrIsland()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        // Controller controls no Forest or Island — {G} is blocked.
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "'Activate only if you control a Forest or an Island' blocks activation");
    }

    [Fact]
    public void WillowrushVerge_GreenAbility_CanActivate_WhenControllerHasForest()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "controller has a Forest on the battlefield");
    }

    [Fact]
    public void WillowrushVerge_GreenAbility_CanActivate_WhenControllerHasIsland()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "controller has an Island on the battlefield");
    }

    [Fact]
    public void WillowrushVerge_GreenAbility_CannotActivate_WhenSwampOnBattlefield_NotForestOrIsland()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "Swamp does not satisfy the Forest-or-Island restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void WillowrushVerge_AfterActivatingBlueAbility_GreenAbilityCannotActivate()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        // Put a Forest on the battlefield so {G} would otherwise be legal.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        // Activate {U} — this taps the land.
        blue.Activate();

        // {G} cannot fire because the source is now tapped.
        green.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void WillowrushVerge_AfterActivatingGreenAbility_BlueAbilityCannotActivate()
    {
        var land = WillowrushVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        // Activate {G} — this taps the land.
        green.Activate();

        // {U} cannot fire because the source is now tapped.
        blue.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_WillowrushVerge()
    {
        var card = NamedCardFactory.Create("Willowrush Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Willowrush Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
