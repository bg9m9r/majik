using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HushwoodVergeFactory"/>.
///
/// Hushwood Verge — GW Verge cycle (counterpart to Gloomlake Verge UB,
/// Wastewood Verge GB, etc.).
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {G}.
///    {T}: Add {W}. Activate only if you control a Forest or a Plains."
///
/// CR 605.1 — both abilities are mana abilities (do not use the stack).
/// CR 605.4 — "Activate only if …" is an activation restriction checked
/// before activation is legal; it is NOT a cost and does NOT use the stack.
///
/// Covers:
/// - Card identity: name "Hushwood Verge", Land type, not Legendary.
/// - Owner / controller assignment.
/// - Exactly two ManaAbility objects; no TriggeredAbility / ActivatedAbility.
/// - {G} ability is always activable (untapped land, no restriction).
/// - {W} ability is legal only when controller's battlefield contains a
///   permanent with Forest or Plains subtype.
/// - Tap-contention: activating either ability taps the land, rendering the
///   other ability unactivatable for the same untap cycle.
/// - NamedCardFactory dispatch round-trip.
/// </summary>
public class HushwoodVergeTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void HushwoodVerge_IsLand()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void HushwoodVerge_NameIsCorrect()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Name.Should().Be("Hushwood Verge");
    }

    [Fact]
    public void HushwoodVerge_IsNotLegendary()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void HushwoodVerge_OwnerAndControllerAreSet()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — structure
    // -----------------------------------------------------------------------

    [Fact]
    public void HushwoodVerge_HasExactlyTwoManaAbilities()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {G} and one for {W}");
    }

    [Fact]
    public void HushwoodVerge_HasGreenManaAbility()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.White == 0,
                "must have exactly one {G} mana ability");
    }

    [Fact]
    public void HushwoodVerge_HasWhiteManaAbility()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Green == 0,
                "must have exactly one {W} mana ability");
    }

    [Fact]
    public void HushwoodVerge_GreenManaAbility_ProducesOnlyGreen()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.ManaGenerated.Generic.Should().Be(0);
        green.ManaGenerated.White.Should().Be(0);
        green.ManaGenerated.Blue.Should().Be(0);
        green.ManaGenerated.Black.Should().Be(0);
        green.ManaGenerated.Red.Should().Be(0);
    }

    [Fact]
    public void HushwoodVerge_WhiteManaAbility_ProducesOnlyWhite()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.ManaGenerated.Generic.Should().Be(0);
        white.ManaGenerated.Blue.Should().Be(0);
        white.ManaGenerated.Black.Should().Be(0);
        white.ManaGenerated.Red.Should().Be(0);
        white.ManaGenerated.Green.Should().Be(0);
    }

    [Fact]
    public void HushwoodVerge_HasNoTriggeredAbilities()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Hushwood Verge has no triggered abilities");
    }

    [Fact]
    public void HushwoodVerge_HasNoNonManaActivatedAbilities()
    {
        var land = HushwoodVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Hushwood Verge has no non-mana activated abilities");
    }

    // -----------------------------------------------------------------------
    // {G} ability — activation legality (no restriction)
    // -----------------------------------------------------------------------

    [Fact]
    public void HushwoodVerge_GreenAbility_CanActivate_WhenUntapped_NoForestOrPlains()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        // No Forest or Plains on battlefield — {G} is still legal.
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue(
            "{T}: Add {G} has no 'activate only if' restriction");
    }

    [Fact]
    public void HushwoodVerge_GreenAbility_CanActivate_WhenUntapped_WithForest()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {W} ability — activation legality (conditional)
    // -----------------------------------------------------------------------

    [Fact]
    public void HushwoodVerge_WhiteAbility_CannotActivate_WhenNoForestOrPlains()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        // Controller controls no Forest or Plains — {W} is blocked.
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "'Activate only if you control a Forest or a Plains' blocks activation");
    }

    [Fact]
    public void HushwoodVerge_WhiteAbility_CanActivate_WhenControllerHasForest()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "controller has a Forest on the battlefield");
    }

    [Fact]
    public void HushwoodVerge_WhiteAbility_CanActivate_WhenControllerHasPlains()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue(
            "controller has a Plains on the battlefield");
    }

    [Fact]
    public void HushwoodVerge_WhiteAbility_CannotActivate_WhenIslandOnBattlefield_NotForestOrPlains()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var island = new Land("Island", subtypes: new[] { CardSubtype.Island });
        island.SetOwner(_alice);
        island.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(island);

        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "Island does not satisfy the Forest-or-Plains restriction");
    }

    // -----------------------------------------------------------------------
    // Tap contention — only one ability fires per untap cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void HushwoodVerge_AfterActivatingGreenAbility_WhiteAbilityCannotActivate()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        // Put a Plains on the battlefield so {W} would otherwise be legal.
        var plains = new Land("Plains", subtypes: new[] { CardSubtype.Plains });
        plains.SetOwner(_alice);
        plains.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(plains);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        // Activate {G} — this taps the land.
        green.Activate();

        // {W} cannot fire because the source is now tapped.
        white.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    [Fact]
    public void HushwoodVerge_AfterActivatingWhiteAbility_GreenAbilityCannotActivate()
    {
        var land = HushwoodVergeFactory.Create(_alice);
        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(forest);

        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        // Activate {W} — this taps the land.
        white.Activate();

        // {G} cannot fire because the source is now tapped.
        green.CanActivate().Should().BeFalse(
            "the land is already tapped; both abilities share the same {T} cost");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_HushwoodVerge()
    {
        var card = NamedCardFactory.Create("Hushwood Verge", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hushwood Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
