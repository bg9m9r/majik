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
/// Unit tests for <see cref="BlazemireVergeFactory"/>.
///
/// Blazemire Verge — Murders at Karlov Manor Commander / Verge cycle, BR.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {B}.
///    {T}: Add {R}. Activate only if you control a Swamp or a Mountain."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Note the asymmetry vs. Gloomlake Verge: here the FIRST listed ability
/// ({B}) is unconditional and the SECOND ({R}) carries the restriction.
///
/// Covers:
/// - Card identity: name "Blazemire Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {B} ability is always activable (untapped land, no restriction).
/// - {R} ability is legal only when controller's battlefield contains a
///   permanent with Swamp or Mountain subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class BlazemireVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazemireVerge_IsLand()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void BlazemireVerge_NameIsCorrect()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Name.Should().Be("Blazemire Verge");
    }

    [Fact]
    public void BlazemireVerge_IsNotLegendary()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void BlazemireVerge_OwnerAndControllerAreSet()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazemireVerge_HasExactlyTwoManaAbilities()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {B} and one for {R}");
    }

    [Fact]
    public void BlazemireVerge_HasBlackManaAbility()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Red == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void BlazemireVerge_HasRedManaAbility()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {R} mana ability");
    }

    [Fact]
    public void BlazemireVerge_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void BlazemireVerge_RedManaAbility_ProducesOnlyRed()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.ManaGenerated.Generic.Should().Be(0);
        red.ManaGenerated.White.Should().Be(0);
        red.ManaGenerated.Blue.Should().Be(0);
        red.ManaGenerated.Black.Should().Be(0);
        red.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void BlazemireVerge_HasNoTriggeredAbilities()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Blazemire Verge has no triggered abilities");
    }

    [Fact]
    public void BlazemireVerge_HasNoNonManaActivatedAbilities()
    {
        var land = BlazemireVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Blazemire Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {B} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazemireVerge_BlackAbility_CanActivate_WhenUntapped_NoSwampOrMountain()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        // No Swamp or Mountain on battlefield — {B} is still legal.
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue(
            "{T}: Add {B} has no 'activate only if' restriction");
    }

    [Fact]
    public void BlazemireVerge_BlackAbility_CanActivate_WhenUntapped_WithSwamp()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {R} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazemireVerge_RedAbility_CannotActivate_WhenNoSwampOrMountain()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        // Controller controls no Swamp or Mountain — {R} is blocked.
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeFalse(
            "'Activate only if you control a Swamp or a Mountain' blocks activation");
    }

    [Fact]
    public void BlazemireVerge_RedAbility_CanActivate_WhenControllerHasSwamp()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "controller has a Swamp on the battlefield");
    }

    [Fact]
    public void BlazemireVerge_RedAbility_CanActivate_WhenControllerHasMountain()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "controller has a Mountain on the battlefield");
    }

    [Fact]
    public void BlazemireVerge_RedAbility_CannotActivate_WhenForestOnBattlefield_NotSwampOrMountain()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Swamp-or-Mountain restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void BlazemireVerge_AfterActivatingBlackAbility_RedAbilityCannotActivate()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        // Put a Mountain on the battlefield so {R} would otherwise be legal.
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);
        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);

        // Activate {B} — this taps the land.
        black.Activate();

        // {R} cannot fire because the source is now tapped.
        red.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void BlazemireVerge_AfterActivatingRedAbility_BlackAbilityCannotActivate()
    {
        var land = BlazemireVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);
        var red   = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red   == 1);

        // Activate {R} — this taps the land.
        red.Activate();

        // {B} cannot fire because the source is now tapped.
        black.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_BlazemireVerge()
    {
        var card = NamedCardFactory.Create("Blazemire Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Blazemire Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
