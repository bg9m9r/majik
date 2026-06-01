using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PropheticPrismFactory"/> (Conflux, {2}).
///
/// Artifact cantrip mana rock. Oracle text (verified against Scryfall):
///   "When this artifact enters, draw a card.
///    {1}, {T}: Add one mana of any color."
///
/// Loaded from the embedded JSON definition via
/// <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>.
/// Colour-fixing twin of <see cref="PrismaticLensFactory"/>; the
/// difference is the ETB cantrip trigger (and the absence of the Lens's
/// free {C} ability).
///
/// Covers:
/// - Identity (name, Artifact type, {2} cost, owner/controller, nonbasic /
///   non-legendary, no creature type).
/// - One ETB triggered ability (CR 603.6) plus five "{1}, {T}: Add
///   &lt;color&gt;" mana abilities (the JSON encoding of "Add one mana of
///   any color", CR 605.1 / 605.1a) — no free {C} ability.
/// - The ETB trigger draws one card on resolution (CR 120.2); empty library
///   is a graceful no-op.
/// - The {1} additional cost gates the coloured abilities: no mana =>
///   cannot activate; one generic in pool => can activate.
/// - Activating a coloured ability pays {1} from the pool, taps the prism,
///   and adds that colour.
/// - Tap-as-cost: a tapped prism cannot activate any of its abilities.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
public class PropheticPrismFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void PropheticPrism_IsArtifact_WithCorrectName()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);

        prism.Should().BeOfType<Artifact>();
        prism.Name.Should().Be("Prophetic Prism");
        prism.HasType(CardType.Artifact).Should().BeTrue();
        prism.HasType(CardType.Creature).Should().BeFalse();
        prism.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        prism.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        prism.ManaCost.Should().Be("{2}");
        prism.Owner.Should().BeSameAs(_alice);
        prism.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PropheticPrism()
    {
        var card = NamedCardFactory.Create("Prophetic Prism", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Prophetic Prism");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape — one ETB trigger + five coloured mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void PropheticPrism_HasSingleEtbTrigger()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);

        prism.Abilities.OfType<TriggeredAbility>().Should().HaveCount(
            1, "the prism cantrips on a single enters-the-battlefield trigger");
    }

    [Fact]
    public void PropheticPrism_HasFiveColoredManaAbilities_OnePerColor()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);
        var mana = prism.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(
            5, "five {1},{T}: Add <color> abilities — no free {C} ability (unlike the Lens)");

        mana.Count(a => a.ManaGenerated.White == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
    }

    [Fact]
    public void PropheticPrism_HasNoColorlessManaAbility()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);

        prism.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.Generic >= 1)
            .Should().BeEmpty("Prophetic Prism has no {T}: Add {C} ability (unlike Prismatic Lens)");
    }

    // -----------------------------------------------------------------------
    // ETB cantrip — draw a card on resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void PropheticPrism_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top of library", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", alice);
        var etb = prism.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // CR 120.2 — the controller draws one card on resolution.
        alice.Zones.Hand.GetCards().Should().Contain(top);
        alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void PropheticPrism_EtbTrigger_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", alice);
        var etb = prism.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {1}, {T}: Add one mana of any color — {1} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void PropheticPrism_ColoredAbilities_CannotActivate_WithEmptyPool()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);

        foreach (var ability in prism.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "the {1} additional cost cannot be paid from an empty pool");
        }
    }

    [Fact]
    public void PropheticPrism_ColoredAbilities_CanActivate_WithOneGenericInPool()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));

        foreach (var ability in prism.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeTrue();
        }
    }

    [Fact]
    public void PropheticPrism_BlueActivation_PaysOneGeneric_TapsSelf_AndAddsBlue()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        var blue = prism.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "the seed {1} was spent on the prism's {1} cost");
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        prism.IsTapped.Should().BeTrue("{T} is part of the activation cost");
    }

    // -----------------------------------------------------------------------
    // Tap-as-cost
    // -----------------------------------------------------------------------

    [Fact]
    public void PropheticPrism_NoAbilityCanActivate_WhenTapped()
    {
        var prism = (Artifact)NamedCardFactory.Create("Prophetic Prism", _alice);
        // Plenty of mana so any rejection is solely from the tap state.
        _alice.AddManaToPool(ManaCost.Parse("5"));
        var blue = prism.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);
        prism.IsTapped.Should().BeTrue();

        foreach (var ability in prism.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "a tapped permanent cannot pay the {T} cost");
        }
    }
}
