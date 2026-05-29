using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ManaConfluenceFactory"/> — Mana Confluence (Journey
/// into Nyx). Oracle text (verified against Scryfall):
///   "{T}, Pay 1 life: Add one mana of any color."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — same any-colour fan-out
///   shape as Aether Hub's coloured modes, with NO {C} mode (unlike Aether
///   Hub / pain lands).
/// - Each coloured activation pays 1 life (CR 120.6 — "Pay N life") on top
///   of the {T} tap.
/// - Life-floor gate (CR 119.4 — "you can't pay life you don't have"):
///   activation is illegal at 1 life or below. Distinct from pain lands,
///   which deal damage and CAN drop you to 0.
/// - Tap-as-cost: a second coloured activation can't pay {T} once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class ManaConfluenceFactoryTests
{
    private const string CardName = "Mana Confluence";

    public static IEnumerable<object[]> AllColors => new[]
    {
        new object[] { "W" },
        new object[] { "U" },
        new object[] { "B" },
        new object[] { "R" },
        new object[] { "G" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaConfluence_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void ManaConfluence_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void ManaConfluence_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void ManaConfluence_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(CardName, alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(CardName);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaConfluence_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void ManaConfluence_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        // Mana Confluence has NO "{T}: Add {C}" mode (unlike Aether Hub /
        // pain lands): every mode produces one coloured mana.
        land.Abilities.OfType<ManaAbility>()
            .Should().NotContain(m =>
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0 &&
                m.ManaGenerated.Generic == 1);
    }

    [Fact]
    public void ManaConfluence_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ManaConfluence_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = ManaConfluenceFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"Mana Confluence can add {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // Pay 1 life cost (CR 120.6)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ManaConfluence_Activation_PaysOneLife(string color)
    {
        var alice = new Player("Alice", 20);
        var land = ManaConfluenceFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(19,
            $"adding {{{color}}} costs 1 life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void ManaConfluence_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = ManaConfluenceFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Life-floor gate (CR 119.4 — distinct from pain lands)
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaConfluence_CannotActivateAtOneLife()
    {
        // CR 119.4 — "Pay N life" is a cost you can't pay if it would
        // reduce your life total below 0. At 1 life, paying 1 life would
        // drop to 0, which IS allowed (0 is not below 0). So the floor is
        // life > 0: legal at 1, illegal at 0.
        var alice = new Player("Alice", 1);
        var land = ManaConfluenceFactory.Create(alice);
        var white = FindColoredAbility(land, "W");

        // At exactly 1 life, paying 1 life leaves 0 — that is payable.
        white.CanActivate().Should().BeTrue(
            "paying 1 life at 1 life is legal (drops to 0, not below)");

        white.Activate();
        alice.LifeTotal.Should().Be(0);
    }

    [Fact]
    public void ManaConfluence_CannotActivateAtZeroLife()
    {
        var alice = new Player("Alice", 0);
        var land = ManaConfluenceFactory.Create(alice);
        var white = FindColoredAbility(land, "W");

        white.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life with 0 life");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindColoredAbility(Land land, string color)
    {
        var match = ManaCost.Parse(color);
        return land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            m.ManaGenerated.Generic == match.Generic);
    }
}
