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
/// Unit tests for <see cref="EldraziTempleFactory"/> (Rise of the Eldrazi).
///
/// Covers:
/// - Identity (name, Land type, owner/controller, non-legendary, non-basic).
/// - NamedCardFactory dispatch.
/// - Two <see cref="ManaAbility"/> instances:
///     * One {T}: Add {C} producing 1 generic.
///     * One {T}: Add {C}{C} producing 2 generic.
/// - Mana abilities activatable when untapped / blocked when tapped.
/// - Spend-restriction tag deferred (see factory xmldoc) — no triggered or
///   non-mana activated abilities present.
/// </summary>
public class EldraziTempleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziTemple_Identity()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.Name.Should().Be("Eldrazi Temple");
        land.HasType(CardType.Land).Should().BeTrue();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EldraziTemple_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Eldrazi Temple", _alice);

        card.Should().BeOfType<Land>("Eldrazi Temple is a Land");
        card.Name.Should().Be("Eldrazi Temple");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void EldraziTemple_IsNotLegendary()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void EldraziTemple_IsNotBasic()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Eldrazi Temple is a non-basic land — the BasicLandManaColors " +
            "fallback in NamedCardFactory must not attach extra mana");
    }

    // -----------------------------------------------------------------------
    // Mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziTemple_HasTwoManaAbilities()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one {T}: Add {C} + one {T}: Add {C}{C}");
    }

    [Fact]
    public void EldraziTemple_HasOneColorlessManaAbility()
    {
        var land = EldraziTempleFactory.Create(_alice);

        // {C} parses as +1 generic (see ManaCost.cs:170). The single-{C}
        // ability is identifiable as the one producing exactly 1 generic
        // and no coloured pips.
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
    public void EldraziTemple_HasOneDoubleColorlessManaAbility()
    {
        var land = EldraziTempleFactory.Create(_alice);

        // {C}{C} parses as +2 generic — same bucket as {C}, doubled.
        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m =>
                m.ManaGenerated.Generic == 2 &&
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0,
                "{T}: Add {C}{C} — one double-colourless mana ability");
    }

    [Fact]
    public void EldraziTemple_AllManaAbilities_AreActivatable_WhenUntapped()
    {
        var land = EldraziTempleFactory.Create(_alice);

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeTrue(
                "an untapped land's {T}-cost mana abilities should be activatable");
        }
    }

    [Fact]
    public void EldraziTemple_ManaAbilities_NotActivatable_WhenTapped()
    {
        var land = EldraziTempleFactory.Create(_alice);
        land.Tap();

        foreach (var m in land.Abilities.OfType<ManaAbility>())
        {
            m.CanActivate().Should().BeFalse(
                "a tapped land's {T}-cost mana abilities are not activatable");
        }
    }

    // -----------------------------------------------------------------------
    // Spend-restriction posture — see factory xmldoc.
    //
    // The {C}{C} ability stamps an Eldrazi-only SpendRestriction; the payment
    // gate now ENFORCES it on colorless mana (see
    // SpendRestrictionProvenanceGateTests). These tests pin the raw activation
    // shape (produces 2 generic / colorless when tapped); the gate-enforcement
    // assertions live in SpendRestrictionProvenanceGateTests.
    // -----------------------------------------------------------------------

    [Fact]
    public void EldraziTemple_DoubleColorlessAbility_ActivatesAsTwoGeneric()
    {
        // CR 605.3 / 605.4 — mana abilities produce mana when activated. The
        // raw Activate() output is 2 generic / colorless; the Eldrazi-only
        // spend-restriction is applied by the payment gate at spend time
        // (CR 106.4), not at production, so the produced ManaCost itself is
        // plain colorless here.
        var land = EldraziTempleFactory.Create(_alice);
        var doubleAbility = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 2);

        var produced = doubleAbility.Activate();

        produced.Generic.Should().Be(2);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("activating a {T}-cost mana ability taps the source");
    }

    [Fact]
    public void EldraziTemple_HasNoTriggeredAbilities()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Eldrazi Temple has no triggered abilities");
    }

    [Fact]
    public void EldraziTemple_HasNoNonManaActivatedAbilities()
    {
        var land = EldraziTempleFactory.Create(_alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Eldrazi Temple has only mana abilities");
    }
}
