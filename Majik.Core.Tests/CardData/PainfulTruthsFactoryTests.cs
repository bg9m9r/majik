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
/// Unit tests for <see cref="PainfulTruthsFactory"/>.
///
/// Card: Painful Truths — Sorcery {2}{B} (Battle for Zendikar).
///   "Converge — Draw cards equal to the number of colors of mana spent
///    to cast this spell, then you lose that much life."
///
/// Covers:
///   - Identity / dispatch.
///   - Sorcery type.
///   - Resolve effect: draws N + loses N life with caller-supplied
///     colors-spent provider.
///   - Default provider value (1) is used when none supplied.
///   - Clamp at MaxColorsSpent (5).
/// </summary>
public class PainfulTruthsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PainfulTruths_Identity()
    {
        var card = PainfulTruthsFactory.Create(_alice);

        card.Name.Should().Be("Painful Truths");
        card.ManaCost.Should().Be("{2}{B}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PainfulTruths()
    {
        var card = NamedCardFactory.Create("Painful Truths", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Painful Truths");
        card.ManaCost.Should().Be("{2}{B}");
    }

    // -----------------------------------------------------------------------
    // Resolve — Converge draw / life-loss
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_ThreeColors_DrawsThreeAndLosesThreeLife()
    {
        SeedLibrary(_alice, 5);
        var startingHand = _alice.Zones.Hand.GetCards().Count();

        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 3);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 3,
            "Converge with 3 colors → draw 3");
        _alice.LifeTotal.Should().Be(17, "20 - 3 = 17");
    }

    [Fact]
    public void Resolve_ProviderNull_UsesDefaultOfOne()
    {
        SeedLibrary(_alice, 5);
        var startingHand = _alice.Zones.Hand.GetCards().Count();

        // Build with a null provider — falls back to DefaultColorsSpent.
        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, colorsSpentProvider: null);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 1);
        _alice.LifeTotal.Should().Be(19, "default colors-spent = 1");
    }

    [Fact]
    public void Resolve_FiveColors_DrawsFiveAndLosesFive()
    {
        SeedLibrary(_alice, 7);
        var startingHand = _alice.Zones.Hand.GetCards().Count();

        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 5);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 5);
        _alice.LifeTotal.Should().Be(15);
    }

    [Fact]
    public void Resolve_NegativeProvider_ClampsToZero()
    {
        SeedLibrary(_alice, 5);
        var startingHand = _alice.Zones.Hand.GetCards().Count();

        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => -3);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand);
        _alice.LifeTotal.Should().Be(20);
    }

    [Fact]
    public void Resolve_OverMax_ClampsToFive()
    {
        SeedLibrary(_alice, 9);
        var startingHand = _alice.Zones.Hand.GetCards().Count();

        var effects = PainfulTruthsFactory.BuildResolveEffect(_alice, () => 8);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Count().Should().Be(startingHand + 5,
            "WUBRG floor — at most 5 colours of mana");
        _alice.LifeTotal.Should().Be(15);
    }

    private static void SeedLibrary(Player p, int n)
    {
        for (var i = 0; i < n; i++)
        {
            var c = new Creature($"Filler{i}", "{1}", 1, 1);
            c.SetOwner(p);
            c.SetController(p);
            c.SetZone(ZoneType.Library);
            p.Zones.Library.AddCard(c);
        }
    }
}
