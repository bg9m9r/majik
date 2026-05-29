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
/// Unit tests for <see cref="PrismaticLensFactory"/> (Time Spiral, {2}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers:
/// - Identity (name, Artifact type, {2} cost, owner/controller, nonbasic
///   / non-legendary, no creature type).
/// - Six mana abilities total: one free "{T}: Add {C}" plus five
///   "{1}, {T}: Add &lt;color&gt;" (one per WUBRG) — the JSON encoding of
///   "Add one mana of any color" (CR 605.1 / 605.1a), same posture as
///   Chromatic Star / Springleaf Drum.
/// - The free {C} ability adds one colourless (folded into generic per
///   CR 107.4c) and needs no mana in the pool.
/// - The {1} additional cost gates the coloured abilities: no mana =>
///   cannot activate; one generic in pool => can activate.
/// - Activating a coloured ability pays {1} from the pool, taps the lens,
///   and adds that colour.
/// - Tap-as-cost: a tapped lens cannot activate any of its abilities.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class PrismaticLensFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static readonly string[] Colors = { "W", "U", "B", "R", "G" };

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticLens_IsArtifact_WithCorrectName()
    {
        var lens = PrismaticLensFactory.Create(_alice);

        lens.Should().BeOfType<Artifact>();
        lens.Name.Should().Be("Prismatic Lens");
        lens.HasType(CardType.Artifact).Should().BeTrue();
        lens.HasType(CardType.Creature).Should().BeFalse();
        lens.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        lens.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        lens.Owner.Should().BeSameAs(_alice);
        lens.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PrismaticLens()
    {
        var card = NamedCardFactory.Create("Prismatic Lens", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Prismatic Lens");
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticLens_HasSixManaAbilities_OneColorlessAndFiveColored()
    {
        var lens = PrismaticLensFactory.Create(_alice);

        var mana = lens.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(
            6,
            "one free {T}: Add {C} plus five {1},{T}: Add <color> abilities");
    }

    [Fact]
    public void PrismaticLens_HasNoActivatedOrTriggeredAbilities()
    {
        var lens = PrismaticLensFactory.Create(_alice);

        lens.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the lens's only abilities are mana abilities");
        lens.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
    }

    [Fact]
    public void PrismaticLens_HasOneAbilityProducingColorless()
    {
        var lens = PrismaticLensFactory.Create(_alice);

        // {C} folds into the generic bucket per CR 107.4c (ManaCost.cs:170).
        var colorless = lens.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.Generic == 1
                        && a.ManaGenerated.TotalValue == 1)
            .ToList();

        colorless.Should().HaveCount(1, "{T}: Add {C} is a single colourless mana ability");
    }

    [Fact]
    public void PrismaticLens_HasOneAbilityPerColor_ProducingThatColor()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        var mana = lens.Abilities.OfType<ManaAbility>().ToList();

        mana.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C} — free, needs no pool
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticLens_ColorlessAbility_CanActivate_WithEmptyPool()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        var colorless = lens.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 1 && a.ManaGenerated.TotalValue == 1);

        colorless.CanActivate().Should().BeTrue("{T}: Add {C} has no additional cost");
    }

    [Fact]
    public void PrismaticLens_ColorlessActivation_AddsOneColorless_AndTapsSelf()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        var colorless = lens.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 1 && a.ManaGenerated.TotalValue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(colorless, _alice);

        // {C} folds into the generic bucket (CR 107.4c).
        _alice.ManaPool.Generic.Should().Be(1);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        lens.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color — {1} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticLens_ColoredAbilities_CannotActivate_WithEmptyPool()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        var colored = lens.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.TotalValue == 1 && a.ManaGenerated.Generic == 0);

        foreach (var ability in colored)
        {
            ability.CanActivate().Should().BeFalse(
                "the {1} additional cost cannot be paid from an empty pool");
        }
    }

    [Fact]
    public void PrismaticLens_ColoredAbilities_CanActivate_WithOneGenericInPool()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var colored = lens.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.TotalValue == 1 && a.ManaGenerated.Generic == 0);

        foreach (var ability in colored)
        {
            ability.CanActivate().Should().BeTrue();
        }
    }

    [Fact]
    public void PrismaticLens_BlueActivation_PaysOneGeneric_TapsSelf_AndAddsBlue()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var blue = lens.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the lens's {1} cost");
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        lens.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void PrismaticLens_NoAbilityCanActivate_WhenTapped()
    {
        var lens = PrismaticLensFactory.Create(_alice);
        // Plenty of mana so any rejection is solely from the tap state.
        _alice.AddManaToPool(ManaCost.Parse("5"));
        var colorless = lens.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Generic == 1 && a.ManaGenerated.TotalValue == 1);
        var activator = new ManaAbilityActivator();

        // First activation taps the lens.
        activator.ActivateManaAbility(colorless, _alice);
        lens.IsTapped.Should().BeTrue();

        foreach (var ability in lens.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "a tapped permanent cannot pay the {T} cost");
        }
    }
}
