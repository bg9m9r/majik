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
/// Tests for <see cref="FilterLandCycleFactory"/> — the 10-card
/// Shadowmoor + Eventide filter-land cycle.
///
/// Covers, per cycle member:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic, non-Legendary).
/// - Mana abilities: exactly 4 (one {C} + three filter modes
///   {A}{A}, {A}{B}, {B}{B}).
/// - Filter modes require {1} in the mana pool — activating without
///   the {1} returns false from CanActivate.
/// - Activating a filter mode pays {1} from the pool and adds the
///   produced two-pip mana.
/// - Tap-as-cost: a tapped land can't activate any of its mana abilities.
/// - Dispatch through <see cref="NamedCardFactory"/> resolves each printed
///   name to the parametric Create overload.
/// </summary>
public class FilterLandCycleFactoryTests
{
    /// <summary>
    /// All 10 filter lands with their canonical coloured option pair.
    /// First five: Shadowmoor allied (WU, UB, BR, RG, GW).
    /// Last five: Eventide enemy (UR, BG, WB, RW, GU).
    /// </summary>
    public static IEnumerable<object[]> AllFilterLands => new[]
    {
        new object[] { "Mystic Gate",      "W", "U" },
        new object[] { "Sunken Ruins",     "U", "B" },
        new object[] { "Graven Cairns",    "B", "R" },
        new object[] { "Fire-Lit Thicket", "R", "G" },
        new object[] { "Wooded Bastion",   "G", "W" },
        new object[] { "Cascade Bluffs",   "U", "R" },
        new object[] { "Twilight Mire",    "B", "G" },
        new object[] { "Fetid Heath",      "W", "B" },
        new object[] { "Rugged Prairie",   "R", "W" },
        new object[] { "Flooded Grove",    "G", "U" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_IsLand_WithCorrectName(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(cardName);
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_OwnerAndControllerAreSet(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_IsNotBasic_AndNotLegendary(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_Dispatch_ResolvesViaNamedCardFactory(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(cardName, alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(cardName);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_HasFourManaAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(4,
            "one {C} + three filter modes ({A}{A}, {A}{B}, {B}{B})");
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_HasNoActivatedOrTriggeredAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "filter lands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "filter lands have no triggered abilities");
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_HasColorlessManaAbility(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        // {C} parses to one generic mana — distinguishing it from the
        // three filter abilities is "has 0 of every WUBRG colour AND
        // exactly 1 generic".
        land.Abilities.OfType<ManaAbility>()
            .Should().Contain(m =>
                m.ManaGenerated.White == 0 &&
                m.ManaGenerated.Blue == 0 &&
                m.ManaGenerated.Black == 0 &&
                m.ManaGenerated.Red == 0 &&
                m.ManaGenerated.Green == 0 &&
                m.ManaGenerated.Generic == 1,
                $"{cardName} has a {{T}}: Add {{C}} mana ability");
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_HasAllThreeFilterModes(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        // Each filter mode produces a two-pip combo.
        FindFilterMode(land, a + a).Should().NotBeNull($"{cardName}: {{{a}}}{{{a}}} mode");
        FindFilterMode(land, a + b).Should().NotBeNull($"{cardName}: {{{a}}}{{{b}}} mode");
        FindFilterMode(land, b + b).Should().NotBeNull($"{cardName}: {{{b}}}{{{b}}} mode");
    }

    // -----------------------------------------------------------------------
    // Filter mode — {1} cost gate
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_FilterModes_CannotActivateWithoutOneGenericMana(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });

        // Empty mana pool — none of the filter modes can pay {1}.
        FindFilterMode(land, a + a)!.CanActivate().Should().BeFalse(
            $"{cardName}: {{{a}}}{{{a}}} requires {{1}} in the pool");
        FindFilterMode(land, a + b)!.CanActivate().Should().BeFalse(
            $"{cardName}: {{{a}}}{{{b}}} requires {{1}} in the pool");
        FindFilterMode(land, b + b)!.CanActivate().Should().BeFalse(
            $"{cardName}: {{{b}}}{{{b}}} requires {{1}} in the pool");
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_FilterModes_CanActivateWithOneGenericInPool(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.AddManaToPool(ManaCost.Parse("1"));

        FindFilterMode(land, a + a)!.CanActivate().Should().BeTrue();
        FindFilterMode(land, a + b)!.CanActivate().Should().BeTrue();
        FindFilterMode(land, b + b)!.CanActivate().Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_FilterModeAA_Activation_PaysOneGeneric_AndAddsTwoPips(string cardName, string a, string b)
    {
        _ = b;
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        // Seed pool with {1} (the filter cost) — coloured mana would also
        // pay {1}, but using {1} keeps the post-pay pool check unambiguous.
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mode = FindFilterMode(land, a + a)!;
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, alice);

        // {1} consumed; two pips of A added to the pool by the activator.
        var expected = ManaCost.Parse(a + a);
        alice.ManaPool.White.Should().Be(expected.White);
        alice.ManaPool.Blue.Should().Be(expected.Blue);
        alice.ManaPool.Black.Should().Be(expected.Black);
        alice.ManaPool.Red.Should().Be(expected.Red);
        alice.ManaPool.Green.Should().Be(expected.Green);
        alice.ManaPool.Generic.Should().Be(0,
            $"{cardName}: the seed {{1}} was spent on the filter cost");
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_FilterModeAB_Activation_PaysOneGeneric_AndAddsTwoPips(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mode = FindFilterMode(land, a + b)!;
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, alice);

        var expected = ManaCost.Parse(a + b);
        alice.ManaPool.White.Should().Be(expected.White);
        alice.ManaPool.Blue.Should().Be(expected.Blue);
        alice.ManaPool.Black.Should().Be(expected.Black);
        alice.ManaPool.Red.Should().Be(expected.Red);
        alice.ManaPool.Green.Should().Be(expected.Green);
        alice.ManaPool.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_FilterModeBB_Activation_PaysOneGeneric_AndAddsTwoPips(string cardName, string a, string b)
    {
        _ = a;
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.AddManaToPool(ManaCost.Parse("1"));
        var mode = FindFilterMode(land, b + b)!;
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(mode, alice);

        var expected = ManaCost.Parse(b + b);
        alice.ManaPool.White.Should().Be(expected.White);
        alice.ManaPool.Blue.Should().Be(expected.Blue);
        alice.ManaPool.Black.Should().Be(expected.Black);
        alice.ManaPool.Red.Should().Be(expected.Red);
        alice.ManaPool.Green.Should().Be(expected.Green);
        alice.ManaPool.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_Colorless_Activation_DoesNotRequireOneGeneric(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);
        var activator = new ManaAbilityActivator();

        colorless.CanActivate().Should().BeTrue(
            $"{cardName}: the {{T}}: Add {{C}} mode does NOT carry the {{1}} rider");

        activator.ActivateManaAbility(colorless, alice);

        alice.ManaPool.Generic.Should().Be(1,
            $"{cardName}: {{T}}: Add {{C}} produces 1 colourless / generic");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost — tapped land cannot activate
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFilterLands))]
    public void FilterLand_CannotActivateAnyModeWhenTapped(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = FilterLandCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.AddManaToPool(ManaCost.Parse("1"));
        // Pre-tap by activating the {C} mode (no {1} cost — leaves the
        // seeded {1} in the pool so we know the filter-mode rejection
        // below is from the tap state, not from a missing payment).
        var activator = new ManaAbilityActivator();
        activator.ActivateManaAbility(
            land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly), alice);
        land.IsTapped.Should().BeTrue();
        alice.ManaPool.Generic.Should().Be(2, "seed {1} + {C} from the colourless activation");

        FindFilterMode(land, a + a)!.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
        FindFilterMode(land, a + b)!.CanActivate().Should().BeFalse();
        FindFilterMode(land, b + b)!.CanActivate().Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void FilterLand_Create_ThrowsOnNullOwner()
    {
        var act = () => FilterLandCycleFactory.Create(null!, new[] { "Mystic Gate", "W", "U" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void FilterLand_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => FilterLandCycleFactory.Create(alice, new[] { "Mystic Gate", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*FilterLandCycleFactory needs args*");
    }

    [Fact]
    public void FilterLand_FallbackOverload_BuildsMysticGate()
    {
        var alice = new Player("Alice", 20);

        var land = FilterLandCycleFactory.Create(alice);

        land.Name.Should().Be("Mystic Gate");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(4);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Locate the filter-mode <see cref="ManaAbility"/> producing exactly
    /// the two-pip combo encoded by <paramref name="pips"/> (e.g. "WU"
    /// matches the {W}{U} mode). Returns <c>null</c> if no such ability
    /// exists. Filter modes always add 0 generic / colourless mana —
    /// that's how we distinguish them from the {C} ability.
    /// </summary>
    private static ManaAbility? FindFilterMode(Land land, string pips)
    {
        var match = ManaCost.Parse(pips);
        return land.Abilities.OfType<ManaAbility>().SingleOrDefault(m =>
            m.ManaGenerated.White == match.White &&
            m.ManaGenerated.Blue == match.Blue &&
            m.ManaGenerated.Black == match.Black &&
            m.ManaGenerated.Red == match.Red &&
            m.ManaGenerated.Green == match.Green &&
            m.ManaGenerated.Generic == 0 &&
            (match.White + match.Blue + match.Black + match.Red + match.Green) == 2);
    }

    private static bool IsColorlessOnly(ManaAbility m) =>
        m.ManaGenerated.White == 0 &&
        m.ManaGenerated.Blue == 0 &&
        m.ManaGenerated.Black == 0 &&
        m.ManaGenerated.Red == 0 &&
        m.ManaGenerated.Green == 0 &&
        m.ManaGenerated.Generic == 1;
}
