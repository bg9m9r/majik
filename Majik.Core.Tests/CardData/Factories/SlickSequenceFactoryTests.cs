using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SlickSequenceFactory"/> (Outlaws of Thunder
/// Junction, {U}{R}).
///
/// Slick Sequence — Instant.
/// Oracle text (verified against Scryfall):
///   "Slick Sequence deals 2 damage to any target. If you've cast another
///    spell this turn, draw a card."
///
/// Covers ONLY the card's unique behaviour (plus a single identity assert):
/// - Identity ({U}{R} Instant) loaded from the embedded JSON def.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 2 damage to a player target (CR 120.3).
/// - Resolve deals 2 damage to a creature target.
/// - Conditional draw (CR 608.2 "If you've cast another spell this turn"),
///   read off the live TurnState.SpellsCastByPlayer tally — which already
///   includes this spell at resolution, so the threshold is &gt;= 2:
///     * Caster has cast another spell (tally 2) → draws a card.
///     * Caster has cast ONLY this spell (tally 1) → no draw.
///     * No TurnState wired (legacy / context-free path) → no draw.
/// </summary>
[Collection(nameof(StaticRegistryCollection))]
[Trait("Color", "M")]
public class SlickSequenceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void SlickSequence_Identity_InstantAtUR()
    {
        var card = SlickSequenceFactory.Create(_alice);

        card.Name.Should().Be("Slick Sequence");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{U}{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void SlickSequence_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void SlickSequence_Resolve_DealsTwoDamageToPlayer()
    {
        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, target: _bob, turnState: null);

        _bob.LifeTotal.Should().Be(18, "Slick Sequence deals 2 damage to any target (CR 120.3)");
    }

    [Fact]
    public void SlickSequence_Resolve_DealsTwoDamageToCreature()
    {
        var wall = MakeWall();
        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, target: wall, turnState: null);

        wall.Damage.Should().Be(2, "Slick Sequence deals 2 damage to target creature");
    }

    // ── Conditional draw ──────────────────────────────────────────────────────

    [Fact]
    public void SlickSequence_AnotherSpellCastThisTurn_DrawsACard()
    {
        // Library: [a, b]. Tally = 2 (this spell + one other) → draw a.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");

        var turnState = new TurnState();
        // Two spells cast this turn by the caster: this spell + another. By
        // resolution Slick Sequence's own cast is already tallied, so a tally
        // of 2 represents "you've cast another spell this turn" (CR 608.2).
        turnState.RecordSpellCast(_alice, EmptyColors);
        turnState.RecordSpellCast(_alice, EmptyColors);

        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, target: _bob, turnState: turnState);

        _bob.LifeTotal.Should().Be(18);
        _alice.Zones.Hand.GetCards().Should().Contain(a,
            "the caster has cast another spell this turn, so Slick Sequence draws a card");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { b },
            "exactly one card was drawn off the top");
    }

    [Fact]
    public void SlickSequence_OnlyThisSpellCastThisTurn_NoDraw()
    {
        // Library: [a, b]. Tally = 1 (only this spell) → no other spell, no draw.
        var a = SeedLibraryCard(_alice, "A");
        var b = SeedLibraryCard(_alice, "B");

        var turnState = new TurnState();
        turnState.RecordSpellCast(_alice, EmptyColors); // only Slick Sequence itself

        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, target: _bob, turnState: turnState);

        _bob.LifeTotal.Should().Be(18);
        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no other spell was cast this turn, so Slick Sequence does not draw");
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a, b },
            "library is untouched when the draw rider is not satisfied");
    }

    [Fact]
    public void SlickSequence_NoTurnStateWired_NoDraw()
    {
        // Legacy / context-free path: null TurnState reads as 0 spells cast,
        // so the conditional draw is skipped (damage still resolves).
        var a = SeedLibraryCard(_alice, "A");

        var def = SlickSequenceFactory.BuildSpellDefinition(_alice, resolver: x => x);
        Resolve(def, target: _bob, turnState: null);

        _bob.LifeTotal.Should().Be(18);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().Equal(new[] { a });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static readonly IReadOnlySet<ManaColor> EmptyColors =
        new HashSet<ManaColor>();

    /// <summary>Resolve the spell's effects against a context carrying the
    /// supplied <paramref name="turnState"/> (CR 608.2 rider reads it live).</summary>
    private void Resolve(
        SpellDefinition def,
        object target,
        TurnState? turnState)
    {
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana:      ManaPayment.Empty);

        var game = new GameContext(
            self: _alice,
            allPlayers: new[] { _alice, _bob },
            activePlayer: _alice,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()),
            landPlayAvailable: true,
            turnState: turnState);
        var ctx = ResolutionContext.For(_alice, agent: null, game: game, chosenTargets: null);

        foreach (var effect in def.EffectFactory(chosen))
        {
            effect.ExecuteAsync(ctx).AsTask().GetAwaiter().GetResult();
        }
    }

    private Creature MakeWall()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);
        return wall;
    }

    private static Card SeedLibraryCard(Player player, string name)
    {
        var c = new Card(name, "");
        c.SetOwner(player);
        player.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
