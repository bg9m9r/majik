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
/// Unit tests for <see cref="ThornspireVergeFactory"/>.
///
/// Thornspire Verge — Duskmourn: House of Horror, RG Verge cycle.
/// Counterpart to Gloomlake Verge (UB), Wastewood Verge (GB),
/// Sunsplit Verge (RW), Gleamfield Verge (GW), Floodfarm Verge (UR).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {R}.
///    {T}: Add {G}. Activate only if you control a Mountain or a Forest."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Thornspire Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {R} ability is always activable (untapped land, no restriction).
/// - {G} ability is legal only when controller's battlefield contains a
///   permanent with Mountain or Forest subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class ThornspireVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ThornspireVerge_IsLand()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void ThornspireVerge_NameIsCorrect()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Name.Should().Be("Thornspire Verge");
    }

    [Fact]
    public void ThornspireVerge_IsNotLegendary()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ThornspireVerge_OwnerAndControllerAreSet()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void ThornspireVerge_HasExactlyTwoManaAbilities()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {R} and one for {G}");
    }

    [Fact]
    public void ThornspireVerge_HasRedManaAbility()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Green == 0,
                "must have exactly one {R} mana ability");
    }

    [Fact]
    public void ThornspireVerge_HasGreenManaAbility()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Red == 0,
                "must have exactly one {G} mana ability");
    }

    [Fact]
    public void ThornspireVerge_RedManaAbility_ProducesOnlyRed()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.ManaGenerated.Generic.Should().Be(0);
        red.ManaGenerated.White.Should().Be(0);
        red.ManaGenerated.Blue.Should().Be(0);
        red.ManaGenerated.Black.Should().Be(0);
        red.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void ThornspireVerge_GreenManaAbility_ProducesOnlyGreen()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.ManaGenerated.Generic.Should().Be(0);
        green.ManaGenerated.White.Should().Be(0);
        green.ManaGenerated.Blue.Should().Be(0);
        green.ManaGenerated.Black.Should().Be(0);
        green.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void ThornspireVerge_HasNoTriggeredAbilities()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Thornspire Verge has no triggered abilities");
    }

    [Fact]
    public void ThornspireVerge_HasNoNonManaActivatedAbilities()
    {
        var land = ThornspireVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Thornspire Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {R} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThornspireVerge_RedAbility_CanActivate_WhenUntapped_NoMountainOrForest()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        // No Mountain or Forest on battlefield — {R} is still legal.
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "{T}: Add {R} has no 'activate only if' restriction");
    }

    [Fact]
    public void ThornspireVerge_RedAbility_CanActivate_WhenUntapped_WithMountain()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {G} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void ThornspireVerge_GreenAbility_CannotActivate_WhenNoMountainOrForest()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        // Controller controls no Mountain or Forest — {G} is blocked.
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "'Activate only if you control a Mountain or a Forest' blocks activation");
    }

    [Fact]
    public void ThornspireVerge_GreenAbility_CanActivate_WhenControllerHasMountain()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "controller has a Mountain on the battlefield");
    }

    [Fact]
    public void ThornspireVerge_GreenAbility_CanActivate_WhenControllerHasForest()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "controller has a Forest on the battlefield");
    }

    [Fact]
    public void ThornspireVerge_GreenAbility_CannotActivate_WhenIslandOnBattlefield_NotMountainOrForest()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "Island does not satisfy the Mountain-or-Forest restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void ThornspireVerge_AfterActivatingRedAbility_GreenAbilityCannotActivate()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        // Put a Forest on the battlefield so {G} would otherwise be legal.
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        // Activate {R} — this taps the land.
        red.Activate();

        // {G} cannot fire because the source is now tapped.
        green.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void ThornspireVerge_AfterActivatingGreenAbility_RedAbilityCannotActivate()
    {
        var land = ThornspireVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        // Activate {G} — this taps the land.
        green.Activate();

        // {R} cannot fire because the source is now tapped.
        red.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_ThornspireVerge()
    {
        var card = NamedCardFactory.Create("Thornspire Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Thornspire Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
