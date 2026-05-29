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
/// Tests for <see cref="CityOfBrassFactory"/> — City of Brass (Arabian
/// Nights and many reprints). Oracle text (verified against Scryfall):
///   "Whenever City of Brass becomes tapped, it deals 1 damage to you.
///    {T}: Add one mana of any color."
///
/// <para>
/// Modeling posture (v1): the engine has no faithful "becomes tapped"
/// event trigger (Rule 603.2 — there is no tapped-event on the bus, and
/// <c>StateChangeTriggerCondition</c> is only evaluated after an SBA pass,
/// which a mana-ability tap (CR 605.3 — never on the stack) does not run).
/// The only way City of Brass taps itself is its own {T} mana ability, so
/// the "deals 1 damage to you" rider is modelled the same way the merged
/// <see cref="PainLandCycleFactory"/> models its pain: an
/// <c>additionalCostPayer = controller.LoseLife(1)</c> attached to each of
/// the five WUBRG <see cref="ManaAbility"/> instances (the
/// <see cref="ManaConfluenceFactory"/> any-colour fan-out). CR 120.3 —
/// damage to a player reduces life by that amount. NO life-floor gate
/// (unlike Mana Confluence's "Pay 1 life", CR 119.4): pain can drop you to
/// 0 or below, exactly like the painlands.
/// </para>
///
/// Covers:
/// - Identity (Land, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - Five mana abilities (one per WUBRG colour) — any-colour fan-out, no
///   {C} mode.
/// - Each coloured activation deals 1 damage (loses 1 life) on top of {T}.
/// - No life-floor gate: pain can drop you to 0 / below.
/// - Tap-as-cost: a second activation can't pay {T} once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class CityOfBrassFactoryTests
{
    private const string CardName = "City of Brass";

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
    public void CityOfBrass_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void CityOfBrass_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void CityOfBrass_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void CityOfBrass_Dispatch_ResolvesViaNamedCardFactory()
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
    public void CityOfBrass_HasFiveManaAbilities_OnePerColor()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one any-colour mana ability per WUBRG; no {C} mode");
    }

    [Fact]
    public void CityOfBrass_HasNoColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

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
    public void CityOfBrass_HasNoActivatedAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(AllColors))]
    public void CityOfBrass_ProducesEachColor(string color)
    {
        var alice = new Player("Alice", 20);

        var land = CityOfBrassFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Should().NotBeNull($"City of Brass can add {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // "Deals 1 damage to you" rider (CR 120.3)
    // -----------------------------------------------------------------------

    [Theory]
    [MemberData(nameof(AllColors))]
    public void CityOfBrass_Activation_Deals1Damage(string color)
    {
        var alice = new Player("Alice", 20);
        var land = CityOfBrassFactory.Create(alice);
        var ability = FindColoredAbility(land, color);

        ability.Activate();

        alice.LifeTotal.Should().Be(19,
            $"tapping for {{{color}}} deals 1 damage to you");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void CityOfBrass_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var land = CityOfBrassFactory.Create(alice);
        var white = FindColoredAbility(land, "W");
        var blue = FindColoredAbility(land, "U");

        white.Activate();

        blue.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    [Fact]
    public void CityOfBrass_HasNoLifeFloorGate()
    {
        // CR 120.3 — damage is not a "pay life" cost; unlike Mana Confluence
        // (CR 119.4) there is no life-floor gate. Tapping at 1 life deals
        // 1 damage and drops you to 0 (then you lose to SBAs).
        var alice = new Player("Alice", 1);
        var land = CityOfBrassFactory.Create(alice);
        var white = FindColoredAbility(land, "W");

        white.CanActivate().Should().BeTrue(
            "pain damage carries no life-floor gate (CR 120.3, not 119.4)");

        white.Activate();
        alice.LifeTotal.Should().Be(0);
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
