using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoseijuWhoSheltersAllFactory"/>.
///
/// Card: Boseiju, Who Shelters All (Legendary Land).
/// Oracle text (verified against Scryfall):
///   "Boseiju, Who Shelters All enters tapped.
///    {T}, Pay 2 life: Add {C}. If that mana is spent on an instant or
///    sorcery spell, that spell can't be countered."
///
/// Covers:
/// - Card identity (name, Legendary supertype, Land type, owner/controller).
/// - The single "{T}, Pay 2 life: Add {C}" mana ability — colorless output,
///   no activated (stack-using) ability.
/// - The "Pay 2 life" activation gate (CR 119.4) and that activating both
///   taps the land and pays 2 life.
///
/// Deferred (not asserted — see factory xmldoc): the "that spell can't be
/// countered" rider (per-slot mana provenance + cast-time flag) and the
/// production-path enters-tapped replacement (owned by EntersTappedBinder).
/// </summary>
[Trait("Color", "C")]
public class BoseijuWhoSheltersAllTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_HasCorrectName()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);

        bos.Name.Should().Be("Boseiju, Who Shelters All");
    }

    [Fact]
    public void Boseiju_IsLegendaryLand()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);

        bos.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        bos.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Boseiju_OwnerAndControllerAreSet()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);

        bos.Owner.Should().BeSameAs(_alice);
        bos.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}, Pay 2 life: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_HasExactlyOneManaAbility()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);

        bos.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Boseiju_HasNoStackUsingActivatedAbility()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);

        bos.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only activated ability is a mana ability (ManaAbility), which doesn't use the stack");
    }

    [Fact]
    public void Boseiju_ManaAbility_ProducesColorless()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        // {C} rolls into the generic bucket (ManaCost.Parse, case 'C').
        mana.ManaGenerated.Generic.Should().Be(1, "Boseiju taps for exactly one {C}");
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Pay-2-life activation cost (CR 119.4)
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_ManaAbility_CanActivate_WhenUntappedAndLifeAbove2()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice); // Alice at 20 life
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue();
    }

    [Fact]
    public void Boseiju_ManaAbility_CannotActivate_WhenLifeIsExactly2()
    {
        var bob = new Player("Bob", 2);
        var bos = BoseijuWhoSheltersAllFactory.Create(bob);
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeFalse(
            "CR 119.4 — you can't pay 2 life with only 2 life (it would not leave you above 0 after the cost? "
            + "strictly the gate requires life > 2 so the printed 'Pay 2 life' is payable)");
    }

    [Fact]
    public void Boseiju_ManaAbility_Activate_TapsLandAndPays2Life()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice); // 20 life
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        var produced = mana.Activate();

        produced.Generic.Should().Be(1, "one {C} produced");
        bos.IsTapped.Should().BeTrue("{T} is part of the activation cost");
        _alice.LifeTotal.Should().Be(18, "Pay 2 life is the additional activation cost");
    }

    [Fact]
    public void Boseiju_ManaAbility_CannotActivate_WhenAlreadyTapped()
    {
        var bos = BoseijuWhoSheltersAllFactory.Create(_alice);
        bos.Tap();
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeFalse("a tapped land can't pay the {T} cost again");
    }
}
