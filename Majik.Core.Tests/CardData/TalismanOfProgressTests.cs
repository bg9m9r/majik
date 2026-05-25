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
/// Unit tests for <see cref="TalismanOfProgressFactory"/>.
///
/// Talisman of Progress — Artifact {2}.
///   "{T}: Add {C}.
///    {T}: Add {W} or {U}. Talisman of Progress deals 1 damage to you."
///
/// Covers:
/// - Identity (Artifact, {2}) + NamedCardFactory dispatch.
/// - Three mana abilities total: one painless {C}, plus painful {W} and {U}.
/// - Tap-for-{C} produces colourless (generic bucket) and DOES NOT deal
///   damage.
/// - Tap-for-{W} produces white AND costs 1 life (painland-style additional
///   cost; mirrors HorizonLandBinder).
/// - Tap-for-{U} produces blue AND costs 1 life.
/// - Activation taps the talisman (the {T} half) but does NOT sacrifice it.
/// </summary>
public class TalismanOfProgressTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity + dispatch
    // --------------------------------------------------------------

    [Fact]
    public void TalismanOfProgress_IsArtifact_TwoCost()
    {
        var t = TalismanOfProgressFactory.Create(_alice);

        t.Name.Should().Be("Talisman of Progress");
        t.HasType(CardType.Artifact).Should().BeTrue();
        t.ManaCost.Should().Be("{2}");
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TalismanOfProgress()
    {
        var card = NamedCardFactory.Create("Talisman of Progress", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Talisman of Progress");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{2}");
    }

    // --------------------------------------------------------------
    // Ability shape — {C}, {W}, {U}
    // --------------------------------------------------------------

    [Fact]
    public void TalismanOfProgress_HasThreeManaAbilities()
    {
        var t = TalismanOfProgressFactory.Create(_alice);
        var mas = t.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(3, "{C}, {W}, {U}");

        // Colourless body folds into the generic bucket via ManaCost.Parse
        // (CR 107.4c) — TotalValue == 1, no colour pip set.
        mas.Should().ContainSingle(m => m.ManaGenerated.TotalValue == 1
                                     && m.ManaGenerated.White == 0
                                     && m.ManaGenerated.Blue == 0
                                     && m.ManaGenerated.Black == 0
                                     && m.ManaGenerated.Red == 0
                                     && m.ManaGenerated.Green == 0,
            "{C} ability folds to generic");

        mas.Should().ContainSingle(m => m.ManaGenerated.White == 1
                                     && m.ManaGenerated.TotalValue == 1);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.TotalValue == 1);
    }

    // --------------------------------------------------------------
    // Tap-for-colourless — painless
    // --------------------------------------------------------------

    [Fact]
    public void TapForColorless_ProducesGeneric_NoLifeLoss()
    {
        var t = TalismanOfProgressFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);

        var colorless = t.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 0
                      && m.ManaGenerated.Blue == 0
                      && m.ManaGenerated.TotalValue == 1);

        colorless.CanActivate().Should().BeTrue();
        var produced = colorless.Activate();

        produced.TotalValue.Should().Be(1);
        produced.White.Should().Be(0);
        produced.Blue.Should().Be(0);

        t.IsTapped.Should().BeTrue("activation taps the talisman");
        t.Zone.Should().Be(ZoneType.Battlefield,
            "no sacrifice — talisman stays on the battlefield");
        _alice.LifeTotal.Should().Be(20, "the {C} ability is painless");
    }

    // --------------------------------------------------------------
    // Tap-for-{W} / {U} — painful
    // --------------------------------------------------------------

    [Fact]
    public void TapForWhite_ProducesWhite_AndCostsOneLife()
    {
        var t = TalismanOfProgressFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);

        var white = t.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.White == 1);

        white.CanActivate().Should().BeTrue();
        var produced = white.Activate();

        produced.White.Should().Be(1);
        produced.TotalValue.Should().Be(1);

        t.IsTapped.Should().BeTrue("activation taps the talisman");
        t.Zone.Should().Be(ZoneType.Battlefield,
            "the talisman is NOT sacrificed");
        _alice.LifeTotal.Should().Be(19, "1 damage to the controller (painland rider)");
    }

    [Fact]
    public void TapForBlue_ProducesBlue_AndCostsOneLife()
    {
        var t = TalismanOfProgressFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);

        var blue = t.Abilities.OfType<ManaAbility>()
            .Single(m => m.ManaGenerated.Blue == 1);

        var produced = blue.Activate();

        produced.Blue.Should().Be(1);
        _alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void CantActivate_WhileTapped()
    {
        var t = TalismanOfProgressFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(t);
        t.SetZone(ZoneType.Battlefield);
        t.Tap();

        foreach (var ma in t.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "tapped talismans can't pay the {T} cost again");
        }
    }
}
