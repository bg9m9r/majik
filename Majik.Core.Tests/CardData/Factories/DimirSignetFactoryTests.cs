using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DimirSignetFactory"/> — the Ravnica signet
/// mana-rock.
///
/// Oracle text (verified against Scryfall):
/// <code>
/// Artifact {2}.
/// {1}, {T}: Add {U}{B}.
/// </code>
///
/// The signet's single coloured mode is the artifact analogue of the
/// filter-land filter mode: a {1} additional mana cost paid into the same
/// activation as the {T} tap, producing two coloured pips at once (here
/// {U} and {B} together, not "or"). Mirrors
/// <see cref="FilterLandCycleFactory"/>'s additional-cost
/// <see cref="ManaAbility"/> shape. CR 605.1 — still a mana ability, never
/// on the stack; the {1} is paid atomically with the {T} tap.
/// </summary>
public class DimirSignetFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void DimirSignet_IsArtifact_TwoCost_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var signet = DimirSignetFactory.Create(alice);

        signet.Should().BeOfType<Artifact>();
        signet.HasType(CardType.Artifact).Should().BeTrue();
        signet.Name.Should().Be("Dimir Signet");
        signet.ManaCost.Should().Be("{2}");
    }

    [Fact]
    public void DimirSignet_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var signet = DimirSignetFactory.Create(alice);

        signet.Owner.Should().BeSameAs(alice);
        signet.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void DimirSignet_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Dimir Signet", alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be("Dimir Signet");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void DimirSignet_HasExactlyOneManaAbility()
    {
        var alice = new Player("Alice", 20);

        var signet = DimirSignetFactory.Create(alice);

        signet.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the signet has a single {1}, {T}: Add {U}{B} mode");
    }

    [Fact]
    public void DimirSignet_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var signet = DimirSignetFactory.Create(alice);

        signet.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the signet has no non-mana activated abilities");
        signet.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "the signet has no triggered abilities");
    }

    [Fact]
    public void DimirSignet_ManaAbility_ProducesUAndB()
    {
        var alice = new Player("Alice", 20);

        var signet = DimirSignetFactory.Create(alice);

        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Blue.Should().Be(1, "Add {U}{B} yields one blue");
        mana.ManaGenerated.Black.Should().Be(1, "Add {U}{B} yields one black");
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
        mana.ManaGenerated.Generic.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {1} additional cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void DimirSignet_CannotActivateWithoutOneGenericMana()
    {
        var alice = new Player("Alice", 20);
        var signet = DimirSignetFactory.Create(alice);

        // Empty pool — cannot pay the {1} additional cost.
        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse(
                "the {U}{B} mode requires {1} in the pool");
    }

    [Fact]
    public void DimirSignet_CanActivateWithOneGenericInPool()
    {
        var alice = new Player("Alice", 20);
        var signet = DimirSignetFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeTrue();
    }

    [Fact]
    public void DimirSignet_Activation_PaysOneGeneric_AndAddsUB()
    {
        var alice = new Player("Alice", 20);
        var signet = DimirSignetFactory.Create(alice);
        // Seed pool with {1} (the signet cost).
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mana, alice);

        // {1} consumed; {U}{B} added by the activator. Net +1 mana,
        // converted into one blue + one black pip.
        alice.ManaPool.Blue.Should().Be(1);
        alice.ManaPool.Black.Should().Be(1);
        alice.ManaPool.White.Should().Be(0);
        alice.ManaPool.Red.Should().Be(0);
        alice.ManaPool.Green.Should().Be(0);
        alice.ManaPool.Generic.Should().Be(0,
            "the seed {1} was spent on the signet's activation cost");
        signet.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost — tapped signet cannot activate
    // -----------------------------------------------------------------------

    [Fact]
    public void DimirSignet_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var signet = DimirSignetFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));
        signet.Tap();

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse(
                "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void DimirSignet_Create_ThrowsOnNullOwner()
    {
        var act = () => DimirSignetFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
