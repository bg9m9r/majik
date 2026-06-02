using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Unit tests for <see cref="RendingVolleyFactory"/> (Dragons of Tarkir, {R}).
///
/// Rending Volley — Instant.
/// Oracle text (verified against Scryfall):
///   "This spell can't be countered.
///    Rending Volley deals 4 damage to target white or blue creature."
///
/// Covers:
/// - Identity ({R} Instant, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - "Can't be countered" keyword marker attached to the card shape
///   (same posture as <see cref="AbruptDecayFactory"/>).
/// - Spell definition shape: single 1..1 "target white or blue creature"
///   request, no X.
/// - Resolve deals 4 damage to a white creature (CR 119).
/// - Resolve deals 4 damage to a blue creature.
/// - Resolve no-ops against a creature that is neither white nor blue
///   (CR 608.2b — illegal target at resolution).
/// - Resolve no-ops against a non-Creature target.
/// </summary>
[Trait("Color", "R")]
public class RendingVolleyFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Identity_InstantAtR()
    {
        var card = RendingVolleyFactory.Create(_alice);

        card.Name.Should().Be("Rending Volley");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void HasCantBeCounteredKeyword()
    {
        var card = RendingVolleyFactory.Create(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .Should().Contain(RendingVolleyFactory.CantBeCounteredMarker,
                "Rending Volley reads \"This spell can't be countered.\"");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleWhiteOrBlueCreatureTargetRequest_NoX()
    {
        var def = RendingVolleyFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target white or blue creature");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DealsFourDamageToWhiteCreature()
    {
        var bear = MakeCreature("White Bear", "{2}{W}", 2, 2);

        ResolveAgainst(bear);

        bear.Damage.Should().Be(4, "Rending Volley deals 4 damage to target white or blue creature");
    }

    [Fact]
    public void Resolve_DealsFourDamageToBlueCreature()
    {
        var merfolk = MakeCreature("Blue Merfolk", "{1}{U}", 1, 1);

        ResolveAgainst(merfolk);

        merfolk.Damage.Should().Be(4);
    }

    [Fact]
    public void Resolve_NoOps_AgainstCreatureThatIsNotWhiteOrBlue()
    {
        // Green creature — neither white nor blue, so the effect does nothing
        // (CR 608.2b — illegal target at resolution).
        var beast = MakeCreature("Green Beast", "{3}{G}", 4, 4);

        ResolveAgainst(beast);

        beast.Damage.Should().Be(0, "a green creature is not a legal target — no damage is dealt");
    }

    [Fact]
    public void Resolve_NoOps_AgainstNonCreatureTarget()
    {
        var def = RendingVolleyFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        Action act = () => { foreach (var e in def.EffectFactory(chosen)) e.Execute(); };

        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20, "a player is not a legal target for Rending Volley");
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

    private static void ResolveAgainst(Creature target)
    {
        var def = RendingVolleyFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana: ManaPayment.Empty);

        foreach (var e in def.EffectFactory(chosen)) e.Execute();
    }
}
