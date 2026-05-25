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
/// Tests for <see cref="TalismanCycleFactory"/> — the 10-card Mirrodin +
/// Mirrodin Besieged Talisman cycle.
///
/// Covers, per cycle member:
/// - Identity (Artifact type, {2} mana cost, printed name, owner/controller
///   wiring).
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
public class TalismanCycleFactoryTests
{
    /// <summary>
    /// All 10 talismans with their canonical coloured option pair.
    /// First five: Mirrodin allied (WU, UB, BR, RG, GW).
    /// Last five: Mirrodin Besieged enemy (WB, GU, BG, WR, UR).
    /// </summary>
    public static IEnumerable<object[]> AllTalismans => new[]
    {
        new object[] { "Talisman of Progress",   "W", "U" },
        new object[] { "Talisman of Dominance",  "U", "B" },
        new object[] { "Talisman of Indulgence", "B", "R" },
        new object[] { "Talisman of Impulse",    "R", "G" },
        new object[] { "Talisman of Unity",      "G", "W" },
        new object[] { "Talisman of Hierarchy",  "W", "B" },
        new object[] { "Talisman of Curiosity",  "G", "U" },
        new object[] { "Talisman of Resilience", "B", "G" },
        new object[] { "Talisman of Conviction", "W", "R" },
        new object[] { "Talisman of Creativity", "U", "R" },
    };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_IsArtifact_TwoCost_WithCorrectName(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });

        t.Should().BeOfType<Artifact>();
        t.HasType(CardType.Artifact).Should().BeTrue();
        t.Name.Should().Be(cardName);
        t.ManaCost.Should().Be("{2}");
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_OwnerAndControllerAreSet(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });

        t.Owner.Should().BeSameAs(alice);
        t.Controller.Should().BeSameAs(alice);
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_Dispatch_ResolvesViaNamedCardFactory(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(cardName, alice);

        card.Should().BeAssignableTo<Artifact>();
        card.Name.Should().Be(cardName);
        card.ManaCost.Should().Be("{2}");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_HasThreeManaAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });

        t.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one {C} + one per coloured option (A, B)");
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_HasNoActivatedOrTriggeredAbilities(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });

        t.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "talismans have no non-mana activated abilities");
        t.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "talismans have no triggered abilities");
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_HasColorlessManaAbility(string cardName, string a, string b)
    {
        _ = a; _ = b;
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });

        // {C} parses to one generic mana — distinguishing it from the
        // two coloured abilities is "has 0 of every WUBRG colour".
        t.Abilities.OfType<ManaAbility>()
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
    [MemberData(nameof(AllTalismans))]
    public void Talisman_ColoredA_Activation_LosesOneLife(string cardName, string a, string b)
    {
        _ = b;
        var alice = new Player("Alice", 20);
        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        var coloredA = FindColoredAbility(t, a);

        coloredA.Activate();

        alice.LifeTotal.Should().Be(19,
            $"{cardName}: tapping for {{{a}}} deals 1 damage to you");
        t.IsTapped.Should().BeTrue();
        t.Zone.Should().Be(ZoneType.Battlefield,
            "talismans are NOT sacrificed by their painful mana abilities");
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_ColoredB_Activation_LosesOneLife(string cardName, string a, string b)
    {
        _ = a;
        var alice = new Player("Alice", 20);
        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        var coloredB = FindColoredAbility(t, b);

        coloredB.Activate();

        alice.LifeTotal.Should().Be(19,
            $"{cardName}: tapping for {{{b}}} deals 1 damage to you");
        t.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_Colorless_Activation_DoesNotLoseLife(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        var colorless = t.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);

        colorless.Activate();

        alice.LifeTotal.Should().Be(20,
            $"{cardName}: the {{T}}: Add {{C}} mode does NOT carry a pain rider");
        t.IsTapped.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(AllTalismans))]
    public void Talisman_CannotActivateColoredWhenTapped(string cardName, string a, string b)
    {
        var alice = new Player("Alice", 20);
        var t = TalismanCycleFactory.Create(alice, new[] { cardName, a, b });
        alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        var coloredA = FindColoredAbility(t, a);
        var coloredB = FindColoredAbility(t, b);

        coloredA.Activate();

        coloredB.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void Talisman_CanActivateColoredAtOneLife_DropsToZero()
    {
        // Distinct from Horizon Canopy: CR 119.4's "you can't pay life
        // you don't have" gates "Pay X life" costs only. Talismans deal
        // damage, which reduces life — there's no life-floor activation
        // gate. Activating at 1 life is legal; SBAs then handle loss.
        var alice = new Player("Alice", 1);
        var t = TalismanCycleFactory.Create(alice, new[] { "Talisman of Progress", "W", "U" });
        alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        var white = FindColoredAbility(t, "W");

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
    public void Talisman_Create_ThrowsOnNullOwner()
    {
        var act = () => TalismanCycleFactory.Create(null!, new[] { "Talisman of Progress", "W", "U" });
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Talisman_Create_ThrowsOnTooFewArgs()
    {
        var alice = new Player("Alice", 20);

        var act = () => TalismanCycleFactory.Create(alice, new[] { "Talisman of Progress", "W" });

        act.Should().Throw<ArgumentException>()
            .WithMessage("*TalismanCycleFactory needs args*");
    }

    [Fact]
    public void Talisman_FallbackOverload_BuildsTalismanOfProgress()
    {
        var alice = new Player("Alice", 20);

        var t = TalismanCycleFactory.Create(alice);

        t.Name.Should().Be("Talisman of Progress");
        t.ManaCost.Should().Be("{2}");
        t.Abilities.OfType<ManaAbility>().Should().HaveCount(3);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ManaAbility FindColoredAbility(Artifact talisman, string color)
    {
        var match = ManaCost.Parse(color);
        return talisman.Abilities.OfType<ManaAbility>().Single(m =>
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
