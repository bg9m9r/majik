using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="HeatedDebateFactory"/> (Outlaws of Thunder
/// Junction, {2}{R}).
///
/// Heated Debate — Instant.
/// Oracle text (verified against Scryfall):
///   "This spell can't be countered. (This includes by the ward ability.)
///    Heated Debate deals 4 damage to target creature or planeswalker."
///
/// Covers (the card's UNIQUE behaviour only — CardFactoryContractTests already
/// asserts NamedCardFactory dispatch + well-formedness):
/// - Identity: {2}{R} (non-vanilla mana cost).
/// - "Can't be countered" keyword marker (CR 701.5b; the ward parenthetical is
///   reminder text per CR 207.2).
/// - Spell definition shape: single 1..1 "target creature or planeswalker"
///   request, no X.
/// - Resolve deals 4 damage to a creature (CR 119).
/// - Resolve deals 4 damage (loyalty loss) to a planeswalker (CR 306.7).
/// - Resolve no-ops against a non-creature/non-planeswalker target
///   (CR 608.2b — illegal target at resolution).
/// </summary>
[Trait("Color", "R")]
public class HeatedDebateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_InstantAt2R()
    {
        var card = HeatedDebateFactory.Create(_alice);

        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{2}{R}");
    }

    [Fact]
    public void HasCantBeCounteredKeyword()
    {
        var card = HeatedDebateFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(HeatedDebateFactory.CantBeCounteredMarker,
                "Heated Debate reads \"This spell can't be countered.\"");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleCreatureOrPlaneswalkerTargetRequest_NoX()
    {
        var def = HeatedDebateFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature or planeswalker");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DealsFourDamageToCreature()
    {
        var bear = MakeCreature("Grizzly Bears", "{1}{G}", 2, 2);

        ResolveAgainst(bear);

        bear.Damage.Should().Be(4, "Heated Debate deals 4 damage to target creature or planeswalker");
    }

    [Fact]
    public void Resolve_DealsFourLoyaltyDamageToPlaneswalker()
    {
        var pw = new Planeswalker("Test Walker", "{4}", 5,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        pw.SetOwner(_bob);
        pw.SetController(_bob);
        pw.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(pw);

        ResolveAgainst(pw);

        pw.Loyalty.Should().Be(1, "4 damage to a planeswalker removes 4 loyalty (CR 306.7)");
    }

    [Fact]
    public void Resolve_NoOps_AgainstNonCreatureNonPlaneswalkerTarget()
    {
        var def = HeatedDebateFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        Action act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "a player is not a legal target for Heated Debate");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private Creature MakeCreature(string name, string cost, int power, int toughness)
    {
        var c = new Creature(name, cost, power, toughness,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        c.SetOwner(_bob);
        c.SetController(_bob);
        c.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void ResolveAgainst(object target)
    {
        var def = HeatedDebateFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
