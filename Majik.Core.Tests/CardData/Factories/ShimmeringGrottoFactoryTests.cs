using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="ShimmeringGrottoFactory"/> — Shimmering Grotto (a
/// functional reprint of Unknown Shores). Oracle text (verified against
/// Scryfall):
/// <code>
/// {T}: Add {C}.
/// {1}, {T}: Add one mana of any color.
/// </code>
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Land type, printed name, non-Basic, non-Legendary).
/// - Exactly 6 mana abilities: one {C} + five any-colour modes (WUBRG).
/// - The {C} mode is free (no {1} gate) and produces colourless mana.
/// - Each any-colour mode requires {1} in the pool (CR 605.1) — illegal on an
///   empty pool, legal once {1} is present, and pays the {1} on activation
///   while adding the chosen colour.
/// - Tap-as-cost: the two modes share the single {T}; once tapped, no mode can
///   activate.
/// </summary>
[Trait("Color", "C")]
public class ShimmeringGrottoFactoryTests
{
    private const string CardName = "Shimmering Grotto";

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
    public void ShimmeringGrotto_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void ShimmeringGrotto_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmeringGrotto_HasSixManaAbilities_OneColorlessPlusFiveAnyColor()
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(6,
            "one {C} mode + five any-colour modes (WUBRG)");
    }

    [Fact]
    public void ShimmeringGrotto_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void ShimmeringGrotto_HasColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        // {C} parses to one colourless unit (a subset of Generic) — 0 of every
        // WUBRG colour AND exactly 1 generic.
        land.Abilities.OfType<ManaAbility>()
            .Should().Contain(m =>
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0 &&
                m.ManaGenerated.Generic == 1,
                "{T}: Add {C}");
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ShimmeringGrotto_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = ShimmeringGrottoFactory.Create(alice);

        FindColoredAbility(land, color).Should().NotBeNull(
            $"Shimmering Grotto can add {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // {C} mode — free (no {1} gate)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmeringGrotto_ColorlessMode_ActivatesOnEmptyPool()
    {
        // The {C} mode carries no {1} rider — it is a plain {T} mana ability.
        var alice = new Player("Alice", 20);
        var land = ShimmeringGrottoFactory.Create(alice);
        var colorless = FindColorlessAbility(land);

        colorless.CanActivate().Should().BeTrue(
            "the {C} mode has no {1} additional cost");

        var activator = new ManaAbilityActivator();
        activator.ActivateManaAbility(colorless, alice);

        alice.ManaPool.Generic.Should().Be(1, "Add {C}");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Any-colour modes — {1} cost gate (CR 605.1)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ShimmeringGrotto_AnyColorMode_CannotActivateWithoutOneGenericMana(string color)
    {
        var alice = new Player("Alice", 20);
        var land = ShimmeringGrottoFactory.Create(alice);

        // Empty mana pool — the any-colour mode can't pay {1}.
        FindColoredAbility(land, color).CanActivate().Should().BeFalse(
            $"{{{color}}} requires {{1}} in the pool");
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ShimmeringGrotto_AnyColorMode_CanActivateWithOneGenericInPool(string color)
    {
        var alice = new Player("Alice", 20);
        var land = ShimmeringGrottoFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));

        FindColoredAbility(land, color).CanActivate().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void ShimmeringGrotto_AnyColorMode_PaysOneGeneric_AndAddsChosenColor(string color)
    {
        var alice = new Player("Alice", 20);
        var land = ShimmeringGrottoFactory.Create(alice);
        // Seed the pool with {1} (the any-colour cost).
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mode = FindColoredAbility(land, color);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, alice);

        var expected = ManaCost.Parse(color);
        alice.ManaPool.White.Should().Be(expected.White);
        alice.ManaPool.Blue.Should().Be(expected.Blue);
        alice.ManaPool.Black.Should().Be(expected.Black);
        alice.ManaPool.Red.Should().Be(expected.Red);
        alice.ManaPool.Green.Should().Be(expected.Green);
        alice.ManaPool.Generic.Should().Be(0,
            "the seed {1} was spent on the any-colour cost");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Shared {T} — both modes tap the single land
    // -----------------------------------------------------------------------

    [Fact]
    public void ShimmeringGrotto_OnceTapped_NoModeCanActivate()
    {
        var alice = new Player("Alice", 20);
        var land = ShimmeringGrottoFactory.Create(alice);
        alice.AddManaToPool(ManaCost.Parse("1"));

        // Activate the {C} mode first — taps the land.
        new ManaAbilityActivator().ActivateManaAbility(FindColorlessAbility(land), alice);
        land.IsTapped.Should().BeTrue();

        // Neither the {C} mode nor any any-colour mode can pay {T} now.
        FindColorlessAbility(land).CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
        FindColoredAbility(land, "W").CanActivate().Should().BeFalse(
            "the any-colour mode shares the same {T}");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindColorlessAbility(Land land) =>
        land.Abilities.OfType<ManaAbility>().Single(m =>
            m.ManaGenerated.White == 0 &&
            m.ManaGenerated.Blue == 0 &&
            m.ManaGenerated.Black == 0 &&
            m.ManaGenerated.Red == 0 &&
            m.ManaGenerated.Green == 0 &&
            m.ManaGenerated.Generic == 1);

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
