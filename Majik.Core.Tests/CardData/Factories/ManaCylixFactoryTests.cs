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
/// Unit tests for <see cref="ManaCylixFactory"/> (Hour of Devastation, {1}).
///
/// Artifact mana rock. Oracle text (verified against Scryfall 2026-06-23):
///   "{1}, {T}: Add one mana of any color."
///
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
///
/// Covers the card's UNIQUE shape:
/// - Identity: name, Artifact type, {1} cost (note: cheaper than the {2}
///   Prophetic Prism / Prismatic Lens twins), nonbasic / non-legendary.
/// - Exactly five "{1}, {T}: Add &lt;color&gt;" mana abilities (one per WUBRG)
///   and NO free {C} ability — the JSON encoding of "Add one mana of any color"
///   (CR 605.1 / 605.1a), same modal-colour posture as Prophetic Prism.
/// - No activated or triggered abilities (no ETB cantrip, unlike Prophetic
///   Prism).
/// - The {1} additional cost gates every coloured ability: empty pool =>
///   cannot activate; one generic in pool => can activate.
/// - Activating a coloured ability pays {1} from the pool, taps the cylix,
///   and adds that colour (CR 605.1 — never uses the stack).
/// - Tap-as-cost: a tapped cylix cannot activate any ability.
/// </summary>
[Trait("Color", "C")]
public class ManaCylixFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaCylix_Identity_IsArtifact_OneGenericCost()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);

        cylix.Should().BeOfType<Artifact>();
        cylix.Name.Should().Be("Mana Cylix");
        cylix.HasType(CardType.Artifact).Should().BeTrue();
        cylix.HasType(CardType.Creature).Should().BeFalse();
        cylix.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        cylix.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        cylix.ManaCostValue.Should().Be(ManaCost.Parse("{1}"));
        cylix.Owner.Should().BeSameAs(_alice);
        cylix.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mana abilities — shape (the unique bit: five coloured, no free {C})
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaCylix_HasFiveColoredManaAbilities_AndNoFreeColorless()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);

        var mana = cylix.Abilities.OfType<ManaAbility>().ToList();
        mana.Should().HaveCount(
            5,
            "\"Add one mana of any color\" binds as five {1},{T}: Add <color> abilities, with no free {C}");

        // Unlike Prismatic Lens, there is no free colourless mana ability.
        mana.Should().NotContain(
            a => a.ManaGenerated.Generic == 1 && a.ManaGenerated.TotalValue == 1,
            "Mana Cylix has no \"{T}: Add {C}\" ability");
    }

    [Fact]
    public void ManaCylix_HasNoActivatedOrTriggeredAbilities()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);

        cylix.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "the cylix's only abilities are mana abilities");
        cylix.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Mana Cylix has no ETB cantrip (unlike Prophetic Prism)");
    }

    [Fact]
    public void ManaCylix_HasOneAbilityPerColor_ProducingThatColor()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);
        var mana = cylix.Abilities.OfType<ManaAbility>().ToList();

        mana.Count(a => a.ManaGenerated.White == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color — {1} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaCylix_ColoredAbilities_CannotActivate_WithEmptyPool()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);

        foreach (var ability in cylix.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "the {1} additional cost cannot be paid from an empty pool");
        }
    }

    [Fact]
    public void ManaCylix_ColoredAbilities_CanActivate_WithOneGenericInPool()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));

        foreach (var ability in cylix.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeTrue();
        }
    }

    [Fact]
    public void ManaCylix_BlueActivation_PaysOneGeneric_TapsSelf_AndAddsBlue()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var blue = cylix.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the cylix's {1} cost");
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        cylix.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void ManaCylix_NoAbilityCanActivate_WhenTapped()
    {
        var cylix = (Artifact)NamedCardFactory.Create("Mana Cylix", _alice);
        // Plenty of mana so any rejection is solely from the tap state.
        _alice.AddManaToPool(ManaCost.Parse("5"));
        var green = cylix.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Green == 1);
        var activator = new ManaAbilityActivator();

        // First activation taps the cylix.
        activator.ActivateManaAbility(green, _alice);
        cylix.IsTapped.Should().BeTrue();

        foreach (var ability in cylix.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "a tapped permanent cannot pay the {T} cost");
        }
    }
}
