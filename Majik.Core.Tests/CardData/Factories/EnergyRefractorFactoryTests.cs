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
/// Unit tests for <see cref="EnergyRefractorFactory"/> (Edge of Eternities,
/// {2}).
///
/// Artifact cantrip mana rock. Oracle text (verified against Scryfall):
///   "When this artifact enters, draw a card.
///    {2}: Add one mana of any color."
///
/// Card identity + the ETB cantrip are loaded from the embedded JSON
/// definition via <see cref="Majik.Core.CardData.Definitions.CardDefinitionFactory"/>;
/// the colour-fixing ability is added in the factory as five no-tap
/// {2}: Add &lt;color&gt; abilities (the JSON mana encoding always implies
/// {T}, which Energy Refractor's flat {2} cost does not have).
///
/// Covers:
/// - Identity (name, Artifact type, {2} cost, owner/controller, nonbasic /
///   non-legendary, no creature type).
/// - One ETB triggered ability (CR 603.6) plus five "{2}: Add &lt;color&gt;"
///   mana abilities (the encoding of "Add one mana of any color",
///   CR 605.1 / 605.1a) — no free {C} ability.
/// - The ETB trigger draws one card on resolution (CR 120.2); empty library
///   is a graceful no-op.
/// - The {2} cost gates the coloured abilities: no mana => cannot activate;
///   two generic in pool => can activate.
/// - Activating a coloured ability pays {2} from the pool, leaves the
///   refractor UNTAPPED (no {T} component), and adds that colour.
/// - The ability is repeatable while untapped (no {T}): a refractor that has
///   already produced mana can still activate as long as {2} is payable.
/// - Dispatch through <see cref="NamedCardFactory"/>.
/// </summary>
[Trait("Color", "C")]
public class EnergyRefractorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void EnergyRefractor_IsArtifact_WithCorrectName()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);

        refractor.Should().BeOfType<Artifact>();
        refractor.Name.Should().Be("Energy Refractor");
        refractor.HasType(CardType.Artifact).Should().BeTrue();
        refractor.HasType(CardType.Creature).Should().BeFalse();
        refractor.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        refractor.HasSupertype(CardSupertype.Legendary).Should().BeFalse();
        refractor.ManaCost.Should().Be("{2}");
        refractor.Owner.Should().BeSameAs(_alice);
        refractor.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape — one ETB trigger + five coloured mana abilities
    // -----------------------------------------------------------------------

    [Fact]
    public void EnergyRefractor_HasSingleEtbTrigger()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);

        refractor.Abilities.OfType<TriggeredAbility>().Should().HaveCount(
            1, "the refractor cantrips on a single enters-the-battlefield trigger");
    }

    [Fact]
    public void EnergyRefractor_HasFiveColoredManaAbilities_OnePerColor()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);
        var mana = refractor.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(
            5, "five {2}: Add <color> abilities — one per WUBRG, no free {C} ability");

        mana.Count(a => a.ManaGenerated.White == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Blue == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Black == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Red == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
        mana.Count(a => a.ManaGenerated.Green == 1 && a.ManaGenerated.TotalValue == 1).Should().Be(1);
    }

    [Fact]
    public void EnergyRefractor_HasNoColorlessManaAbility()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);

        refractor.Abilities.OfType<ManaAbility>()
            .Where(a => a.ManaGenerated.Generic >= 1)
            .Should().BeEmpty("Energy Refractor has no {T}: Add {C} ability");
    }

    // -----------------------------------------------------------------------
    // ETB cantrip — draw a card on resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void EnergyRefractor_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top of library", "");
        top.SetOwner(alice);
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", alice);
        var etb = refractor.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in etb.Effects) effect.Execute();

        // CR 120.2 — the controller draws one card on resolution.
        alice.Zones.Hand.GetCards().Should().Contain(top);
        alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void EnergyRefractor_EtbTrigger_EmptyLibrary_NoOp()
    {
        var alice = new Player("Alice", 20);

        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", alice);
        var etb = refractor.Abilities.OfType<TriggeredAbility>().Single();
        Action act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // {2}: Add one mana of any color — {2} cost gate
    // -----------------------------------------------------------------------

    [Fact]
    public void EnergyRefractor_ColoredAbilities_CannotActivate_WithEmptyPool()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);
        refractor.SetZone(ZoneType.Battlefield);

        foreach (var ability in refractor.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeFalse(
                "the {2} cost cannot be paid from an empty pool");
        }
    }

    [Fact]
    public void EnergyRefractor_ColoredAbilities_CanActivate_WithTwoGenericInPool()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);
        refractor.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("2"));

        foreach (var ability in refractor.Abilities.OfType<ManaAbility>())
        {
            ability.CanActivate().Should().BeTrue();
        }
    }

    [Fact]
    public void EnergyRefractor_BlueActivation_PaysTwoGeneric_StaysUntapped_AndAddsBlue()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);
        refractor.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("2"));
        var blue = refractor.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(1);
        _alice.ManaPool.Generic.Should().Be(0, "the seed {2} was spent on the refractor's {2} cost");
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        refractor.IsTapped.Should().BeFalse("{2} has no {T} component — the refractor stays untapped");
    }

    [Fact]
    public void EnergyRefractor_IsRepeatable_WhileTwoManaAvailable()
    {
        var refractor = (Artifact)NamedCardFactory.Create("Energy Refractor", _alice);
        refractor.SetZone(ZoneType.Battlefield);
        // Enough for two activations.
        _alice.AddManaToPool(ManaCost.Parse("4"));
        var blue = refractor.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Blue == 1);
        var activator = new ManaAbilityActivator();

        activator.ActivateManaAbility(blue, _alice);
        // Still untapped + {2} left => can fire again (no {T} lock).
        blue.CanActivate().Should().BeTrue("no {T} component, so the ability is repeatable while {2} is payable");
        activator.ActivateManaAbility(blue, _alice);

        _alice.ManaPool.Blue.Should().Be(2);
        _alice.ManaPool.Generic.Should().Be(0);
        refractor.IsTapped.Should().BeFalse();
    }
}
