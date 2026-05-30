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
/// Tests for <see cref="GroveOfTheBurnwillowsFactory"/> — Future Sight's
/// "reverse painland" (the player-helping dual that gives EACH OPPONENT
/// life rather than dealing damage to you).
///
/// Oracle text (Scryfall, verified):
///   "{T}: Add {C}.
///    {T}: Add {R} or {G}. Each opponent gains 1 life."
///
/// Structurally identical to <see cref="PainLandCycleFactory"/> — a
/// colourless mode plus two coloured modes that carry an
/// activation-time rider — except the rider is "each opponent gains 1
/// life" (CR 119.3 lifegain) routed through the Zulaport-style
/// <c>opponentResolver</c> convention instead of "this deals 1 damage to
/// you". CR 605.1 — every mode is a mana ability and never uses the
/// stack.
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic/-Legendary).
/// - Exactly 3 mana abilities (one {C}, one {R}, one {G}).
/// - {C} mode has no rider — no opponent gains life.
/// - {R} / {G} modes give EACH opponent 1 life on activation.
/// - No life-floor gate / no self-damage (controller life never changes).
/// - Tap-as-cost: second coloured activation can't pay {T} once tapped.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class GroveOfTheBurnwillowsFactoryTests
{
    private const string Name = "Grove of the Burnwillows";

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Grove_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(Name);
    }

    [Fact]
    public void Grove_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void Grove_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
    }

    [Fact]
    public void Grove_Dispatch_ResolvesViaNamedCardFactory()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(Name, alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be(Name);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Grove_HasThreeManaAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(3,
            "one {C} + one {R} + one {G}");
    }

    [Fact]
    public void Grove_HasNoActivatedOrTriggeredAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Grove has no non-mana activated abilities");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Grove has no triggered abilities");
    }

    [Fact]
    public void Grove_HasColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().Contain(m => IsColorlessOnly(m),
                "Grove has a {T}: Add {C} mana ability");
    }

    [Fact]
    public void Grove_ProducesRedAndGreen()
    {
        var alice = new Player("Alice", 20);

        var land = GroveOfTheBurnwillowsFactory.Create(alice);

        FindColoredAbility(land, "R").Should().NotBeNull("Grove taps for {R}");
        FindColoredAbility(land, "G").Should().NotBeNull("Grove taps for {G}");
    }

    // -----------------------------------------------------------------------
    // Opponent-lifegain rider — coloured activations give each opponent 1 life
    // -----------------------------------------------------------------------

    [Fact]
    public void Grove_ColoredRed_Activation_EachOpponentGainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var carol = new Player("Carol", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(
            alice, opponentResolver: () => new[] { bob, carol });
        var red = FindColoredAbility(land, "R");

        red.Activate();

        bob.LifeTotal.Should().Be(21, "tapping for {R} gives each opponent 1 life");
        carol.LifeTotal.Should().Be(21, "each opponent gains 1 life (multiplayer scaling)");
        alice.LifeTotal.Should().Be(20, "Grove never costs its controller life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Grove_ColoredGreen_Activation_EachOpponentGainsOneLife()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(
            alice, opponentResolver: () => new[] { bob });
        var green = FindColoredAbility(land, "G");

        green.Activate();

        bob.LifeTotal.Should().Be(21, "tapping for {G} gives the opponent 1 life");
        alice.LifeTotal.Should().Be(20, "Grove never costs its controller life");
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Grove_Colorless_Activation_NoOpponentLifeGain()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(
            alice, opponentResolver: () => new[] { bob });
        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorlessOnly);

        colorless.Activate();

        bob.LifeTotal.Should().Be(20,
            "the {T}: Add {C} mode carries no 'each opponent gains 1 life' rider");
        alice.LifeTotal.Should().Be(20);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Grove_NeverGivesControllerLife()
    {
        // CR 102.4 — "each opponent" excludes the controller. The
        // resolver feeds the full player list; the rider must skip the
        // controller even when present in that list.
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(
            alice, opponentResolver: () => new[] { alice, bob });
        var red = FindColoredAbility(land, "R");

        red.Activate();

        alice.LifeTotal.Should().Be(20, "the controller is not 'an opponent' of itself");
        bob.LifeTotal.Should().Be(21);
    }

    [Fact]
    public void Grove_ColoredActivation_NoResolver_IsNoOpButStillTaps()
    {
        // Single-arg Create wires no resolver — the lifegain side is a
        // no-op (shape/dispatch tests), the mana + tap still happen.
        var alice = new Player("Alice", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(alice);
        var red = FindColoredAbility(land, "R");

        var produced = red.Activate();

        produced.Red.Should().Be(1, "tapping still yields {R}");
        alice.LifeTotal.Should().Be(20);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Grove_CannotActivateColoredWhenTapped()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = GroveOfTheBurnwillowsFactory.Create(
            alice, opponentResolver: () => new[] { bob });
        var red = FindColoredAbility(land, "R");
        var green = FindColoredAbility(land, "G");

        red.Activate();

        green.CanActivate().Should().BeFalse(
            "the {T} cost cannot be paid by a tapped permanent");
    }

    // -----------------------------------------------------------------------
    // Args validation
    // -----------------------------------------------------------------------

    [Fact]
    public void Grove_Create_ThrowsOnNullOwner()
    {
        var act = () => GroveOfTheBurnwillowsFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
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
