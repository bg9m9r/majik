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
/// Unit tests for <see cref="FloodfarmVergeFactory"/>.
///
/// Floodfarm Verge — Duskmourn: House of Horror, WU Verge cycle.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {W}.
///    {T}: Add {U}. Activate only if you control a Plains or an Island."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Mirrors the sibling cycle tests <see cref="GloomlakeVergeFactory"/> (UB)
/// and <see cref="BleachboneVergeFactory"/> (WB): the conditional {U} ability
/// is gated by a <c>canActivateCheck</c> predicate, because
/// <c>ManaAbilityDefinition</c> JSON carries only the produced mana and cannot
/// express the subtype restriction.
///
/// Covers:
/// - Card identity: name "Floodfarm Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {W} ability is always activable (untapped land, no restriction).
/// - {U} ability is legal only when controller's battlefield contains a
///   permanent with Plains or Island subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class FloodfarmVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodfarmVerge_IsLand()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void FloodfarmVerge_NameIsCorrect()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Name.Should().Be("Floodfarm Verge");
    }

    [Fact]
    public void FloodfarmVerge_IsNotLegendary()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void FloodfarmVerge_OwnerAndControllerAreSet()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodfarmVerge_HasExactlyTwoManaAbilities()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {W} and one for {U}");
    }

    [Fact]
    public void FloodfarmVerge_HasWhiteManaAbility()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Blue == 0,
                "must have exactly one {W} mana ability");
    }

    [Fact]
    public void FloodfarmVerge_HasBlueManaAbility()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.White == 0,
                "must have exactly one {U} mana ability");
    }

    [Fact]
    public void FloodfarmVerge_WhiteManaAbility_ProducesOnlyWhite()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.ManaGenerated.Generic.Should().Be(0);
        white.ManaGenerated.Blue.Should().Be(0);
        white.ManaGenerated.Black.Should().Be(0);
        white.ManaGenerated.Red.Should().Be(0);
        white.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void FloodfarmVerge_BlueManaAbility_ProducesOnlyBlue()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.ManaGenerated.Generic.Should().Be(0);
        blue.ManaGenerated.White.Should().Be(0);
        blue.ManaGenerated.Black.Should().Be(0);
        blue.ManaGenerated.Red.Should().Be(0);
        blue.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void FloodfarmVerge_HasNoTriggeredAbilities()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Floodfarm Verge has no triggered abilities");
    }

    [Fact]
    public void FloodfarmVerge_HasNoNonManaActivatedAbilities()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Floodfarm Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {W} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodfarmVerge_WhiteAbility_CanActivate_WhenUntapped_NoPlainsOrIsland()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        // No Plains or Island on battlefield — {W} is still legal.
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "{T}: Add {W} has no 'activate only if' restriction");
    }

    [Fact]
    public void FloodfarmVerge_WhiteAbility_CanActivate_WhenUntapped_WithPlains()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {U} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodfarmVerge_BlueAbility_CannotActivate_WhenNoPlainsOrIsland()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        // Controller controls no Plains or Island — {U} is blocked.
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "'Activate only if you control a Plains or an Island' blocks activation");
    }

    [Fact]
    public void FloodfarmVerge_BlueAbility_CanActivate_WhenControllerHasPlains()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "controller has a Plains on the battlefield");
    }

    [Fact]
    public void FloodfarmVerge_BlueAbility_CanActivate_WhenControllerHasIsland()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "controller has an Island on the battlefield");
    }

    [Fact]
    public void FloodfarmVerge_BlueAbility_CannotActivate_WhenForestOnBattlefield_NotPlainsOrIsland()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Plains-or-Island restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void FloodfarmVerge_AfterActivatingWhiteAbility_BlueAbilityCannotActivate()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        // Put an Island on the battlefield so {U} would otherwise be legal.
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);
        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);

        // Activate {W} — this taps the land.
        white.Activate();

        // {U} cannot fire because the source is now tapped.
        blue.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void FloodfarmVerge_AfterActivatingBlueAbility_WhiteAbilityCannotActivate()
    {
        var land = FloodfarmVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);
        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);

        // Activate {U} — this taps the land.
        blue.Activate();

        // {W} cannot fire because the source is now tapped.
        white.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_FloodfarmVerge()
    {
        var card = NamedCardFactory.Create("Floodfarm Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Floodfarm Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
