using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpringleafDrumFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact, owner/controller).
/// - NamedCardFactory dispatch.
/// - Five mana abilities (one per WUBRG colour).
/// - Activation: taps drum AND another untapped creature, produces one
///   coloured mana.
/// - CanActivate false when no eligible creature available.
/// - CanActivate false when drum is already tapped.
/// - Summoning sickness on the creature blocks the tap-cost path.
/// </summary>
public class SpringleafDrumTests
{
    private readonly Player _alice = new("Alice", 20);

    private Creature ReadyBear()
    {
        var bear = new Creature("Bear", "1G", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.ClearSummoningSickness();
        return bear;
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpringleafDrum_Identity()
    {
        var drum = SpringleafDrumFactory.Create(_alice);

        drum.Name.Should().Be("Springleaf Drum");
        drum.HasType(CardType.Artifact).Should().BeTrue();
        drum.ManaCost.Should().Be("{1}");
        drum.Owner.Should().BeSameAs(_alice);
        drum.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpringleafDrum_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Springleaf Drum", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Springleaf Drum");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SpringleafDrum_HasFiveColorManaAbilities()
    {
        var drum = SpringleafDrumFactory.Create(_alice);

        drum.Abilities.OfType<SpringleafDrumManaAbility>().Should().HaveCount(5);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void SpringleafDrum_HasOneAbilityPerColor(string colorPip)
    {
        var drum = SpringleafDrumFactory.Create(_alice);

        drum.Abilities.OfType<SpringleafDrumManaAbility>()
            .Should().ContainSingle(a => a.ColorPip == colorPip);
    }

    // -----------------------------------------------------------------------
    // Activation
    // -----------------------------------------------------------------------

    [Fact]
    public void SpringleafDrum_TapForBlue_TapsDrumAndCreature_ProducesU()
    {
        var drum = SpringleafDrumFactory.Create(_alice);
        var bear = ReadyBear();

        var blue = drum.Abilities.OfType<SpringleafDrumManaAbility>()
            .Single(a => a.ColorPip == "U");
        blue.TapChoice.Target = bear;

        blue.CanActivate().Should().BeTrue();
        var mana = blue.Activate();

        mana.Blue.Should().Be(1, "{T}+tap-creature: Add one mana of any color — here U");
        mana.Generic.Should().Be(0);
        drum.IsTapped.Should().BeTrue("self-tap is part of the activation cost");
        bear.IsTapped.Should().BeTrue("the tap-another-creature cost taps the bear");
    }

    [Fact]
    public void SpringleafDrum_FallsBack_ToFirstEligibleCreature_WhenNoTargetSet()
    {
        var drum = SpringleafDrumFactory.Create(_alice);
        var bear = ReadyBear();

        var green = drum.Abilities.OfType<SpringleafDrumManaAbility>()
            .Single(a => a.ColorPip == "G");

        // Target intentionally unset — deterministic first-eligible fallback.
        var mana = green.Activate();

        mana.Green.Should().Be(1);
        bear.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // CanActivate gates
    // -----------------------------------------------------------------------

    [Fact]
    public void SpringleafDrum_CannotActivate_WhenNoOtherCreature()
    {
        var drum = SpringleafDrumFactory.Create(_alice);

        var any = drum.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "the tap-an-untapped-creature cost cannot be paid without an eligible creature");
    }

    [Fact]
    public void SpringleafDrum_CannotActivate_WhenDrumTapped()
    {
        var drum = SpringleafDrumFactory.Create(_alice);
        ReadyBear();
        drum.Tap();

        var any = drum.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse("the drum itself must be untapped to pay {T}");
    }

    [Fact]
    public void SpringleafDrum_CannotActivate_WhenOnlyCreature_HasSummoningSickness()
    {
        var drum = SpringleafDrumFactory.Create(_alice);
        var sick = new Creature("Wurm", "5G", 5, 5);
        sick.SetOwner(_alice);
        sick.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(sick);
        // Summoning sickness is the default on Permanent — do NOT clear.

        var any = drum.Abilities.OfType<SpringleafDrumManaAbility>().First();
        any.CanActivate().Should().BeFalse(
            "a summoning-sick creature cannot be tapped to pay a tap-cost");
    }
}
