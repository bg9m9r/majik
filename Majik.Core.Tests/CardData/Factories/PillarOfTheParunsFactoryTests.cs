using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PillarOfTheParunsFactory"/> — Pillar of the Paruns
/// (Guildpact). Oracle text (verified against Scryfall):
///   "{T}: Add one mana of any color. Spend this mana only to cast a
///   multicolored spell."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — same any-colour fan-out
///   shape as Mana Confluence / Cavern of Souls, with NO {C} mode and NO
///   "Pay 1 life" cost (unlike Mana Confluence).
/// - Each coloured ability stamps a <see cref="SpendRestriction"/> with the
///   "multicolored spell" predicate (CR 105.4 — a multicolored object has
///   two or more colours). Same data-only posture as Cavern of Souls /
///   Eldrazi Temple (payment-gate enforcement deferred until ManaPool grows
///   per-slot tags).
/// - Tap-as-cost: a second coloured activation can't pay {T} once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class PillarOfTheParunsFactoryTests
{
    private const string CardName = "Pillar of the Paruns";

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
    public void PillarOfTheParuns_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void PillarOfTheParuns_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void PillarOfTheParuns_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void PillarOfTheParuns_Dispatch_ResolvesViaNamedCardFactory()
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
    public void PillarOfTheParuns_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void PillarOfTheParuns_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        // Pillar of the Paruns has NO "{T}: Add {C}" mode: every mode
        // produces one coloured mana.
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
    public void PillarOfTheParuns_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void PillarOfTheParuns_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = PillarOfTheParunsFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"Pillar of the Paruns can add {{{color}}}");
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void PillarOfTheParuns_Activation_TapsWithoutLifeLoss(string color)
    {
        // Unlike Mana Confluence, Pillar of the Paruns charges NO life — the
        // only activation cost is {T}.
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(20, "the only activation cost is {T}");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void PillarOfTheParuns_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Spend-restriction — multicolored-only (CR 105.4)
    //
    // Data-only posture (payment-gate deferred; see factory xmldoc). These
    // tests pin that the rider exists and its predicate distinguishes
    // multicolored from monocolored / colourless spells.
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllColors))]
    public void PillarOfTheParuns_EveryColoredAbility_CarriesMulticoloredRestriction(string color)
    {
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.SpendRestriction.Should().NotBeNull(
            "Pillar's mana is spend-restricted to multicolored spells");
    }

    [Fact]
    public void PillarOfTheParuns_Restriction_SatisfiedByMulticoloredSpell()
    {
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var restriction = FindColoredAbility(land, "W").SpendRestriction!;

        // {W}{U} spell — two colours ⇒ multicolored (CR 105.4).
        var multi = new Majik.Core.Spells.Spell(
            new Card("Azorius Charm", "WU", new[] { CardType.Instant }), alice);

        restriction.SatisfiedBy(multi).Should().BeTrue(
            "a two-colour spell is multicolored");
    }

    [Fact]
    public void PillarOfTheParuns_Restriction_NotSatisfiedByMonocoloredSpell()
    {
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var restriction = FindColoredAbility(land, "W").SpendRestriction!;

        // {R} spell — one colour ⇒ NOT multicolored.
        var mono = new Majik.Core.Spells.Spell(
            new Card("Lightning Bolt", "R", new[] { CardType.Instant }), alice);

        restriction.SatisfiedBy(mono).Should().BeFalse(
            "a one-colour spell is not multicolored");
    }

    [Fact]
    public void PillarOfTheParuns_Restriction_NotSatisfiedByColorlessSpell()
    {
        var alice = new Player("Alice", 20);
        var land = PillarOfTheParunsFactory.Create(alice);
        var restriction = FindColoredAbility(land, "W").SpendRestriction!;

        // {2} artifact — no coloured pips ⇒ colourless, not multicolored.
        var colorless = new Majik.Core.Spells.Spell(
            new Card("Ornithopter", "0", new[] { CardType.Artifact }), alice);

        restriction.SatisfiedBy(colorless).Should().BeFalse(
            "a colourless spell is not multicolored");
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
