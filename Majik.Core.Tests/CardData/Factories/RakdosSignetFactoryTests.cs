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
/// Tests for <see cref="RakdosSignetFactory"/> — Rakdos Signet, the
/// Ravnica {2} artifact mana rock ("{1}, {T}: Add {B}{R}.").
///
/// Covers:
/// - Identity (Artifact type, printed name, {2} cost, owner/controller).
/// - Exactly one mana ability producing {B}{R}.
/// - The mana ability requires {1} in the pool (CanActivate gate).
/// - Activation pays {1} from the pool and adds {B}{R}, tapping the signet.
/// - Tap-as-cost: a tapped signet can't activate the mana ability.
/// - Dispatch through <see cref="NamedCardFactory"/> resolves the name.
/// </summary>
public class RakdosSignetFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void RakdosSignet_IsArtifact_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        signet.Should().BeOfType<Artifact>();
        signet.HasType(CardType.Artifact).Should().BeTrue();
        signet.Name.Should().Be("Rakdos Signet");
    }

    [Fact]
    public void RakdosSignet_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        signet.Owner.Should().BeSameAs(alice);
        signet.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void RakdosSignet_HasPrintedManaCostTwo()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        // {2} — two generic, no coloured pips.
        var cost = signet.ManaCostValue;
        cost.Generic.Should().Be(2);
        cost.White.Should().Be(0);
        cost.Blue.Should().Be(0);
        cost.Black.Should().Be(0);
        cost.Red.Should().Be(0);
        cost.Green.Should().Be(0);
    }

    [Fact]
    public void RakdosSignet_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        signet.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        signet.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void RakdosSignet_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create("Rakdos Signet", alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be("Rakdos Signet");
    }

    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void RakdosSignet_HasExactlyOneManaAbility_ProducingBlackRed()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        var manaAbilities = signet.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().ContainSingle("Rakdos Signet has one {1}, {T}: Add {B}{R} ability");

        var produced = manaAbilities.Single().ManaGenerated;
        produced.Black.Should().Be(1);
        produced.Red.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.Generic.Should().Be(0);
    }

    [Fact]
    public void RakdosSignet_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var signet = RakdosSignetFactory.Create(alice);

        signet.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only ability is a mana ability");
        signet.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void RakdosSignet_CannotActivateWithoutOneGenericMana()
    {
        var alice = new Player("Alice", 20);
        var signet = RakdosSignetFactory.Create(alice);

        // Empty mana pool — the {1} extra cost can't be paid.
        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse("the {1} cost requires mana in the pool");
    }

    [Fact]
    public void RakdosSignet_CanActivateWithOneGenericInPool()
    {
        var alice = new Player("Alice", 20);
        var signet = RakdosSignetFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeTrue();
    }

    [Fact]
    public void RakdosSignet_Activation_PaysOneGeneric_AndAddsBlackRed()
    {
        var alice = new Player("Alice", 20);
        var signet = RakdosSignetFactory.Create(alice);
        // Seed pool with {1} (the signet's extra cost).
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mana, alice);

        // {1} consumed; {B}{R} added by the activator. Net: 0 generic, 1B 1R.
        alice.ManaPool.Black.Should().Be(1);
        alice.ManaPool.Red.Should().Be(1);
        alice.ManaPool.White.Should().Be(0);
        alice.ManaPool.Blue.Should().Be(0);
        alice.ManaPool.Green.Should().Be(0);
        alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the signet's cost");
        signet.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void RakdosSignet_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var signet = RakdosSignetFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));
        signet.Tap();

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse("the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void RakdosSignet_Create_ThrowsOnNullOwner()
    {
        var act = () => RakdosSignetFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
