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
/// Tests for <see cref="UrGolemsEyeFactory"/> — Ur-Golem's Eye, the {4}
/// artifact mana rock.
///
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}{C}."
///
/// Covers:
/// - Identity (Artifact type, printed name, {4} cost, owner/controller).
/// - Exactly one mana ability producing two colourless ({C}{C}).
///   CR 107.4c — {C} folds into the generic bucket via
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>; "CC" yields
///   <c>Generic == 2</c> (same as Worn Powerstone / Mana Crypt).
/// - No activated / triggered abilities (the rock is a pure mana source).
/// - Dispatch through <see cref="NamedCardFactory"/> resolves the name.
///
/// Ur-Golem's Eye is a strictly simpler {4} cousin of Worn Powerstone: it has
/// NO "enters tapped" clause, so CR 614.1c does not apply and the rock always
/// enters untapped.
/// </summary>
public class UrGolemsEyeFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void UrGolemsEye_IsArtifact_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        eye.Should().BeOfType<Artifact>();
        eye.HasType(CardType.Artifact).Should().BeTrue();
        eye.Name.Should().Be("Ur-Golem's Eye");
    }

    [Fact]
    public void UrGolemsEye_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        eye.Owner.Should().BeSameAs(alice);
        eye.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void UrGolemsEye_HasPrintedManaCostFour()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        // {4} — four generic, no coloured pips.
        var cost = eye.ManaCostValue;
        cost.Generic.Should().Be(4);
        cost.White.Should().Be(0);
        cost.Blue.Should().Be(0);
        cost.Black.Should().Be(0);
        cost.Red.Should().Be(0);
        cost.Green.Should().Be(0);
    }

    [Fact]
    public void UrGolemsEye_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        eye.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        eye.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void UrGolemsEye_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Ur-Golem's Eye", alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be("Ur-Golem's Eye");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void UrGolemsEye_HasExactlyOneManaAbility_ProducingTwoColorless()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        var manaAbilities = eye.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().ContainSingle("Ur-Golem's Eye has one {T}: Add {C}{C} ability");

        // CR 107.4c — {C}{C} folds into the generic bucket (Generic == 2).
        var produced = manaAbilities.Single().ManaGenerated;
        produced.Generic.Should().Be(2);
        produced.TotalValue.Should().Be(2);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
    }

    [Fact]
    public void UrGolemsEye_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var eye = (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", alice);

        eye.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only ability is a mana ability");
        eye.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void UrGolemsEye_Create_ThrowsOnNullOwner()
    {
        var act = () => (Artifact)NamedCardFactory.Create("Ur-Golem's Eye", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
