using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Painful Truths (Battle for Zendikar, {1}{B}{B}, Sorcery).
///
/// Oracle: "Converge — You draw X cards and lose X life, where X is the
/// number of colors of mana spent to cast this spell."
///
/// Coverage:
///   - Identity (name, type, cost) + NamedCardFactory dispatch.
///   - Resolve draws X cards and ticks X life off the caster.
///   - Default colors-spent = 1 (printed-pip floor: single distinct color
///     {B}).
///   - Zero / negative X clamps to a clean no-op.
///   - Empty library short-circuits: flags the draw-from-empty loss
///     condition, life-loss still applies for the printed X.
///   - SpellDefinition shape: no target requests, no modes, no X variable.
/// </summary>
public class PainfulTruthsTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void PainfulTruths_IsSorcery_At1BB()
    {
        var s = PainfulTruthsFactory.Create(_alice);

        s.Name.Should().Be("Painful Truths");
        s.ManaCost.Should().Be("{1}{B}{B}");
        s.HasType(CardType.Sorcery).Should().BeTrue();
        s.Owner.Should().BeSameAs(_alice);
        s.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PainfulTruths()
    {
        var card = NamedCardFactory.Create("Painful Truths", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Painful Truths");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{B}{B}");
    }

    [Fact]
    public void DefaultColorsSpent_IsOne()
    {
        PainfulTruthsFactory.DefaultColorsSpent.Should().Be(1,
            "the printed cost has 1 distinct colored pip ({B}; the two {B} pips collapse to one distinct color)");
    }

    // ── Resolve — draw X + lose X life ──────────────────────────────────

    [Fact]
    public void Resolve_X3_DrawsThree_LosesThree()
    {
        // Library [a, b, c, d, e]; X = 3 → draw [a, b, c], lose 3 life.
        var a = SeedLibraryCard("A");
        var b = SeedLibraryCard("B");
        var c = SeedLibraryCard("C");
        var d = SeedLibraryCard("D");
        var e = SeedLibraryCard("E");

        var startingLife = _alice.LifeTotal;
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 3);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a, b, c });
        _alice.Zones.Library.GetCards().Should().Equal(new[] { d, e });
        _alice.LifeTotal.Should().Be(startingLife - 3);
    }

    [Fact]
    public void Resolve_X1_DrawsOne_LosesOne()
    {
        var a = SeedLibraryCard("A");
        SeedLibraryCard("B");

        var startingLife = _alice.LifeTotal;
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 1);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.LifeTotal.Should().Be(startingLife - 1);
    }

    [Fact]
    public void Resolve_DefaultProviderNull_UsesDefaultColorsSpent_One()
    {
        var a = SeedLibraryCard("A");
        SeedLibraryCard("B");

        var startingLife = _alice.LifeTotal;
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, colorsSpentProvider: null);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(new[] { a });
        _alice.LifeTotal.Should().Be(startingLife - 1);
    }

    [Fact]
    public void Resolve_XZero_NoOp()
    {
        SeedLibraryCard("A");

        var startingLife = _alice.LifeTotal;
        var startingHand = _alice.Zones.Hand.GetCards().Count();
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 0);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand);
        _alice.LifeTotal.Should().Be(startingLife);
    }

    [Fact]
    public void Resolve_XNegative_ClampsToZero_NoOp()
    {
        SeedLibraryCard("A");

        var startingLife = _alice.LifeTotal;
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => -3);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.LifeTotal.Should().Be(startingLife);
    }

    [Fact]
    public void Resolve_X3_EmptyLibrary_FlagsDrawLoss_LifeLossStillApplies()
    {
        // Empty library: every draw stamps TriedToDrawFromEmpty (CR 704.5b).
        // Life-loss still ticks for the printed X (CR 700.2 separates the
        // draw and life-loss into the same instruction set, but the
        // life-loss is evaluated against printed X, not "draws actually
        // landed").
        var startingLife = _alice.LifeTotal;
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 3);
        foreach (var fx in effects) fx.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.LifeTotal.Should().Be(startingLife - 3);
    }

    // ── SpellDefinition shape ───────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasNoTargetRequests_NoModes_NoX()
    {
        var def = PainfulTruthsFactory.BuildSpellDefinition(_alice);

        def.TargetRequests.Should().BeEmpty(
            "Painful Truths' converge body resolves entirely on the caster");
        def.HasVariableX.Should().BeFalse(
            "the converge X is supplied by the mana ledger, not a target-time X choice");
        def.Modes.Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private Card SeedLibraryCard(string name)
    {
        var c = new Card(name, "");
        c.SetOwner(_alice);
        _alice.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
