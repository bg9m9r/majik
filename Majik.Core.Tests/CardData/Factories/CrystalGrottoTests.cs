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
/// Unit tests for <see cref="CrystalGrottoFactory"/> (March of the Machine
/// Commander / Dominaria United precon staple).
///
/// Land. Oracle text:
///   "When this land enters, scry 1.
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Unlike the Theros scry-temples, Crystal Grotto does NOT enter tapped.
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, owner/controller, non-Basic).
/// - {T}: Add {C} — one cost-free colourless mana ability (CR 605.1a).
/// - {1}, {T}: Add one mana of any color — modelled as five per-colour
///   ManaAbility slots, each carrying the {1} additional mana cost, the
///   same WUBRG fan-out the engine uses for "any color" everywhere else
///   (Springleaf Drum, Aether Hub). So six mana abilities total.
/// - One battlefield-active ETB triggered ability that scries 1.
/// - Scry-1 fall-back (no agent) puts the peeked card on the bottom.
/// - The {1} mana cost gates the any-colour abilities (CR 605.1) — they
///   can't activate with an empty pool, but can once {1} is available.
/// </summary>
[Trait("Color", "C")]
public class CrystalGrottoTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CrystalGrotto_IsLand_WithCorrectName()
    {
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", _alice);

        land.Name.Should().Be("Crystal Grotto");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CrystalGrotto_IsNotBasic()
    {
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", _alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }
    [Fact]
    public void CrystalGrotto_HasColorlessManaAbility_NoCost()
    {
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", _alice);

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
    public void CrystalGrotto_HasAnyColorManaAbility_PerColor(string color)
    {
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", _alice);
        var match = ManaCost.Parse(color);

        land.Abilities.OfType<ManaAbility>().Should().ContainSingle(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green,
            $"Crystal Grotto can add {{{color}}} via its any-colour mode");
    }

    [Fact]
    public void CrystalGrotto_AnyColorAbility_GatedByOneManaCost()
    {
        // CR 605.1 — the {1} is part of the activation cost. With an empty
        // pool the any-colour modes can't be activated; once {1} is in the
        // pool they can. Mirrors the signet / filter-land posture.
        var alice = new Player("Alice", 20);
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", alice);
        var white = FindAnyColorAbility(land, "W");

        white.CanActivate().Should().BeFalse(
            "the {1} additional cost can't be paid from an empty pool");

        alice.AddManaToPool(ManaCost.Parse("1"));
        white.CanActivate().Should().BeTrue(
            "with {1} available the any-colour mode is payable");
    }

    [Fact]
    public void CrystalGrotto_EtbTrigger_IsBattlefieldActive()
    {
        var land = (Land)NamedCardFactory.Create("Crystal Grotto", _alice);
        var trigger = land.Abilities.OfType<TriggeredAbility>().Single();

        trigger.ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    [Fact]
    public void CrystalGrotto_EtbEffect_ScriesOne_DefaultsTopCardToBottom()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", ""); top.SetOwner(alice);
        var second = new Card("Second", ""); second.SetOwner(alice);
        foreach (var c in new[] { top, second })
        {
            alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var land = (Land)NamedCardFactory.Create("Crystal Grotto", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // No agent registered → fall-back puts the single peeked card (Top)
        // on the bottom; the previously-second card is now on top.
        alice.Zones.Library.GetCards().Should().Equal(new[] { second, top });
        alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void CrystalGrotto_EtbEffect_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var land = (Land)NamedCardFactory.Create("Crystal Grotto", alice);
        var etb = land.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Library.GetCards().Should().BeEmpty();
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
