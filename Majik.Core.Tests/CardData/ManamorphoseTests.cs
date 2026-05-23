using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ManamorphoseFactory"/>.
///
/// Card: Manamorphose — Instant {1}{R/G} (Shadowmoor).
///   "Add two mana in any combination of colors. Draw a card."
///
/// Covers:
///   - Identity (name, instant type, hybrid mana cost, owner/controller, MV).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Default-resolve adds {R}{G} to the caster's mana pool and draws a card.
///   - Custom colour picker — caller-chosen pair lands in the pool (e.g. {U}{U}).
///   - Empty library: mana still added; SBA-flag set; no throw.
/// </summary>
public class ManamorphoseTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Manamorphose_Identity()
    {
        var c = ManamorphoseFactory.Create(_alice);

        c.Name.Should().Be("Manamorphose");
        c.ManaCost.Should().Be("{1}{R/G}");
        c.HasType(CardType.Instant).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 107.4e hybrid parsing — {1}{R/G} = 1 generic + one HybridPip(R, G).
        c.ManaCostValue.Generic.Should().Be(1);
        c.ManaCostValue.HybridPips.Should().HaveCount(1);
        c.ManaCostValue.HybridPips[0].Color1.Should().Be(ManaColor.Red);
        c.ManaCostValue.HybridPips[0].Color2.Should().Be(ManaColor.Green);
        c.ManaCostValue.TotalValue.Should().Be(2);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Manamorphose()
    {
        var card = NamedCardFactory.Create("Manamorphose", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Manamorphose");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R/G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: default picker = {R}{G}, draws one
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DefaultPicker_AddsRedAndGreen_AndDrawsOne()
    {
        var top = SeedLibraryCard(_alice, "Top");

        var effects = ManamorphoseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.ManaPool.Red.Should().Be(1, "default picker adds {R}");
        _alice.ManaPool.Green.Should().Be(1, "default picker adds {G}");
        _alice.ManaPool.Total.Should().Be(2, "Manamorphose deposits exactly two mana");

        _alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolve: caller-supplied colour picker
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_CustomPicker_AddsChosenColors()
    {
        SeedLibraryCard(_alice, "Top");

        // Caller wants {U}{U} — the legal "two of one color" combination.
        var effects = ManamorphoseFactory.BuildResolveEffect(
            _alice, _ => new[] { ManaColor.Blue, ManaColor.Blue });
        foreach (var e in effects) e.Execute();

        _alice.ManaPool.Blue.Should().Be(2, "picker chose two blue mana");
        _alice.ManaPool.Red.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(2);
    }

    [Fact]
    public void Resolve_ThreeColorPicker_Throws()
    {
        // "Add two mana" — the picker must yield exactly two colours.
        var effects = ManamorphoseFactory.BuildResolveEffect(
            _alice, _ => new[] { ManaColor.Red, ManaColor.Green, ManaColor.Blue });

        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*exactly 2*");
    }

    // -----------------------------------------------------------------------
    // Empty library — mana lands first, draw flags SBA loss
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_EmptyLibrary_AddsManaAndFlagsSbaLoss()
    {
        // Library starts empty.
        var effects = ManamorphoseFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.ManaPool.Red.Should().Be(1, "the mana deposit happens before the draw");
        _alice.ManaPool.Green.Should().Be(1);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "draw on empty library must flag the SBA-driven loss (CR 704.5b)");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static ICard SeedLibraryCard(Player p, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
