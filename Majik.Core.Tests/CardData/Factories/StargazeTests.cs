using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="StargazeFactory"/>.
///
/// Card: Stargaze — Sorcery {X}{B}{B} (March of the Machine).
///   "Look at twice X cards from the top of your library. Put X cards from
///    among them into your hand and the rest into your graveyard. You lose X
///    life."
///
/// Covers the card's UNIQUE behaviour (the contract test already asserts
/// dispatch + well-formedness):
///   - Identity (name, type, X-cost {X}{B}{B}, owner/controller).
///   - Resolve looks at 2X, puts X into hand, the rest into the graveyard,
///     and loses X life.
///   - X = 0 is a clean no-op (no look, no move, no life loss — CR 119.4).
///   - A short library (fewer than 2X cards) keeps up to X and bins the rest
///     without throwing.
///   - The production OracleSpellBinder binds the seed oracle text to this
///     body (the real cast path — cards resolve by name via the binder).
/// </summary>
[Trait("Color", "B")]
public class StargazeTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Card NewCardInLibrary(Player owner, string name)
    {
        var c = new Card(name, "{B}");
        c.SetOwner(owner);
        owner.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }

    private static void FillLibrary(Player owner, int count)
    {
        for (var i = 0; i < count; i++) NewCardInLibrary(owner, $"Card{i}");
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Stargaze_Identity()
    {
        var c = StargazeFactory.Create(_alice);

        c.Name.Should().Be("Stargaze");
        c.ManaCost.Should().Be("{X}{B}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve — look 2X, hand X, rest to graveyard, lose X life
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_LooksAtTwiceX_KeepsX_RestToGraveyard_LosesXLife()
    {
        // X = 3: look at 6, keep 3 to hand, 3 to graveyard, lose 3 life.
        FillLibrary(_alice, 10);
        var top = _alice.Zones.Library.GetCards().Take(6).ToList();

        foreach (var e in StargazeFactory.BuildResolveEffect(_alice, x: 3)) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(3, "X cards are put into hand");
        _alice.Zones.Graveyard.Count.Should().Be(3, "the rest of the looked-at cards go to the graveyard");
        _alice.Zones.Library.Count.Should().Be(4, "10 - (2X = 6) looked-at cards remain in the library");
        _alice.LifeTotal.Should().Be(17, "you lose X = 3 life (CR 119.3)");

        // The first X looked-at cards are the ones in hand; the next X are binned.
        _alice.Zones.Hand.GetCards().Should().BeEquivalentTo(top.Take(3));
        _alice.Zones.Graveyard.GetCards().Should().BeEquivalentTo(top.Skip(3).Take(3));
        // The looked-at cards are no longer in the library.
        _alice.Zones.Library.GetCards().Should().NotContain(top);
    }

    [Fact]
    public void Resolve_XZero_IsCleanNoOp()
    {
        FillLibrary(_alice, 5);

        var act = () => { foreach (var e in StargazeFactory.BuildResolveEffect(_alice, x: 0)) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.Count.Should().Be(0, "X = 0 looks at nothing");
        _alice.Zones.Graveyard.Count.Should().Be(0);
        _alice.Zones.Library.Count.Should().Be(5, "the library is untouched");
        _alice.LifeTotal.Should().Be(20, "losing 0 life is not losing life (CR 119.4)");
    }

    [Fact]
    public void Resolve_ShortLibrary_KeepsUpToX_BinsRest_WithoutThrowing()
    {
        // X = 4 wants to look at 8, but only 5 cards exist. Keep up to X = 4,
        // bin the 1 remaining, lose X = 4 life.
        FillLibrary(_alice, 5);

        var act = () => { foreach (var e in StargazeFactory.BuildResolveEffect(_alice, x: 4)) e.Execute(); };

        act.Should().NotThrow();
        _alice.Zones.Hand.Count.Should().Be(4, "keep up to X when fewer than 2X were looked at");
        _alice.Zones.Graveyard.Count.Should().Be(1, "the single remaining looked-at card goes to the graveyard");
        _alice.Zones.Library.Count.Should().Be(0, "all 5 cards were looked at");
        _alice.LifeTotal.Should().Be(16, "you still lose X = 4 life");
    }

    // -----------------------------------------------------------------------
    // Production binding — the seed oracle text resolves through the live
    // OracleSpellBinder (NOT just the factory helper). This is the path the
    // real cast flow takes: cards are resolved AT CAST TIME BY NAME via the
    // binder registry, so a working factory helper is meaningless unless a
    // template binds the printed text to it.
    // -----------------------------------------------------------------------

    [Fact]
    public void ProductionBinder_BindsSeedOracleText_AndDigsAndDrains()
    {
        FillLibrary(_alice, 8);

        var def = OracleSpellBinder.Bind(
            new CardEntity
            {
                Name = "Stargaze",
                ManaCost = "{X}{B}{B}",
                OracleText =
                    "Look at twice X cards from the top of your library. Put X cards from among them into your hand and the rest into your graveyard. You lose X life.",
            },
            _alice, raw => raw, null);

        def.Should().NotBeNull("the binder must recognise Stargaze's printed text");
        def!.HasVariableX.Should().BeTrue("{X}{B}{B} is an X-spell — the cast flow must prompt for X");
        def.TargetRequests.Should().BeEmpty("Stargaze chooses no targets");

        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: 2,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Hand.Count.Should().Be(2, "X = 2 cards go to hand on resolution");
        _alice.Zones.Graveyard.Count.Should().Be(2, "the rest of the 2X = 4 looked-at cards go to the graveyard");
        _alice.Zones.Library.Count.Should().Be(4, "8 - 4 looked-at cards remain");
        _alice.LifeTotal.Should().Be(18, "you lose X = 2 life");
    }
}
