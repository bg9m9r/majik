using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="PlazaOfHeroesFactory"/> — Plaza of Heroes
/// (Dominaria United). Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Spend this mana only to cast a
///    legendary spell.
///    {T}: Add one mana of any color among legendary permanents you
///    control.
///    {3}, {T}, Exile this land: Target legendary creature gains hexproof
///    and indestructible until end of turn."
///
/// Covers:
/// - Identity (Land type, printed name, owner/controller, non-Basic,
///   non-Legendary).
/// - {T}: Add {C} — one colourless mana ability ({C} buckets as Generic +1).
/// - {T}: Add one mana of any color (legendary-spell-only) — five
///   any-colour <see cref="ManaAbility"/> instances stamped with a
///   "legendary spell" <see cref="SpendRestriction"/> (same posture as
///   Delighted Halfling).
/// - {T}: Add one mana of any color among legendary permanents you control —
///   five any-colour <see cref="ManaAbility"/> instances, each gated by a
///   canActivateCheck requiring that colour to appear among the controller's
///   legendary permanents.
/// - {3}, {T}, Exile this land: an <see cref="ActivatedAbility"/> with a
///   single "target legendary creature" <see cref="Majik.Core.Players.Agents.TargetRequest"/>;
///   on resolve grants Hexproof + Indestructible until end of turn.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class PlazaOfHeroesFactoryTests
{
    private const string CardName = "Plaza of Heroes";

    private static readonly string[] Wubrg = { "W", "U", "B", "R", "G" };

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void PlazaOfHeroes_IsLand_WithCorrectName()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        land.Should().BeOfType<Land>();
        land.HasType(CardType.Land).Should().BeTrue();
        land.Name.Should().Be(CardName);
    }

    [Fact]
    public void PlazaOfHeroes_OwnerAndControllerAreSet()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        land.Owner.Should().BeSameAs(alice);
        land.Controller.Should().BeSameAs(alice);
    }

    [Fact]
    public void PlazaOfHeroes_IsNotBasic_AndNotLegendary()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        land.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse(
            "Plaza of Heroes is a plain non-legendary Land");
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsLand()
    {
        var alice = new Player("Alice", 20);

        var card = NamedCardFactory.Create(CardName, alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be(CardName);
        card.HasType(CardType.Land).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void PlazaOfHeroes_HasColorlessManaAbility()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        var colorless = land.Abilities.OfType<ManaAbility>().Where(IsColorless).ToList();
        colorless.Should().HaveCount(1, "{T}: Add {C} → exactly one colourless mode");
        colorless[0].SpendRestriction.Should().BeNull(
            "the {C} ability is unrestricted");
    }

    [Fact]
    public void PlazaOfHeroes_ColorlessAbility_TapsForColorless()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        var colorless = land.Abilities.OfType<ManaAbility>().Single(IsColorless);

        var produced = colorless.Activate();

        produced.Generic.Should().Be(1, "{C} buckets as Generic +1 in ManaCost.Parse");
        land.IsTapped.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of any color. Spend this mana only to cast a
    // legendary spell.
    // -----------------------------------------------------------------------

    [Fact]
    public void PlazaOfHeroes_HasFiveLegendaryRestrictedColorAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        var restricted = land.Abilities.OfType<ManaAbility>()
            .Where(m => m.SpendRestriction != null)
            .ToList();

        restricted.Should().HaveCount(5,
            "{T}: Add one mana of any color (legendary-spell-only) → one per WUBRG");
        restricted.Should().OnlyContain(
            m => m.SpendRestriction!.Description == "legendary spell");
    }

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void PlazaOfHeroes_LegendaryRestricted_CoversEachColor(string color)
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);

        var match = ManaCost.Parse(color);
        land.Abilities.OfType<ManaAbility>()
            .Where(m => m.SpendRestriction != null)
            .Should().Contain(m => ColorMatches(m, match),
                $"the legendary-spell-only mode can add {{{color}}}");
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of any color among legendary permanents you control.
    // -----------------------------------------------------------------------

    [Fact]
    public void PlazaOfHeroes_HasFiveLegendaryColorIdentityAbilities()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        // Unrestricted any-colour abilities (no SpendRestriction) gated on
        // colours among legendary permanents you control — one per WUBRG.
        var identity = land.Abilities.OfType<ManaAbility>()
            .Where(m => m.SpendRestriction == null && !IsColorless(m))
            .ToList();

        identity.Should().HaveCount(5,
            "{T}: Add one mana of any color among legendary permanents you control");
    }

    [Fact]
    public void PlazaOfHeroes_ColorIdentityAbility_CannotActivate_WithNoLegendaryPermanents()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.SpendRestriction == null
                && ColorMatches(m, ManaCost.Parse("W")));

        white.CanActivate().Should().BeFalse(
            "no white appears among legendary permanents you control");
    }

    [Fact]
    public void PlazaOfHeroes_ColorIdentityAbility_CanActivate_WhenLegendaryPermanentHasThatColor()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A legendary green creature the controller controls.
        var legend = new Creature("Yavimaya Elder", "{1}{G}{G}", 0, 0,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(alice);
        legend.SetController(alice);
        alice.Zones.Battlefield.AddCard(legend);
        legend.SetZone(ZoneType.Battlefield);

        var green = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.SpendRestriction == null
                && ColorMatches(m, ManaCost.Parse("G")));
        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.SpendRestriction == null
                && ColorMatches(m, ManaCost.Parse("W")));

        green.CanActivate().Should().BeTrue(
            "green appears among legendary permanents you control");
        white.CanActivate().Should().BeFalse(
            "white does not appear among legendary permanents you control");
    }

    [Fact]
    public void PlazaOfHeroes_ColorIdentityAbility_IgnoresNonLegendaryAndOpponentPermanents()
    {
        var alice = new Player("Alice", 20);
        var bob = new Player("Bob", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // A NON-legendary red creature Alice controls — must NOT count.
        var nonLegend = new Creature("Goblin", "{R}", 1, 1);
        nonLegend.SetOwner(alice);
        nonLegend.SetController(alice);
        alice.Zones.Battlefield.AddCard(nonLegend);
        nonLegend.SetZone(ZoneType.Battlefield);

        // A legendary blue creature BOB controls — must NOT count.
        var theirLegend = new Creature("Bob's Legend", "{U}{U}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        theirLegend.SetOwner(bob);
        theirLegend.SetController(bob);
        bob.Zones.Battlefield.AddCard(theirLegend);
        theirLegend.SetZone(ZoneType.Battlefield);

        var red = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.SpendRestriction == null
                && ColorMatches(m, ManaCost.Parse("R")));
        var blue = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.SpendRestriction == null
                && ColorMatches(m, ManaCost.Parse("U")));

        red.CanActivate().Should().BeFalse(
            "the red permanent is not legendary");
        blue.CanActivate().Should().BeFalse(
            "the blue legendary permanent is controlled by an opponent");
    }

    // -----------------------------------------------------------------------
    // {3}, {T}, Exile this land: Target legendary creature gains hexproof
    // and indestructible until end of turn.
    // -----------------------------------------------------------------------

    [Fact]
    public void PlazaOfHeroes_HasProtectionActivatedAbility_WithCorrectCost()
    {
        var alice = new Player("Alice", 20);

        var land = PlazaOfHeroesFactory.Create(alice);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();

        activated.TargetRequests.Should().HaveCount(1,
            "target legendary creature");
        activated.TargetRequests[0].MinTargets.Should().Be(1);
        activated.TargetRequests[0].MaxTargets.Should().Be(1);

        var manaCost = activated.Costs.OfType<ManaCostCost>().Single().Cost;
        manaCost.Generic.Should().Be(3, "the protection ability costs {3}");

        activated.Costs.OfType<AdditionalCost>().Should().NotBeEmpty(
            "the protection ability has a tap cost ({T}) and a self-exile cost");
    }

    [Fact]
    public void PlazaOfHeroes_Protection_OnResolve_GrantsHexproofAndIndestructible()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var legend = new Creature("Hero", "{1}{W}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(alice);
        legend.SetController(alice);
        legend.ActiveEffects = new ContinuousEffectsService();
        alice.Zones.Battlefield.AddCard(legend);
        legend.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(
            new IReadOnlyList<object>[] { new object[] { legend } });

        foreach (var effect in activated.Effects) effect.Execute();

        legend.ActiveEffects!.Compute(legend).Keywords
            .Contains("Hexproof").Should().BeTrue(
                "the targeted legendary creature gains hexproof until end of turn");
        legend.ActiveEffects!.Compute(legend).Keywords
            .Contains("Indestructible").Should().BeTrue(
                "the targeted legendary creature gains indestructible until end of turn");
    }

    [Fact]
    public void PlazaOfHeroes_Protection_ExilesItself_OnResolve()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var legend = new Creature("Hero", "{1}{W}", 2, 2,
            supertypes: new[] { CardSupertype.Legendary });
        legend.SetOwner(alice);
        legend.SetController(alice);
        legend.ActiveEffects = new ContinuousEffectsService();
        alice.Zones.Battlefield.AddCard(legend);
        legend.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(
            new IReadOnlyList<object>[] { new object[] { legend } });

        foreach (var effect in activated.Effects) effect.Execute();

        land.Zone.Should().Be(ZoneType.Exile,
            "the self-exile cost moves the land to exile");
        alice.Zones.Exile.GetCards().Should().Contain(land);
    }

    [Fact]
    public void PlazaOfHeroes_Protection_NoTargetChosen_IsNoOp()
    {
        var alice = new Player("Alice", 20);
        var land = PlazaOfHeroesFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var activated = land.Abilities.OfType<ActivatedAbility>().Single();
        // No SetChosenTargets — ChosenTargets is empty.

        var act = () => { foreach (var effect in activated.Effects) effect.Execute(); };

        act.Should().NotThrow("no target → resolution is a no-op");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static bool IsColorless(ManaAbility m) =>
        m.ManaGenerated.White == 0 &&
        m.ManaGenerated.Blue == 0 &&
        m.ManaGenerated.Black == 0 &&
        m.ManaGenerated.Red == 0 &&
        m.ManaGenerated.Green == 0 &&
        m.ManaGenerated.Generic == 1;

    private static bool ColorMatches(ManaAbility m, ManaCost match) =>
        m.ManaGenerated.White == match.White &&
        m.ManaGenerated.Blue == match.Blue &&
        m.ManaGenerated.Black == match.Black &&
        m.ManaGenerated.Red == match.Red &&
        m.ManaGenerated.Green == match.Green &&
        m.ManaGenerated.Generic == match.Generic;
}
