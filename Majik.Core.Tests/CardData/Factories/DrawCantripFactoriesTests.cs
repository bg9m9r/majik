using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for the pure "Draw N cards" cantrips harvested onto the shared
/// declarative <c>draw_card</c> verb path (cantrip-factory-harvest pay-down):
/// <list type="bullet">
///   <item><see cref="HarmonizeFactory"/> — {2}{G}{G} Sorcery, "Draw three cards."</item>
///   <item><see cref="TidingsFactory"/> — {3}{U}{U} Sorcery, "Draw four cards."</item>
///   <item><see cref="WeaveFateFactory"/> — {3}{U} Instant, "Draw two cards."</item>
/// </list>
///
/// Each is a thin shape over the ordered <see cref="Majik.Core.CardData.Definitions.DrawCardEffectDef"/>
/// verb array — identical posture to <see cref="OptFactory"/> /
/// <see cref="SerumVisionsFactory"/>. Covers card identity,
/// <see cref="NamedCardFactory"/> dispatch, SpellDefinition shape, the exact
/// draw count, and the empty-library CR 704.5b loss flag.
/// </summary>
public class DrawCantripFactoriesTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Harmonize ───────────────────────────────────────────────────────────

    [Fact]
    public void Harmonize_HasSorceryShape_Green_At2GG()
    {
        var card = HarmonizeFactory.Create(_alice);

        card.Name.Should().Be("Harmonize");
        card.ManaCost.Should().Be("{2}{G}{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Harmonize()
    {
        var card = NamedCardFactory.Create("Harmonize", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Harmonize");
        card.ManaCost.Should().Be("{2}{G}{G}");
    }

    [Fact]
    public void Harmonize_Resolve_DrawsExactlyThreeCards()
    {
        var lib = SeedLibrary(5);

        HarmonizeFactory.BuildResolveEffect(_alice).Single().Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(lib.GetRange(0, 3));
        _alice.Zones.Library.GetCards().Should().Equal(lib.GetRange(3, 2));
        _alice.TriedToDrawFromEmptyLibrary.Should().BeFalse();
    }

    // ── Tidings ─────────────────────────────────────────────────────────────

    [Fact]
    public void Tidings_HasSorceryShape_Blue_At3UU()
    {
        var card = TidingsFactory.Create(_alice);

        card.Name.Should().Be("Tidings");
        card.ManaCost.Should().Be("{3}{U}{U}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Tidings()
    {
        var card = NamedCardFactory.Create("Tidings", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Tidings");
        card.ManaCost.Should().Be("{3}{U}{U}");
    }

    [Fact]
    public void Tidings_Resolve_DrawsExactlyFourCards()
    {
        var lib = SeedLibrary(6);

        TidingsFactory.BuildResolveEffect(_alice).Single().Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(lib.GetRange(0, 4));
        _alice.Zones.Library.GetCards().Should().Equal(lib.GetRange(4, 2));
    }

    // ── Weave Fate ──────────────────────────────────────────────────────────

    [Fact]
    public void WeaveFate_HasInstantShape_Blue_At3U()
    {
        var card = WeaveFateFactory.Create(_alice);

        card.Name.Should().Be("Weave Fate");
        card.ManaCost.Should().Be("{3}{U}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Blue);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_WeaveFate()
    {
        var card = NamedCardFactory.Create("Weave Fate", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Weave Fate");
        card.ManaCost.Should().Be("{3}{U}");
    }

    [Fact]
    public void WeaveFate_Resolve_DrawsExactlyTwoCards()
    {
        var lib = SeedLibrary(4);

        WeaveFateFactory.BuildResolveEffect(_alice).Single().Execute();

        _alice.Zones.Hand.GetCards().Should().Equal(lib.GetRange(0, 2));
        _alice.Zones.Library.GetCards().Should().Equal(lib.GetRange(2, 2));
    }

    [Fact]
    public void WeaveFate_Resolve_EmptyLibrary_FlagsLossSba_DoesNotThrow()
    {
        var act = () => WeaveFateFactory.BuildResolveEffect(_alice).Single().Execute();

        act.Should().NotThrow();
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // ── SpellDefinition shape (declarative path) ─────────────────────────────

    [Fact]
    public void DrawCantrips_SpellDefinitions_HaveNoTargets_NoModes_NoX()
    {
        foreach (var def in new[]
        {
            HarmonizeFactory.BuildDefinition(),
            TidingsFactory.BuildDefinition(),
            WeaveFateFactory.BuildDefinition(),
        })
        {
            def.HasVariableX.Should().BeFalse();
            def.Modes.Should().BeEmpty();
            def.TargetRequests.Should().BeEmpty();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private List<ICard> SeedLibrary(int n)
    {
        var cards = new List<ICard>(n);
        for (var i = 0; i < n; i++)
        {
            var c = new Card($"L{i}", "");
            c.SetOwner(_alice);
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
            cards.Add(c);
        }

        return cards;
    }
}
