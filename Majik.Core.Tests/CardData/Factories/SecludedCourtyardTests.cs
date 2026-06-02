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
/// Unit tests for <see cref="SecludedCourtyardFactory"/> (Dominaria United).
///
/// Secluded Courtyard is a near-twin of Unclaimed Territory — these tests
/// mirror <c>UnclaimedTerritoryTests</c>. The only oracle difference is the
/// any-colour mana may also be spent to activate an ability of a creature
/// source of the chosen type (captured in the SpendRestriction description;
/// payment-gate enforcement is deferred — see factory xmldoc).
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
public class SecludedCourtyardTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SecludedCourtyard_Identity()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        land.Name.Should().Be("Secluded Courtyard");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SecludedCourtyard_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Secluded Courtyard", _alice);

        card.Should().BeOfType<Land>("Secluded Courtyard is a Land");
        card.Name.Should().Be("Secluded Courtyard");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SecludedCourtyard_IsNotLegendary()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void SecludedCourtyard_IsNotBasic()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        // Non-basic ⇒ the BasicLandManaColors fallback in NamedCardFactory
        // doesn't attach extra mana — Secluded Courtyard wires its own six
        // abilities.
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ETB type choice
    // -----------------------------------------------------------------------

    [Fact]
    public void SecludedCourtyard_NoChooser_LeavesChosenTypeUnset()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        SecludedCourtyardFactory.GetChosenType(land).Should().BeNull(
            "the single-arg overload leaves the chosen-type slot empty");
    }

    [Fact]
    public void SecludedCourtyard_StoresChosenType_FromTypeChooser()
    {
        var land = SecludedCourtyardFactory.Create(_alice, _ => CardSubtype.Wizard);

        SecludedCourtyardFactory.GetChosenType(land).Should().Be(CardSubtype.Wizard,
            "the ETB choice is captured at factory-build time");
    }

    [Fact]
    public void SecludedCourtyard_TypeChooser_ReceivesController()
    {
        Player? captured = null;
        var land = SecludedCourtyardFactory.Create(_alice, p =>
        {
            captured = p;
            return CardSubtype.Goblin;
        });

        captured.Should().BeSameAs(_alice,
            "the chooser is invoked with the land's controller");
        SecludedCourtyardFactory.GetChosenType(land).Should().Be(CardSubtype.Goblin);
    }

    [Fact]
    public void SecludedCourtyard_ChosenTypeIsPerCard()
    {
        var types = new[] { CardSubtype.Wizard, CardSubtype.Goblin };
        var lands = types
            .Select(t => SecludedCourtyardFactory.Create(_alice, _ => t))
            .ToList();

        SecludedCourtyardFactory.GetChosenType(lands[0]).Should().Be(CardSubtype.Wizard);
        SecludedCourtyardFactory.GetChosenType(lands[1]).Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void SecludedCourtyard_HasSixManaAbilities()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one {C} + one per WUBRG colour");
    }

    [Fact]
    public void SecludedCourtyard_HasColorlessManaAbility()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        // {C} parses as +1 generic (the {C} bucket is not separated yet —
        // same posture as Unclaimed Territory). The colourless mana ability
        // is identifiable as the unique one that produces no coloured pips
        // and 1 generic.
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
    public void SecludedCourtyard_HasOneManaAbilityPerWUBRG()
    {
        var land = SecludedCourtyardFactory.Create(_alice);
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
    public void SecludedCourtyard_ColouredManaAbilities_CarrySpendRestriction()
    {
        var land = SecludedCourtyardFactory.Create(_alice);
        var coloured = land.Abilities.OfType<ManaAbility>()
            .Where(m =>
                m.ManaGenerated.White == 1 ||
                m.ManaGenerated.Blue == 1 ||
                m.ManaGenerated.Black == 1 ||
                m.ManaGenerated.Red == 1 ||
                m.ManaGenerated.Green == 1)
            .ToList();

        coloured.Should().OnlyContain(m => m.SpendRestriction != null,
            "the any-colour mana may be spent only to cast a creature spell of the chosen type "
            + "or activate an ability of a creature source of that type");
    }

    [Fact]
    public void SecludedCourtyard_ColorlessManaAbility_IsUnrestricted()
    {
        var land = SecludedCourtyardFactory.Create(_alice);
        var colourless = land.Abilities.OfType<ManaAbility>()
            .Single(m =>
                m.ManaGenerated.Generic == 1 &&
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0);

        colourless.SpendRestriction.Should().BeNull(
            "{T}: Add {C} is unrestricted — only the any-colour ability is gated");
    }

    [Fact]
    public void SecludedCourtyard_AllManaAbilities_AreActivatable_WhenUntapped()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeTrue(
                "an untapped land's mana abilities should be activatable");
        }
    }

    [Fact]
    public void SecludedCourtyard_ManaAbilities_NotActivatable_WhenTapped()
    {
        var land = SecludedCourtyardFactory.Create(_alice);
        land.Tap();

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeFalse(
                "a tapped land's {T}-cost mana abilities are not activatable");
        }
    }

    [Fact]
    public void SecludedCourtyard_HasNoTriggeredAbilities()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "v1 captures the ETB choice eagerly — no triggered abilities");
    }

    [Fact]
    public void SecludedCourtyard_HasNoNonManaActivatedAbilities()
    {
        var land = SecludedCourtyardFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Secluded Courtyard has only mana abilities");
    }
}
