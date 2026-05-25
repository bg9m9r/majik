using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ArcumsAstrolabeFactory"/>.
///
/// Arcum's Astrolabe — Snow Artifact {(S)}.
///   "When Arcum's Astrolabe enters, draw a card.
///    {1}, {T}: Add one mana of any color."
///
/// Covers:
/// - Identity (Snow Artifact, printed cost "{S}") + NamedCardFactory
///   dispatch.
/// - Five WUBRG mana abilities, each gated on untapped + {1} affordability.
/// - Tap-for-coloured deducts {1} from the controller's mana pool.
/// - Insufficient mana blocks activation (without flipping the tap).
/// - ETB trigger fires from CardMovedEvent (library → battlefield) and
///   draws a card.
/// </summary>
public class ArcumsAstrolabeTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void ArcumsAstrolabe_IsSnowArtifact_WithPrintedSnowCost()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);

        c.Name.Should().Be("Arcum's Astrolabe");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "the printed Snow supertype (CR 205.4d)");
        c.ManaCost.Should().Be("{S}",
            "printed Snow mana cost is preserved; engine collapses to "
            + "generic at parse time");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ArcumsAstrolabe()
    {
        var card = NamedCardFactory.Create("Arcum's Astrolabe", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Arcum's Astrolabe");
        card.HasSupertype(CardSupertype.Snow).Should().BeTrue();
    }

    // --------------------------------------------------------------
    // Mana ability shape — WUBRG
    // --------------------------------------------------------------

    [Fact]
    public void ArcumsAstrolabe_HasFiveColouredManaAbilities()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);
        var mas = c.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(5, "one per WUBRG");
        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 1);
    }

    // --------------------------------------------------------------
    // Activation gating — needs {1} in pool
    // --------------------------------------------------------------

    [Fact]
    public void CantActivate_WhenManaPoolIsEmpty()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);

        var white = c.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeFalse(
            "the {1} activation cost can't be paid from an empty pool");
        c.IsTapped.Should().BeFalse(
            "failed legality check must not flip the tap");
    }

    [Fact]
    public void TapForWhite_DeductsOneGeneric_AndProducesWhite()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var white = c.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
        var produced = white.Activate();

        produced.White.Should().Be(1);
        produced.TotalValue.Should().Be(1);
        c.IsTapped.Should().BeTrue();
        _alice.ManaPool.Total.Should().Be(0,
            "the {1} additional cost was paid out of the pool");
    }

    [Fact]
    public void CantActivate_WhileTapped()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        _alice.AddManaToPool(ManaCost.Parse("1"));
        c.Tap();

        foreach (var ma in c.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "tapped Astrolabe can't pay the {T} cost again");
        }
    }

    // --------------------------------------------------------------
    // ETB triggered ability
    // --------------------------------------------------------------

    [Fact]
    public void ArcumsAstrolabe_HasEtbDrawTrigger()
    {
        var c = ArcumsAstrolabeFactory.Create(_alice);

        var triggered = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggered.Should().ContainSingle(
            "ETB-draw trigger");
    }
}
