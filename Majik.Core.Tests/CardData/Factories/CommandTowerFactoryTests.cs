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
/// Tests for <see cref="CommandTowerFactory"/> — Command Tower (Commander
/// 2011 and many reprints). Oracle text (verified against Scryfall
/// 2026-06-02):
///   "{T}: Add one mana of any color in your commander's color identity."
///
/// <para>
/// Modeling posture (v1): Majik is a 1v1 constructed engine with no Commander
/// format / command zone / commander, so there is no commander colour
/// identity to read (CR 903.4). With no commander defined, the faithful
/// resolution of "any color in your commander's color identity" is the full
/// set of five colours — the same any-colour fan-out as
/// <see cref="CityOfBrassFactory"/> and <see cref="ManaConfluenceFactory"/>,
/// but with {T} as the SOLE activation cost: no pain (CR 120.3) and no life
/// payment (CR 119.4). Modelled as five WUBRG <see cref="ManaAbility"/>
/// instances (CR 605.1a); {C} is excluded — colorless is not a colour
/// (CR 105.1).
/// </para>
///
/// Covers:
/// - Identity (Land, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — any-colour fan-out, no
///   {C} mode.
/// - Each coloured activation produces that colour and taps the land, with
///   NO life cost / pain (distinguishing Command Tower from City of Brass /
///   Mana Confluence).
/// - No life-floor gate: activatable at 1 life with no life loss.
/// - Tap-as-cost: a second activation can't pay {T} once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class CommandTowerFactoryTests
{
    private const string CardName = "Command Tower";

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
    public void CommandTower_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void CommandTower_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void CommandTower_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void CommandTower_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void CommandTower_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

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
    public void CommandTower_HasNoActivatedAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void CommandTower_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = CommandTowerFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"Command Tower can add {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // Activation: produces colour, taps land, NO life cost / pain
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllColors))]
    public void CommandTower_Activation_ProducesColor_NoLifeCost(string color)
    {
        var alice = new Player("Alice", 20);
        var land = CommandTowerFactory.Create(alice);
        var ability = FindColoredAbility(land, color);
        var expected = ManaCost.Parse(color);

        var produced = ability.Activate();

        produced.ToString().Should().Be(expected.ToString(),
            $"Command Tower taps for {{{color}}}");
        land.IsTapped.Should().BeTrue("{T} is the activation cost");
        alice.LifeTotal.Should().Be(20,
            "Command Tower has no pain / life cost (unlike City of Brass / Mana Confluence)");
    }

    [Fact]
    public void CommandTower_HasNoLifeFloorGate()
    {
        // Unlike Mana Confluence (CR 119.4) there is no "Pay 1 life"; {T} is
        // the sole cost. Activatable at 1 life with no life loss.
        var alice = new Player("Alice", 1);
        var land = CommandTowerFactory.Create(alice);
        var white = FindColoredAbility(land, "W");

        white.CanActivate().Should().BeTrue(
            "Command Tower's only cost is {T} — no life-floor gate");

        white.Activate();
        alice.LifeTotal.Should().Be(1, "no life is paid");
    }

    [Fact]
    public void CommandTower_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = CommandTowerFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void CommandTower_DispatchesViaNamedFactory()
    {
        var alice = new Player("Alice", 20);

        var land = (Land)NamedCardFactory.Create(CardName, alice);

        land.Name.Should().Be(CardName);
        land.HasType(CardType.Land).Should().BeTrue();
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5);
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
