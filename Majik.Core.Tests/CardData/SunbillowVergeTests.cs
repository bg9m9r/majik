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
/// Unit tests for <see cref="SunbillowVergeFactory"/>.
///
/// Sunbillow Verge — Tarkir: Dragonstorm, RW Verge cycle (counterpart to
/// Gloomlake Verge UB, Wastewood Verge GB, Floodfarm Verge UR, etc.).
///
/// Oracle text:
///   "{T}: Add {W}.
///    {T}: Add {R}. Activate only if you control a Mountain or a Plains."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Sunbillow Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {W} ability is always activable (untapped land, no restriction).
/// - {R} ability is legal only when controller's battlefield contains a
///   permanent with Mountain or Plains subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class SunbillowVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbillowVerge_IsLand()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SunbillowVerge_NameIsCorrect()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Name.Should().Be("Sunbillow Verge");
    }

    [Fact]
    public void SunbillowVerge_IsNotLegendary()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SunbillowVerge_OwnerAndControllerAreSet()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbillowVerge_HasExactlyTwoManaAbilities()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {W} and one for {R}");
    }

    [Fact]
    public void SunbillowVerge_HasWhiteManaAbility()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0,
                "must have exactly one {W} mana ability");
    }

    [Fact]
    public void SunbillowVerge_HasRedManaAbility()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0,
                "must have exactly one {R} mana ability");
    }

    [Fact]
    public void SunbillowVerge_WhiteManaAbility_ProducesOnlyWhite()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.ManaGenerated.Generic.Should().Be(0);
        white.ManaGenerated.Blue.Should().Be(0);
        white.ManaGenerated.Black.Should().Be(0);
        white.ManaGenerated.Red.Should().Be(0);
        white.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void SunbillowVerge_RedManaAbility_ProducesOnlyRed()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.ManaGenerated.Generic.Should().Be(0);
        red.ManaGenerated.White.Should().Be(0);
        red.ManaGenerated.Blue.Should().Be(0);
        red.ManaGenerated.Black.Should().Be(0);
        red.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void SunbillowVerge_HasNoTriggeredAbilities()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Sunbillow Verge has no triggered abilities");
    }

    [Fact]
    public void SunbillowVerge_HasNoNonManaActivatedAbilities()
    {
        var land = SunbillowVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Sunbillow Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {W} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbillowVerge_WhiteAbility_CanActivate_WhenUntapped_NoMountainOrPlains()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        // No Mountain or Plains on battlefield — {W} is still legal.
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "{T}: Add {W} has no 'activate only if' restriction");
    }

    [Fact]
    public void SunbillowVerge_WhiteAbility_CanActivate_WhenUntapped_WithMountain()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {R} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbillowVerge_RedAbility_CannotActivate_WhenNoMountainOrPlains()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        // Controller controls no Mountain or Plains — {R} is blocked.
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeFalse(
            "'Activate only if you control a Mountain or a Plains' blocks activation");
    }

    [Fact]
    public void SunbillowVerge_RedAbility_CanActivate_WhenControllerHasMountain()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "controller has a Mountain on the battlefield");
    }

    [Fact]
    public void SunbillowVerge_RedAbility_CanActivate_WhenControllerHasPlains()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "controller has a Plains on the battlefield");
    }

    [Fact]
    public void SunbillowVerge_RedAbility_CannotActivate_WhenForestOnBattlefield_NotMountainOrPlains()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Mountain-or-Plains restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void SunbillowVerge_AfterActivatingWhiteAbility_RedAbilityCannotActivate()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        // Put a Mountain on the battlefield so {R} would otherwise be legal.
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);
        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);

        // Activate {W} — this taps the land.
        white.Activate();

        // {R} cannot fire because the source is now tapped.
        red.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void SunbillowVerge_AfterActivatingRedAbility_WhiteAbilityCannotActivate()
    {
        var land = SunbillowVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);
        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);

        // Activate {R} — this taps the land.
        red.Activate();

        // {W} cannot fire because the source is now tapped.
        white.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_SunbillowVerge()
    {
        var card = NamedCardFactory.Create("Sunbillow Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Sunbillow Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
