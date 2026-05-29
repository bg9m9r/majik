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
/// Unit tests for <see cref="RiverpyreVergeFactory"/>.
///
/// Riverpyre Verge — Duskmourn: House of Horror, UR Verge cycle.
///
/// Oracle text:
///   "{T}: Add {R}.
///    {T}: Add {U}. Activate only if you control an Island or a Mountain."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Riverpyre Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {R} ability is always activable (untapped land, no restriction).
/// - {U} ability is legal only when controller's battlefield contains a
///   permanent with Island or Mountain subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class RiverpyreVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RiverpyreVerge_IsLand()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void RiverpyreVerge_NameIsCorrect()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Name.Should().Be("Riverpyre Verge");
    }

    [Fact]
    public void RiverpyreVerge_IsNotLegendary()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void RiverpyreVerge_OwnerAndControllerAreSet()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void RiverpyreVerge_HasExactlyTwoManaAbilities()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {R} and one for {U}");
    }

    [Fact]
    public void RiverpyreVerge_HasRedManaAbility()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.Blue == 0,
                "must have exactly one {R} mana ability");
    }

    [Fact]
    public void RiverpyreVerge_HasBlueManaAbility()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 0,
                "must have exactly one {U} mana ability");
    }

    [Fact]
    public void RiverpyreVerge_RedManaAbility_ProducesOnlyRed()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.ManaGenerated.Generic.Should().Be(0);
        red.ManaGenerated.White.Should().Be(0);
        red.ManaGenerated.Blue.Should().Be(0);
        red.ManaGenerated.Black.Should().Be(0);
        red.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void RiverpyreVerge_BlueManaAbility_ProducesOnlyBlue()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.ManaGenerated.Generic.Should().Be(0);
        blue.ManaGenerated.White.Should().Be(0);
        blue.ManaGenerated.Black.Should().Be(0);
        blue.ManaGenerated.Red.Should().Be(0);
        blue.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void RiverpyreVerge_HasNoTriggeredAbilities()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Riverpyre Verge has no triggered abilities");
    }

    [Fact]
    public void RiverpyreVerge_HasNoNonManaActivatedAbilities()
    {
        var land = RiverpyreVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Riverpyre Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {R} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void RiverpyreVerge_RedAbility_CanActivate_WhenUntapped_NoIslandOrMountain()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        // No Island or Mountain on battlefield — {R} is still legal.
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue(
            "{T}: Add {R} has no 'activate only if' restriction");
    }

    [Fact]
    public void RiverpyreVerge_RedAbility_CanActivate_WhenUntapped_WithMountain()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {U} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void RiverpyreVerge_BlueAbility_CannotActivate_WhenNoIslandOrMountain()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        // Controller controls no Island or Mountain — {U} is blocked.
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "'Activate only if you control an Island or a Mountain' blocks activation");
    }

    [Fact]
    public void RiverpyreVerge_BlueAbility_CanActivate_WhenControllerHasIsland()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "controller has an Island on the battlefield");
    }

    [Fact]
    public void RiverpyreVerge_BlueAbility_CanActivate_WhenControllerHasMountain()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "controller has a Mountain on the battlefield");
    }

    [Fact]
    public void RiverpyreVerge_BlueAbility_CannotActivate_WhenForestOnBattlefield_NotIslandOrMountain()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Island-or-Mountain restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void RiverpyreVerge_AfterActivatingRedAbility_BlueAbilityCannotActivate()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        // Put a Mountain on the battlefield so {U} would otherwise be legal.
        var mountain = new Land("Mountain", subtypes: new[] { CardSubtype.Mountain });
        mountain.SetOwner(_alice);
        mountain.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(mountain);

        var red  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red  == 1);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        // Activate {R} — this taps the land.
        red.Activate();

        // {U} cannot fire because the source is now tapped.
        blue.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void RiverpyreVerge_AfterActivatingBlueAbility_RedAbilityCannotActivate()
    {
        var land = RiverpyreVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var red  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red  == 1);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        // Activate {U} — this taps the land.
        blue.Activate();

        // {R} cannot fire because the source is now tapped.
        red.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_RiverpyreVerge()
    {
        var card = NamedCardFactory.Create("Riverpyre Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Riverpyre Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
