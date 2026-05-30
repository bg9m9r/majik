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
/// Tests for <see cref="GildedLotusFactory"/> — Gilded Lotus, the {5}
/// artifact mana rock ("{T}: Add three mana of any one color.").
///
/// Analogue of Lotus Bloom's "three mana of any one color" output, but
/// without the Suspend wrapper or the sacrifice rider — Gilded Lotus is a
/// permanent fixture that taps for three of one colour each turn.
///
/// Covers:
/// - Identity (Artifact, {5} cost, non-legendary, owner/controller).
/// - Five WUBRG mana abilities, each producing three pips of its colour.
/// - No activated/triggered abilities (the only abilities are mana).
/// - Activation taps the lotus and credits three of the chosen colour.
/// - Tap-as-cost: a tapped lotus can't activate; activating one colour
///   disables the other four (CR 605.1b — only one "any one color" mode).
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class GildedLotusFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void GildedLotus_IsArtifact_WithCorrectName_NonLegendary()
    {
        var lotus = GildedLotusFactory.Create(_alice);

        lotus.Should().BeOfType<Artifact>();
        lotus.HasType(CardType.Artifact).Should().BeTrue();
        lotus.Name.Should().Be("Gilded Lotus");
        lotus.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Gilded Lotus is NOT legendary");
        lotus.Owner.Should().BeSameAs(_alice);
        lotus.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void GildedLotus_HasPrintedManaCostFive()
    {
        var lotus = GildedLotusFactory.Create(_alice);

        var cost = lotus.ManaCostValue;
        cost.Generic.Should().Be(5);
        cost.TotalValue.Should().Be(5);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GildedLotus()
    {
        var card = NamedCardFactory.Create("Gilded Lotus", _alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be("Gilded Lotus");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Mana ability shape — three mana of one colour per WUBRG
    // -----------------------------------------------------------------------

    [Fact]
    public void GildedLotus_HasFiveManaAbilities_OnePerColor_EachProducesThree()
    {
        var lotus = GildedLotusFactory.Create(_alice);
        var mas = lotus.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one ManaAbility per WUBRG colour");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 3
                                     && m.ManaGenerated.TotalValue == 3);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 3
                                     && m.ManaGenerated.TotalValue == 3);
    }

    [Fact]
    public void GildedLotus_HasNoActivatedOrTriggeredAbilities()
    {
        var lotus = GildedLotusFactory.Create(_alice);

        lotus.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the only abilities are mana abilities");
        lotus.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Activation — taps + produces three of chosen colour (no sacrifice)
    // -----------------------------------------------------------------------

    [Fact]
    public void GildedLotus_Activate_ProducesThreeOfChosenColor_AndTaps_StaysOnBattlefield()
    {
        var lotus = GildedLotusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lotus);

        var mas = lotus.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue("lotus is untapped");
        }

        var red = mas.Single(m => m.ManaGenerated.Red == 3);
        var produced = red.Activate();

        produced.Red.Should().Be(3);
        produced.TotalValue.Should().Be(3);

        lotus.IsTapped.Should().BeTrue("activation taps the lotus");
        lotus.Zone.Should().Be(Majik.Core.Zones.ZoneType.Battlefield,
            "Gilded Lotus is NOT sacrificed — unlike Lotus Bloom it stays put");
        _alice.Zones.Battlefield.GetCards().Should().Contain(lotus);

        // CR 605.1b — only one "any one color" mode per tap: the others
        // are now un-activatable because the source is tapped.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "the {T} cost is already paid — no further activations until untap");
        }
    }

    [Fact]
    public void GildedLotus_ActivateViaActivator_CreditsManaPool()
    {
        var lotus = GildedLotusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lotus);

        var activator = new ManaAbilityActivator();
        var blue = lotus.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 3);

        _alice.ManaPool.Total.Should().Be(0);

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(3);
        _alice.ManaPool.Total.Should().Be(3);
        lotus.IsTapped.Should().BeTrue();
        lotus.Zone.Should().Be(Majik.Core.Zones.ZoneType.Battlefield);
    }

    [Fact]
    public void GildedLotus_CannotActivateWhenTapped()
    {
        var lotus = GildedLotusFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(lotus);
        lotus.Tap();

        foreach (var ma in lotus.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "the {T} cost cannot be paid by a tapped permanent");
        }
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void GildedLotus_Create_ThrowsOnNullOwner()
    {
        var act = () => GildedLotusFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
