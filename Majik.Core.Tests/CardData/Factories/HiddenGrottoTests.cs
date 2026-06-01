using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HiddenGrottoFactory"/> (Foundations).
///
/// Land. Oracle text:
///   "When this land enters, surveil 1.
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Mechanically identical to Crystal Grotto except the ETB trigger surveils
/// 1 (CR 701.50) rather than scrying. Hidden Grotto does NOT enter tapped.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller, non-Basic).
/// - {T}: Add {C} — one cost-free colourless mana ability (CR 605.1a).
/// - {1}, {T}: Add one mana of any color — modelled as five per-colour
///   ManaAbility slots, each carrying the {1} additional mana cost. So six
///   mana abilities total.
/// - One battlefield-active ETB triggered ability that surveils 1.
/// - Surveil-1 fall-back (no agent) puts the peeked card in the graveyard.
/// - The {1} mana cost gates the any-colour abilities (CR 605.1).
/// </summary>
public class HiddenGrottoTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void HiddenGrotto_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", _alice);

        land.Name.Should().Be("Hidden Grotto");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HiddenGrotto_IsNotBasic()
    {
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HiddenGrotto()
    {
        var card = NamedCardFactory.Create("Hidden Grotto", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hidden Grotto");
        // One {C} + five any-colour (WUBRG) = six mana abilities.
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(6);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void HiddenGrotto_HasColorlessManaAbility_NoCost()
    {
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", _alice);

        // {T}: Add {C}. {C} parses to one generic-mana pip; the colourless
        // ability has no WUBRG component. It must be activatable with an
        // empty pool (no {1} cost rider).
        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);

        colorless.CanActivate().Should().BeTrue(
            "{T}: Add {C} has no extra mana cost");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void HiddenGrotto_HasAnyColorManaAbility_PerColor(string color)
    {
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", _alice);
        var match = ManaCost.Parse(color);

        land.Abilities.OfType<ManaAbility>().Should().ContainSingle(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green,
            $"Hidden Grotto can add {{{color}}} via its any-colour mode");
    }

    [Fact]
    public void HiddenGrotto_AnyColorAbility_GatedByOneManaCost()
    {
        // CR 605.1 — the {1} is part of the activation cost. With an empty
        // pool the any-colour modes can't be activated; once {1} is in the
        // pool they can. Mirrors the signet / filter-land posture.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", alice);
        var white = FindAnyColorAbility(land, "W");

        white.CanActivate().Should().BeFalse(
            "the {1} additional cost can't be paid from an empty pool");

        alice.AddManaToPool(ManaCost.Parse("1"));
        white.CanActivate().Should().BeTrue(
            "with {1} available the any-colour mode is payable");
    }

    [Fact]
    public void HiddenGrotto_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Hidden Grotto", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void HiddenGrotto_EtbEffect_SurveilsOne_DefaultsTopCardToGraveyard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Hidden Grotto", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → surveil fall-back puts the single peeked
        // card (Top) into the graveyard; the previously-second card is now
        // on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second });
        alice.Zones.Graveyard.GetCards().Should().Equal(new[] { top });
    }

    [Fact]
    public void HiddenGrotto_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var land = (Land)NamedCardFactory.Create("Hidden Grotto", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    private static ManaAbility FindAnyColorAbility(Land land, string color)
    {
        var match = ManaCost.Parse(color);
        return land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            (match.White + match.Blue + match.Black + match.Red + match.Green) == 1);
    }

    private static bool IsColorlessOnly(ManaAbility m) =>
        m.ManaGenerated.White == 0 &&
        m.ManaGenerated.Blue == 0 &&
        m.ManaGenerated.Black == 0 &&
        m.ManaGenerated.Red == 0 &&
        m.ManaGenerated.Green == 0 &&
        m.ManaGenerated.Generic == 1;
}
