using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ExpressiveIterationTemplate"/>.
///
/// Covers:
/// - Pattern recognition (oracle text triggers the template).
/// - v1 deterministic distribution: top three cards distribute to hand
///   (first), bottom of library (second), exile (third).
/// - Graceful behavior for libraries smaller than three.
/// </summary>
public class ExpressiveIterationTests
{
    private readonly Player _alice = new("Alice", 20);

    private const string OracleText =
        "Look at the top three cards of your library. Put one of them into your hand, "
        + "put one of them on the bottom of your library, and exile one of them. "
        + "You may play the exiled card this turn.";

    private static CardEntity BuildEntity() => new()
    {
        Name = "Expressive Iteration",
        OracleText = OracleText,
    };

    // -----------------------------------------------------------------------
    // Pattern match
    // -----------------------------------------------------------------------

    [Fact]
    public void Template_MatchesOracleText()
    {
        var template = new ExpressiveIterationTemplate();
        template.TryExtractParams(OracleText).Should().NotBeNull(
            "the regex matches the canonical Expressive Iteration text");
    }

    [Fact]
    public void Template_DoesNotMatchUnrelatedText()
    {
        var template = new ExpressiveIterationTemplate();
        template.TryExtractParams("Draw three cards.").Should().BeNull();
    }

    [Fact]
    public void OracleSpellBinder_ResolvesTemplateForExpressiveIteration()
    {
        var spell = OracleSpellBinder.Bind(
            BuildEntity(), _alice, resolver: o => o, stack: null);

        spell.Should().NotBeNull(
            "the bespoke template is registered and binds Expressive Iteration");
    }

    // -----------------------------------------------------------------------
    // Resolution semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_DistributesTopThreeAcrossHandBottomExile()
    {
        var c1 = new Card("First", "");
        var c2 = new Card("Second", "");
        var c3 = new Card("Third", "");
        var c4 = new Card("Fourth", "");
        // Add in order; first added = top of library (consistent with
        // other templates in this codebase that use GetCards().Take(N)).
        foreach (var c in new[] { c1, c2, c3, c4 })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        spell.Should().NotBeNull();

        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(c1, "first → hand");
        _alice.Zones.Exile.GetCards().Should().Contain(c3, "third → exile");
        c1.Zone.Should().Be(ZoneType.Hand);
        c3.Zone.Should().Be(ZoneType.Exile);

        // Second card → bottom of library. Remaining (untouched fourth +
        // bottomed second) should still be in the library; c2 should NOT be
        // in hand or exile, and c4 should still be present at its original
        // relative position.
        _alice.Zones.Library.GetCards().Should().Contain(c2,
            "second → bottom of library");
        _alice.Zones.Library.GetCards().Should().Contain(c4,
            "fourth card was not touched");
        _alice.Zones.Hand.GetCards().Should().NotContain(c2);
        _alice.Zones.Exile.GetCards().Should().NotContain(c2);
        c2.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Resolve_EmptyLibrary_IsNoOp()
    {
        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        spell.Should().NotBeNull();

        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Resolve_OneCardLibrary_GoesToHandOnly()
    {
        var only = new Card("Only", "");
        _alice.Zones.Library.AddCard(only);
        only.SetZone(ZoneType.Library);

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(only);
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().NotContain(only);
    }

    [Fact]
    public void Resolve_TwoCardLibrary_GoesToHandAndBottom_NoExile()
    {
        var c1 = new Card("c1", "");
        var c2 = new Card("c2", "");
        _alice.Zones.Library.AddCard(c1); c1.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c2); c2.SetZone(ZoneType.Library);

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(c1);
        _alice.Zones.Library.GetCards().Should().Contain(c2,
            "second card returns to the (now-empty) library bottom");
        _alice.Zones.Exile.GetCards().Should().BeEmpty();
    }
}
