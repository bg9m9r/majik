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
/// Unit tests for <see cref="GloomlakeVergeFactory"/>.
///
/// Gloomlake Verge — Duskmourn: House of Horror, UB Verge cycle.
///
/// Oracle text:
///   "{T}: Add {U}.
///    {T}: Add {B}. Activate only if you control an Island or a Swamp."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Gloomlake Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {U} ability is always activable (untapped land, no restriction).
/// - {B} ability is legal only when controller's battlefield contains a
///   permanent with Island or Swamp subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class GloomlakeVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void GloomlakeVerge_IsLand()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void GloomlakeVerge_NameIsCorrect()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Name.Should().Be("Gloomlake Verge");
    }

    [Fact]
    public void GloomlakeVerge_IsNotLegendary()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void GloomlakeVerge_OwnerAndControllerAreSet()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void GloomlakeVerge_HasExactlyTwoManaAbilities()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {U} and one for {B}");
    }

    [Fact]
    public void GloomlakeVerge_HasBlueManaAbility()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Black == 0,
                "must have exactly one {U} mana ability");
    }

    [Fact]
    public void GloomlakeVerge_HasBlackManaAbility()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Blue == 0,
                "must have exactly one {B} mana ability");
    }

    [Fact]
    public void GloomlakeVerge_BlueManaAbility_ProducesOnlyBlue()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.ManaGenerated.Generic.Should().Be(0);
        blue.ManaGenerated.White.Should().Be(0);
        blue.ManaGenerated.Black.Should().Be(0);
        blue.ManaGenerated.Red.Should().Be(0);
        blue.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void GloomlakeVerge_BlackManaAbility_ProducesOnlyBlack()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.ManaGenerated.Generic.Should().Be(0);
        black.ManaGenerated.White.Should().Be(0);
        black.ManaGenerated.Blue.Should().Be(0);
        black.ManaGenerated.Red.Should().Be(0);
        black.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void GloomlakeVerge_HasNoTriggeredAbilities()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Gloomlake Verge has no triggered abilities");
    }

    [Fact]
    public void GloomlakeVerge_HasNoNonManaActivatedAbilities()
    {
        var land = GloomlakeVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Gloomlake Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {U} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void GloomlakeVerge_BlueAbility_CanActivate_WhenUntapped_NoIslandOrSwamp()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        // No Island or Swamp on battlefield — {U} is still legal.
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue(
            "{T}: Add {U} has no 'activate only if' restriction");
    }

    [Fact]
    public void GloomlakeVerge_BlueAbility_CanActivate_WhenUntapped_WithIsland()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {B} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void GloomlakeVerge_BlackAbility_CannotActivate_WhenNoIslandOrSwamp()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        // Controller controls no Island or Swamp — {B} is blocked.
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeFalse(
            "'Activate only if you control an Island or a Swamp' blocks activation");
    }

    [Fact]
    public void GloomlakeVerge_BlackAbility_CanActivate_WhenControllerHasIsland()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue(
            "controller has an Island on the battlefield");
    }

    [Fact]
    public void GloomlakeVerge_BlackAbility_CanActivate_WhenControllerHasSwamp()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeTrue(
            "controller has a Swamp on the battlefield");
    }

    [Fact]
    public void GloomlakeVerge_BlackAbility_CannotActivate_WhenForestOnBattlefield_NotIslandOrSwamp()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.CanActivate().Should().BeFalse(
            "Forest does not satisfy the Island-or-Swamp restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void GloomlakeVerge_AfterActivatingBlueAbility_BlackAbilityCannotActivate()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        // Put a Swamp on the battlefield so {B} would otherwise be legal.
        var swamp = new Land("Swamp", subtypes: new[] { CardSubtype.Swamp });
        swamp.SetOwner(_alice);
        swamp.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(swamp);

        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        // Activate {U} — this taps the land.
        blue.Activate();

        // {B} cannot fire because the source is now tapped.
        black.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void GloomlakeVerge_AfterActivatingBlackAbility_BlueAbilityCannotActivate()
    {
        var land = GloomlakeVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var blue  = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue  == 1);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        // Activate {B} — this taps the land.
        black.Activate();

        // {U} cannot fire because the source is now tapped.
        blue.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_GloomlakeVerge()
    {
        var card = NamedCardFactory.Create("Gloomlake Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Gloomlake Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
