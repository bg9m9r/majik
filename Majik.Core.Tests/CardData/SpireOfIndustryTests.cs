using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpireOfIndustryFactory"/>.
///
/// Spire of Industry — Land (Aether Revolt):
///   "{T}: Add {C}."
///   "{T}, Pay 1 life: Add one mana of any color. Activate only if you
///    control an artifact."
///
/// Same shape as <see cref="GlimmervoidFactory"/> (any-colour land with an
/// artifact-presence gate) plus the Horizon-land "{T}, Pay 1 life" mana
/// cost (see <see cref="HorizonLandBinder.AttachPayLifeMana"/>).
///
/// Covers:
///   - Card identity (Land, non-basic, no subtypes, no supertypes).
///   - NamedCardFactory dispatch.
///   - The {T}: Add {C} colourless ability (no life cost, no artifact gate).
///   - Five "any color" abilities (WUBRG), one per colour, each costing
///     {T} + Pay 1 life.
///   - Artifact gate: the five any-colour abilities are blocked unless the
///     controller controls an artifact.
///   - Pay-1-life: activating an any-colour ability loses 1 life (CR 119.4
///     legality + the additional-cost payer).
///   - Tap gate: all abilities blocked while the land is tapped.
///   - Life gate: any-colour abilities blocked at 1 life (CR 119.4).
/// </summary>
public class SpireOfIndustryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void PutArtifact(Player owner)
    {
        var artifact = new Artifact("Ornithopter", "{0}");
        artifact.SetOwner(owner);
        artifact.SetController(owner);
        owner.Zones.Battlefield.AddCard(artifact);
        artifact.SetZone(ZoneType.Battlefield);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpireOfIndustry_HasCorrectIdentity()
    {
        var land = SpireOfIndustryFactory.Create(_alice);

        land.Name.Should().Be("Spire of Industry");
        land.HasType(CardType.Land).Should().BeTrue("Spire of Industry is a Land");
        land.HasType(CardType.Artifact).Should().BeFalse("it is not an Artifact");
        land.HasType(CardType.Creature).Should().BeFalse("it is not a Creature");
        land.HasSupertype(CardSupertype.Legendary).Should().BeFalse("non-legendary");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse("non-basic");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpireOfIndustry()
    {
        var card = NamedCardFactory.Create("Spire of Industry", _alice);

        card.Should().BeOfType<Land>("factory creates a Land instance");
        card.Name.Should().Be("Spire of Industry");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape — six mana abilities total
    // -----------------------------------------------------------------------

    [Fact]
    public void SpireOfIndustry_HasSixManaAbilities_OneColorlessPlusFiveAnyColor()
    {
        var land = SpireOfIndustryFactory.Create(_alice);
        var mas = land.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(6,
            "one {T}: Add {C} ability plus five {T}, Pay 1 life any-colour abilities");

        // {T}: Add {C} — colourless lands in the Generic slot.
        mas.Should().ContainSingle(m => m.ManaGenerated.Generic == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} — no life cost, no artifact gate
    // -----------------------------------------------------------------------

    [Fact]
    public void ColorlessAbility_ProducesC_WithoutArtifactOrLifeCost()
    {
        var land = SpireOfIndustryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No artifact on the battlefield.

        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1);

        colorless.CanActivate().Should().BeTrue(
            "{T}: Add {C} has no artifact gate and no life cost");
        var produced = colorless.Activate();

        produced.Generic.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        land.IsTapped.Should().BeTrue("tapping the Spire to activate");
        _alice.LifeTotal.Should().Be(20, "{T}: Add {C} does not pay life");
    }

    // -----------------------------------------------------------------------
    // Any-colour abilities — artifact gate (CR — "Activate only if you
    // control an artifact")
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("W")]
    [InlineData("U")]
    [InlineData("B")]
    [InlineData("R")]
    [InlineData("G")]
    public void AnyColorAbility_Blocked_WhenControllerHasNoArtifact(string color)
    {
        var land = SpireOfIndustryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        // No artifact.

        var ability = SelectColored(land, color);
        ability.CanActivate().Should().BeFalse(
            "any-colour ability requires you to control an artifact");
    }

    [Theory]
    [InlineData("W", 1, 0, 0, 0, 0)]
    [InlineData("U", 0, 1, 0, 0, 0)]
    [InlineData("B", 0, 0, 1, 0, 0)]
    [InlineData("R", 0, 0, 0, 1, 0)]
    [InlineData("G", 0, 0, 0, 0, 1)]
    public void AnyColorAbility_ProducesColor_AndPaysOneLife_WhenArtifactPresent(
        string color, int white, int blue, int black, int red, int green)
    {
        var land = SpireOfIndustryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        PutArtifact(_alice);

        var ability = SelectColored(land, color);

        ability.CanActivate().Should().BeTrue(
            "controller controls an artifact and has > 1 life");
        var produced = ability.Activate();

        produced.White.Should().Be(white);
        produced.Blue.Should().Be(blue);
        produced.Black.Should().Be(black);
        produced.Red.Should().Be(red);
        produced.Green.Should().Be(green);
        produced.TotalValue.Should().Be(1, "each activation produces exactly 1 pip");
        land.IsTapped.Should().BeTrue("tapping the Spire to activate");
        _alice.LifeTotal.Should().Be(19, "Pay 1 life is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tap gate — all six abilities blocked while tapped
    // -----------------------------------------------------------------------

    [Fact]
    public void AllAbilities_BlockedWhileTapped()
    {
        var land = SpireOfIndustryFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        PutArtifact(_alice);
        land.Tap();

        foreach (var ma in land.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse("land is tapped");
        }
    }

    // -----------------------------------------------------------------------
    // Life gate — CR 119.4: can't pay life you don't have
    // -----------------------------------------------------------------------

    [Fact]
    public void AnyColorAbility_Blocked_AtOneLife_EvenWithArtifact()
    {
        var lowLife = new Player("Carol", 1);
        var land = SpireOfIndustryFactory.Create(lowLife);
        lowLife.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        PutArtifact(lowLife);

        var white = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "Pay 1 life requires life total > 1 (CR 119.4)");

        // The colourless ability has no life cost — still usable at 1 life.
        var colorless = land.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Generic == 1);
        colorless.CanActivate().Should().BeTrue(
            "{T}: Add {C} has no life cost");
    }

    private static ManaAbility SelectColored(Land land, string color)
    {
        var mas = land.Abilities.OfType<ManaAbility>().ToList();
        return color switch
        {
            "W" => mas.Single(m => m.ManaGenerated.White == 1),
            "U" => mas.Single(m => m.ManaGenerated.Blue == 1),
            "B" => mas.Single(m => m.ManaGenerated.Black == 1),
            "R" => mas.Single(m => m.ManaGenerated.Red == 1),
            "G" => mas.Single(m => m.ManaGenerated.Green == 1),
            _ => throw new ArgumentOutOfRangeException(nameof(color)),
        };
    }
}
