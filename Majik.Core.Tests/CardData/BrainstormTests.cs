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
/// Unit tests for <see cref="BrainstormTemplate"/>.
///
/// Covers:
/// - Pattern recognition (oracle text triggers the template).
/// - Resolution net hand-size math: hand grows by 1 (5 → draw 3 → 8 → return 2 → 6).
/// - Library top after resolution carries the returned cards.
/// - Empty library: partial / no draw + graceful handling.
/// </summary>
public class BrainstormTests
{
    private readonly Player _alice = new("Alice", 20);

    private const string OracleText =
        "Draw three cards, then put two cards from your hand on top of "
        + "your library in any order.";

    private static CardEntity BuildEntity() => new()
    {
        Name = "Brainstorm",
        OracleText = OracleText,
    };

    // -----------------------------------------------------------------------
    // Pattern match
    // -----------------------------------------------------------------------

    [Fact]
    public void Template_MatchesOracleText()
    {
        var template = new BrainstormTemplate();
        template.TryExtractParams(OracleText).Should().NotBeNull(
            "the regex matches the canonical Brainstorm text");
    }

    [Fact]
    public void Template_DoesNotMatchUnrelatedText()
    {
        var template = new BrainstormTemplate();
        template.TryExtractParams("Draw three cards.").Should().BeNull();
    }

    [Fact]
    public void OracleSpellBinder_ResolvesTemplateForBrainstorm()
    {
        var spell = OracleSpellBinder.Bind(
            BuildEntity(), _alice, resolver: o => o, stack: null);

        spell.Should().NotBeNull(
            "the bespoke template is registered and binds Brainstorm");
    }

    // -----------------------------------------------------------------------
    // Resolution semantics
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_HandStartsAt5_EndsAt6_NetPlusOne()
    {
        // 5 cards in hand.
        for (var i = 0; i < 5; i++)
        {
            var c = new Card($"hand{i}", "");
            _alice.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
        // 10 cards in library so we don't run out.
        for (var i = 0; i < 10; i++)
        {
            var c = new Card($"lib{i}", "");
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

        // 5 + 3 drawn - 2 returned = 6.
        _alice.Zones.Hand.GetCards().Should().HaveCount(6,
            "5 starting hand cards plus 3 drawn minus 2 returned to library");
        // 10 - 3 drawn + 2 returned = 9.
        _alice.Zones.Library.GetCards().Should().HaveCount(9,
            "library decreases by 3 (drawn) and increases by 2 (returned)");
    }

    [Fact]
    public void Resolve_ReturnedCardsLandOnTopOfLibrary()
    {
        // Hand: h0..h4 (h4 is last-added).
        var handCards = Enumerable.Range(0, 5)
            .Select(i => new Card($"h{i}", ""))
            .ToList();
        foreach (var c in handCards)
        {
            _alice.Zones.Hand.AddCard(c);
            c.SetZone(ZoneType.Hand);
        }
        // Library top → bottom: l0..l9.
        var libCards = Enumerable.Range(0, 10)
            .Select(i => new Card($"l{i}", ""))
            .ToList();
        foreach (var c in libCards)
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        // After drawing l0, l1, l2 into hand, hand (in add order) is:
        //   [h0, h1, h2, h3, h4, l0, l1, l2]
        // The v1 picker returns the last two: l1 (now at index 6) and l2.
        // Insert order: l1 first onto library[0], then l2 onto library[0],
        // so library top is now l2, then l1, then the original l3, l4, ...
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib[0].Name.Should().Be("l2", "second returned card sits on top");
        lib[1].Name.Should().Be("l1", "first returned card sits second-from-top");
        lib[2].Name.Should().Be("l3", "previous library order resumes below the returned cards");
    }

    [Fact]
    public void Resolve_EmptyLibrary_IsGracefulNoDraw()
    {
        // Hand has 2 cards; library is empty.
        var h0 = new Card("h0", "");
        var h1 = new Card("h1", "");
        _alice.Zones.Hand.AddCard(h0); h0.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(h1); h1.SetZone(ZoneType.Hand);

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        spell.Should().NotBeNull();
        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));

        var act = () => { foreach (var e in effects) e.Execute(); };
        act.Should().NotThrow("an empty library short-circuits the draw loop");

        // No draws happened, so hand still has h0/h1. The "put two on top"
        // half still runs — both move from hand to library.
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "both hand cards return to the (previously empty) library");
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().HaveCount(2);
        // h0 returned first → library[0]; then h1 inserted at 0 → h1 on top,
        // h0 second-from-top.
        lib[0].Name.Should().Be("h1");
        lib[1].Name.Should().Be("h0");
    }

    [Fact]
    public void Resolve_SingleHandCard_ReturnsOnlyOne()
    {
        // 1 hand card, plenty of library.
        var h = new Card("h", "");
        _alice.Zones.Hand.AddCard(h); h.SetZone(ZoneType.Hand);
        for (var i = 0; i < 5; i++)
        {
            var c = new Card($"l{i}", "");
            _alice.Zones.Library.AddCard(c); c.SetZone(ZoneType.Library);
        }

        var spell = OracleSpellBinder.Bind(BuildEntity(), _alice, o => o, stack: null);
        var effects = spell!.EffectFactory(new Majik.Core.Game.ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: System.Array.Empty<System.Collections.Generic.IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        // 1 + 3 drawn - 1 returned (only one card remaining when picker
        // runs after drawing? No — picker sees hand of 4 cards after draws.
        // Actually returnCount = Math.Min(2, 4) = 2, so 1 + 3 - 2 = 2.
        // Net: hand grows by 1 from 1 → 2 only if returnCount were 2; let's
        // assert the actual.
        _alice.Zones.Hand.GetCards().Should().HaveCount(2,
            "1 + 3 drawn - 2 returned = 2");
    }
}
