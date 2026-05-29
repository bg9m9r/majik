using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
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
///
/// Same JSON-pipeline coverage as the sibling <see cref="WastewoodVergeFactory"/>
/// (GB Verge): two plain mana abilities. The "Activate only if you control a
/// Plains or an Island" restriction (CR 605.4) is NOT expressible in the
/// current <c>ManaAbilityDefinition</c> JSON schema (it carries only a
/// <c>produces</c> field), so — exactly like Wastewood Verge and the Kaladesh
/// fastlands — the conditional is deferred to the binder layer. This factory
/// therefore wires the two mana outputs without the activation predicate.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Owner and controller assignment
/// - Two mana abilities: {W} and {U}
/// - Mana outputs are correct and exclusive
/// - No triggered / non-mana activated abilities
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
    // Mana abilities
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
    public void FloodfarmVerge_HasNoActivatedAbilities()
    {
        var land = FloodfarmVergeFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Floodfarm Verge has no non-mana activated abilities in v1");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_FloodfarmVerge()
    {
        var card = NamedCardFactory.Create("Floodfarm Verge", _alice);

        card.Should().BeOfType<Majik.Core.Cards.Land>();
        card.Name.Should().Be("Floodfarm Verge");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }
}
