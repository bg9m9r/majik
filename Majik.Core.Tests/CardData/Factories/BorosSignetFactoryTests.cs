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
/// Tests for <see cref="BorosSignetFactory"/> — Boros Signet, the Ravnica
/// {2} artifact mana rock ("{1}, {T}: Add {R}{W}.").
///
/// Covers:
/// - Identity (Artifact type, printed name, {2} cost, owner/controller).
/// - Exactly one mana ability producing {R}{W}.
/// - The mana ability requires {1} in the pool (CanActivate gate).
/// - Activation pays {1} from the pool and adds {R}{W}, tapping the signet.
/// - Tap-as-cost: a tapped signet can't activate the mana ability.
/// - Dispatch through <see cref="NamedCardFactory"/> resolves the name.
/// </summary>
[Trait("Color", "C")]
public class BorosSignetFactoryTests
{
    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosSignet_IsArtifact_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        signet.Should().BeOfType<Artifact>();
        signet.HasType(CardType.Artifact).Should().BeTrue();
        signet.Name.Should().Be("Boros Signet");
    }

    [Fact]
    public void BorosSignet_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        signet.Owner.Should().BeSameAs(alice);
        signet.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void BorosSignet_HasPrintedManaCostTwo()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

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
    public void BorosSignet_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        signet.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        signet.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }
    // -----------------------------------------------------------------------
    // Mana ability — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosSignet_HasExactlyOneManaAbility_ProducingRedWhite()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        var manaAbilities = signet.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().ContainSingle("Boros Signet has one {1}, {T}: Add {R}{W} ability");

        var produced = manaAbilities.Single().ManaGenerated;
        produced.Red.Should().Be(1);
        produced.White.Should().Be(1);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Green.Should().Be(0);
        produced.Generic.Should().Be(0);
    }

    [Fact]
    public void BorosSignet_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        signet.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only ability is a mana ability");
        signet.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosSignet_CannotActivateWithoutOneGenericMana()
    {
        var alice = new Player("Alice", 20);
        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);

        // Empty mana pool — the {1} extra cost can't be paid.
        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse("the {1} cost requires mana in the pool");
    }

    [Fact]
    public void BorosSignet_CanActivateWithOneGenericInPool()
    {
        var alice = new Player("Alice", 20);
        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);
        alice.AddManaToPool(ManaCost.Parse("1"));

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeTrue();
    }

    [Fact]
    public void BorosSignet_Activation_PaysOneGeneric_AndAddsRedWhite()
    {
        var alice = new Player("Alice", 20);
        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);
        // Seed pool with {1} (the signet's extra cost).
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mana = signet.Abilities.OfType<ManaAbility>().Single();
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mana, alice);

        // {1} consumed; {R}{W} added by the activator. Net: 0 generic, 1R 1W.
        alice.ManaPool.Red.Should().Be(1);
        alice.ManaPool.White.Should().Be(1);
        alice.ManaPool.Blue.Should().Be(0);
        alice.ManaPool.Black.Should().Be(0);
        alice.ManaPool.Green.Should().Be(0);
        alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the signet's cost");
        signet.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosSignet_CannotActivateWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var signet = (Artifact)NamedCardFactory.Create("Boros Signet", alice);
        alice.AddManaToPool(ManaCost.Parse("1"));
        signet.Tap();

        signet.Abilities.OfType<ManaAbility>().Single()
            .CanActivate().Should().BeFalse("the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void BorosSignet_Create_ThrowsOnNullOwner()
    {
        var act = () => (Artifact)NamedCardFactory.Create("Boros Signet", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
