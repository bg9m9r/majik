using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CavernOfSoulsFactory"/> (Avacyn Restored).
///
/// Covers:
/// - Identity (name, Land type, owner/controller).
/// - NamedCardFactory dispatch.
/// - ETB type choice: chosen subtype stored and retrievable.
/// - {T}: Add {C} — 1 colourless mana ability (lands as +1 generic per
///   <see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>).
/// - {T}: Add one mana of any color — 5 mana abilities (one per WUBRG).
/// - Mana abilities are activatable (untapped land, default check).
/// </summary>
public class CavernOfSoulsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CavernOfSouls_Identity()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        land.Name.Should().Be("Cavern of Souls");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CavernOfSouls_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Cavern of Souls", _alice);

        card.Should().BeOfType<Land>("Cavern of Souls is a Land");
        card.Name.Should().Be("Cavern of Souls");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void CavernOfSouls_IsNotLegendary()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void CavernOfSouls_IsNotBasic()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        // Non-basic ⇒ the BasicLandManaColors fallback in NamedCardFactory
        // doesn't attach extra mana — Cavern wires its own six abilities.
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB type choice
    // -----------------------------------------------------------------------

    [Fact]
    public void CavernOfSouls_NoChooser_LeavesChosenTypeUnset()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        CavernOfSoulsFactory.GetChosenType(land).Should().BeNull(
            "the single-arg overload leaves the chosen-type slot empty");
    }

    [Fact]
    public void CavernOfSouls_StoresChosenType_FromTypeChooser()
    {
        var land = CavernOfSoulsFactory.Create(_alice, _ => CardSubtype.Wizard);

        CavernOfSoulsFactory.GetChosenType(land).Should().Be(CardSubtype.Wizard,
            "the ETB choice is captured at factory-build time");
    }

    [Fact]
    public void CavernOfSouls_TypeChooser_ReceivesController()
    {
        Player? captured = null;
        var land = CavernOfSoulsFactory.Create(_alice, p =>
        {
            captured = p;
            return CardSubtype.Goblin;
        });

        captured.Should().BeSameAs(_alice,
            "the chooser is invoked with the land's controller");
        CavernOfSoulsFactory.GetChosenType(land).Should().Be(CardSubtype.Goblin);
    }

    [Fact]
    public void CavernOfSouls_ChosenTypeIsPerCard()
    {
        var caverns = new[] { CardSubtype.Wizard, CardSubtype.Goblin };
        var lands = caverns
            .Select(t => CavernOfSoulsFactory.Create(_alice, _ => t))
            .ToList();

        CavernOfSoulsFactory.GetChosenType(lands[0]).Should().Be(CardSubtype.Wizard);
        CavernOfSoulsFactory.GetChosenType(lands[1]).Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void CavernOfSouls_HasSixManaAbilities()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one {C} + one per WUBRG colour");
    }

    [Fact]
    public void CavernOfSouls_HasColorlessManaAbility()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        // {C} parses as +1 generic (see ManaCost.cs:170 — {C} bucket not
        // separated yet). The colourless mana ability is identifiable as
        // the unique one that produces no coloured pips and 1 generic.
        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m =>
                m.ManaGenerated.Generic == 1 &&
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0,
                "{T}: Add {C} — one colourless mana ability");
    }

    [Fact]
    public void CavernOfSouls_HasOneManaAbilityPerWUBRG()
    {
        var land = CavernOfSoulsFactory.Create(_alice);
        var coloured = land.Abilities.OfType<ManaAbility>()
            .Where(m =>
                m.ManaGenerated.White == 1 ||
                m.ManaGenerated.Blue == 1 ||
                m.ManaGenerated.Black == 1 ||
                m.ManaGenerated.Red == 1 ||
                m.ManaGenerated.Green == 1)
            .ToList();

        coloured.Should().HaveCount(5,
            "{T}: Add one mana of any color — one ManaAbility per WUBRG");
        coloured.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        coloured.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    [Fact]
    public void CavernOfSouls_AllManaAbilities_AreActivatable_WhenUntapped()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeTrue(
                "an untapped land's mana abilities should be activatable");
        }
    }

    [Fact]
    public void CavernOfSouls_ManaAbilities_NotActivatable_WhenTapped()
    {
        var land = CavernOfSoulsFactory.Create(_alice);
        land.Tap();

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeFalse(
                "a tapped land's {T}-cost mana abilities are not activatable");
        }
    }

    [Fact]
    public void CavernOfSouls_HasNoTriggeredAbilities()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "v1 captures the ETB choice eagerly — no triggered abilities");
    }

    [Fact]
    public void CavernOfSouls_HasNoNonManaActivatedAbilities()
    {
        var land = CavernOfSoulsFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Cavern of Souls has only mana abilities");
    }
}
