using System.Linq;
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
/// Tests for <see cref="MirrexFactory"/> (Phyrexia: All Will Be One).
///
/// Oracle text (Scryfall-confirmed 2026-06-01):
///   "{T}: Add {C}.
///    {T}: Add one mana of any color. Activate only if this land entered this
///    turn.
///    {3}, {T}: Create a 1/1 colorless Phyrexian Mite artifact creature token
///    with toxic 1 and "This token can't block." (Players dealt combat damage
///    by it also get a poison counter.)"
///
/// Scryfall type line: Land — Sphere.
///
/// Covers:
/// - Identity: Land type, Sphere subtype, name, nonbasic, owner/controller.
/// - <see cref="NamedCardFactory"/> dispatch resolves "Mirrex".
/// - Three abilities: two <see cref="ManaAbility"/> ({C}; any-colour) + one
///   token-making <see cref="ActivatedAbility"/>.
/// - {T}: Add {C} produces colourless (folded into Generic).
/// - {T}: Add one mana of any color — gated on "entered this turn"
///   (HasSummoningSickness), produces a coloured pip (default Green), and is
///   unavailable once the land has lost summoning sickness.
/// - {3}, {T} token ability: cost shape; resolution mints a 1/1 colourless
///   Phyrexian Mite artifact creature with toxic 1 + a can't-block marker.
///
/// Ability declaration order: the {C} mana ability is built first from the
/// JSON definition; the any-colour mana ability is appended second. The tests
/// rely on that order (<c>.First()</c> = {C}, <c>.Last()</c> = any-colour).
/// </summary>
[Trait("Color", "C")]
public class MirrexFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private Land PlaceOnBattlefield()
    {
        var land = MirrexFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(land);
        return land;
    }

    private static ManaAbility ColorlessAbility(Land land)
        => land.Abilities.OfType<ManaAbility>().First();

    private static ManaAbility AnyColorAbility(Land land)
        => land.Abilities.OfType<ManaAbility>().Last();

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Create_IsLand_Sphere_NamedMirrex()
    {
        var land = MirrexFactory.Create(_alice);
        land.Name.Should().Be("Mirrex");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Sphere).Should().BeTrue("Mirrex is a Sphere land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Create_IsNotBasic_NotLegendary()
    {
        var land = MirrexFactory.Create(_alice);
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("Mirrex is nonbasic");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("Mirrex is not legendary");
    }
    [Fact]
    public void Create_HasExactlyThreeAbilities()
    {
        var land = MirrexFactory.Create(_alice);
        land.Abilities.Should().HaveCount(3,
            "two mana abilities + one token-making activated ability");
        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2);
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void ColorlessManaAbility_Produces_OneGenericColorless()
    {
        var land = PlaceOnBattlefield();
        var colorless = ColorlessAbility(land);

        colorless.CanActivate().Should().BeTrue();
        var produced = colorless.Activate();

        produced.Generic.Should().Be(1, "{C} folds into Generic");
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);
        produced.Black.Should().Be(0);
        produced.Red.Should().Be(0);
        produced.Green.Should().Be(0);
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of any color — gated on "entered this turn"
    // -----------------------------------------------------------------------

    [Fact]
    public void AnyColorAbility_Available_WhileEnteredThisTurn()
    {
        var land = PlaceOnBattlefield();
        // Freshly created => HasSummoningSickness == true => "entered this turn".
        land.HasSummoningSickness.Should().BeTrue();

        AnyColorAbility(land).CanActivate().Should().BeTrue(
            "the any-colour ability is active the turn the land entered");
    }

    [Fact]
    public void AnyColorAbility_Produces_ColoredPip()
    {
        var land = PlaceOnBattlefield();

        var produced = AnyColorAbility(land).Activate();
        produced.Green.Should().Be(1, "defaults to Green (Lotus Cobra deferral)");
        produced.Generic.Should().Be(0, "an any-colour pip is coloured, not generic");
        land.IsTapped.Should().BeTrue("the {T} cost was paid");
    }

    [Fact]
    public void AnyColorAbility_Unavailable_AfterSummoningSicknessCleared()
    {
        var land = PlaceOnBattlefield();
        land.ClearSummoningSickness(); // no longer "entered this turn"

        AnyColorAbility(land).CanActivate().Should().BeFalse(
            "the any-colour ability requires the land to have entered this turn");
    }

    [Fact]
    public void AnyColorAbility_RespectsColorPicker()
    {
        var land = MirrexFactory.Create(_alice, zones: null, colorPicker: () => ManaColor.Blue);
        land.SetZone(ZoneType.Battlefield);

        var produced = AnyColorAbility(land).Activate();
        produced.Blue.Should().Be(1, "the picker chose Blue");
    }

    // -----------------------------------------------------------------------
    // {3}, {T}: Create a Phyrexian Mite token
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenAbility_HasManaAndTapCost()
    {
        var land = MirrexFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        ability.Costs.Should().HaveCount(2, "{3} + {T}");
    }

    [Fact]
    public void TokenAbility_Resolve_CreatesOneToxicArtifactMite()
    {
        var land = PlaceOnBattlefield();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty(
            "no creatures before the token ability resolves");

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var e in ability.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken)
            .ToList();

        tokens.Should().HaveCount(1, "one Mite token is created");
        var token = tokens.Single();
        token.Name.Should().Be("Phyrexian Mite");
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasType(CardType.Artifact).Should().BeTrue("the Mite is an artifact creature");
        token.HasType(CardType.Creature).Should().BeTrue("the Mite is a creature");
        token.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        token.HasSubtype(CardSubtype.Mite).Should().BeTrue();
        CardColors.GetColors(token).Should().BeEmpty("the Mite is colourless (CR 111.4)");

        var keywords = token.Abilities.OfType<KeywordAbility>().ToList();
        keywords.Should().Contain(k => k.Keyword == "toxic" && k.Arg == 1,
            "the Mite has toxic 1");
        keywords.Should().Contain(k => k.Keyword == "CantBlock",
            "the Mite carries the can't-block marker");
    }
}
