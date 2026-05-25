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
/// Tests for <see cref="PainLandCycleFactory"/> — the 10-card Ice Age +
/// Apocalypse painland cycle.
///
/// Covers, per cycle member:
/// - Identity (Land type, printed name, owner/controller wiring,
///   non-Basic).
/// - Mana abilities: exactly 3 (one {C}, one per coloured option).
/// - {C} ability has no pain rider — activating doesn't lose life.
/// - Coloured abilities apply the pain rider — activating loses 1 life.
/// - Tap-as-cost: the second activation can't pay {T} once tapped.
/// - No life-floor gate: pain damage can drop life to 0 / below (CR 119.4
///   does NOT block pain-rider activation, distinct from Horizon Canopy
///   "Pay 1 life").
/// - Dispatch through <see cref="NamedCardFactory"/> resolves each printed
///   name to the parametric Create overload.
/// </summary>
public class PainLandCycleFactoryTests
{
    /// <summary>
    /// All 10 painlands with their canonical coloured option pair.
    /// First five: Ice Age allied (WU, UB, BR, RG, GW).
    /// Last five: Apocalypse enemy (RW, WB, BG, UR, GU).
    /// </summary>
    public static IEnumerable<object[]> AllPainLands => new[]
    {
        new object[] { "Adarkar Wastes",    "W", "U" },
        new object[] { "Underground River", "U", "B" },
        new object[] { "Sulfurous Springs", "B", "R" },
        new object[] { "Karplusan Forest",  "R", "G" },
        new object[] { "Brushland",         "G", "W" },
        new object[] { "Battlefield Forge", "R", "W" },
        new object[] { "Caves of Koilos",   "W", "B" },
        new object[] { "Llanowar Wastes",   "B", "G" },
        new object[] { "Shivan Reef",       "U", "R" },
        new object[] { "Yavimaya Coast",    "G", "U" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_IsLand_WithCorrectName(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(cardName);
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_OwnerAndControllerAreSet(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_IsNotBasic_AndNotLegendary(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_Dispatch_ResolvesViaNamedCardFactory(string cardName, string a, string b)
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
    [MemberData(nameof(AllPainLands))]
    public void PainLand_HasThreeManaAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one {C} + one per coloured option (A, B)");
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_HasNoActivatedOrTriggeredAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "painlands have no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "painlands have no triggered abilities");
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_HasColorlessManaAbility(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });

        // {C} parses to one generic mana — distinguishing it from the
        // two coloured abilities is "has 0 of every WUBRG colour".
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

    // -----------------------------------------------------------------------
    // Pain rider — coloured activations lose 1 life
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_ColoredA_Activation_LosesOneLife(string cardName, string a, string b)
    {
        _ = b;
        var alice = new Player("Alice", 20);
        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });
        var coloredA = FindColoredAbility(land, a);

        coloredA.Activate();

        alice.LifeTotal.Should().Be(19,
            $"{cardName}: tapping for {{{a}}} deals 1 damage to you");
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_ColoredB_Activation_LosesOneLife(string cardName, string a, string b)
    {
        _ = a;
        var alice = new Player("Alice", 20);
        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });
        var coloredB = FindColoredAbility(land, b);

        coloredB.Activate();

        alice.LifeTotal.Should().Be(19,
            $"{cardName}: tapping for {{{b}}} deals 1 damage to you");
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_Colorless_Activation_DoesNotLoseLife(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });
        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);

        colorless.Activate();

        alice.LifeTotal.Should().Be(20,
            $"{cardName}: the {{T}}: Add {{C}} mode does NOT carry a pain rider");
        land.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllPainLands))]
    public void PainLand_CannotActivateColoredWhenTapped(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var land = PainLandCycleFactory.Create(alice, new[] { cardName, a, b });
        var coloredA = FindColoredAbility(land, a);
        var coloredB = FindColoredAbility(land, b);

        coloredA.Activate();

        coloredB.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void PainLand_CanActivateColoredAtOneLife_DropsToZero()
    {
        // Distinct from Horizon Canopy: CR 119.4's "you can't pay life
        // you don't have" gates "Pay X life" costs only. Pain lands deal
        // damage, which reduces life — there's no life-floor activation
        // gate. Activating at 1 life is legal; SBAs then handle loss.
        var alice = new Player("Alice", 1);
        var land = PainLandCycleFactory.Create(alice, new[] { "Adarkar Wastes", "W", "U" });
        var white = FindColoredAbility(land, "W");

        white.CanActivate().Should().BeTrue(
            "pain damage is not a 'pay life' cost — no life-floor gate (CR 119.4 doesn't apply)");

        white.Activate();
        alice.LifeTotal.Should().Be(0,
            "pain damage can deal lethal damage to you; SBAs handle the loss");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void PainLand_Create_ThrowsOnNullOwner()
    {
        var act = () => PainLandCycleFactory.Create(null!, new[] { "Adarkar Wastes", "W", "U" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void PainLand_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => PainLandCycleFactory.Create(alice, new[] { "Adarkar Wastes", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*PainLandCycleFactory needs args*");
    }

    [Fact]
    public void PainLand_FallbackOverload_BuildsAdarkarWastes()
    {
        var alice = new Player("Alice", 20);

        var land = PainLandCycleFactory.Create(alice);

        land.Name.Should().Be("Adarkar Wastes");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3);
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
            m.ManaGenerated.Generic == match.Generic &&
            // Distinguish from the {C} ability: a coloured ability has
            // exactly one WUBRG slot populated.
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
