using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using ManaColorEnum = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for the COMBINED split card factory <see cref="DeadGoneFactory"/>
/// (Dead // Gone, {R} // {2}{R}). Both faces are Instants.
///
/// Oracle text (verified against Scryfall 2026-06-02):
///   Dead {R} — "Dead deals 2 damage to target creature."
///   Gone {2}{R} — "Return target creature you don't control to its owner's
///     hand."
///
/// Split cards present each half as its own castable face (CR 712.2 — a split
/// card has two faces on one card; the caster picks one face to cast, and only
/// that face's cost / effect applies). This factory mirrors the two-face
/// posture of <see cref="FireIceFactory"/>: the combined card name is the
/// <c>[CardName]</c> dispatch key (matching the seed row "Dead // Gone"), the
/// card SHAPE is built from the embedded JSON definition, and each face's
/// resolve-time <see cref="Core.Game.SpellDefinition"/> is built on demand.
///
/// Covers:
///   - Combined card identity (Instant, combined name, red, front Dead cost).
///   - <see cref="NamedCardFactory"/> dispatch for the combined name.
///   - Dead face — 2 damage to target creature.
///   - Gone face — return target creature you don't control to owner's hand.
/// </summary>
[Trait("Color", "R")]
public class DeadGoneFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ────────────────────────────────────────────────

    [Fact]
    public void DeadGone_IsInstant_WithDeadFrontFaceCost()
    {
        var card = DeadGoneFactory.Create(_alice);

        card.Name.Should().Be("Dead // Gone");
        card.HasType(CardType.Instant).Should().BeTrue();
        // The combined card carries the front (Dead) face mana cost.
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DeadGone_IsRed()
    {
        var card = DeadGoneFactory.Create(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColorEnum.Red);
    }

    [Fact]
    public void DeadGone_DispatchesViaNamedFactory()
    {
        // NamedCardFactory falls back to a vanilla Card shell for unknown
        // names; an Instant proves the [CardName] dispatch hit this factory.
        var card = Majik.Core.CardData.NamedCardFactory.Create("Dead // Gone", _alice);
        card.Name.Should().Be("Dead // Gone");
        card.Should().BeOfType<Instant>();
        card.HasType(CardType.Instant).Should().BeTrue();
    }

    // ── Dead face — 2 damage to target creature ─────────────────────────────

    [Fact]
    public void DeadFace_TargetsCreature_Deals2Damage()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = DeadGoneFactory.BuildDeadDefinition(resolver: x => x);
        def.TargetRequests.Should().HaveCount(1, "Dead targets one creature");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.Damage.Should().Be(2, "Dead deals 2 damage to the target creature");
    }

    [Fact]
    public void DeadFace_IllegalTarget_Fizzles()
    {
        // A creature that left the battlefield is no longer a legal target
        // (CR 608.2b) — the damage simply does not happen.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Graveyard);

        var def = DeadGoneFactory.BuildDeadDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        creature.Damage.Should().Be(0, "an off-battlefield creature is an illegal target (CR 608.2b)");
    }

    // ── Gone face — bounce a creature you don't control ─────────────────────

    [Fact]
    public void GoneFace_ReturnsOpponentCreatureToOwnersHand()
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        creature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(creature);

        var def = DeadGoneFactory.BuildGoneDefinition(_alice, resolver: x => x);
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);

        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { creature } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _bob.Zones.Hand.GetCards().Should().Contain(creature,
            "Gone returns the creature to its OWNER's hand (CR 701.10)");
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
        creature.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void GoneFace_DoesNotBounceCreatureYouControl()
    {
        // "creature you don't control" — a creature the caster controls is not
        // a legal target, so the bounce does nothing (CR 608.2b / CR 109.5).
        var own = new Creature("Llanowar Elves", "{G}", 1, 1)
        { Owner = _alice, Controller = _alice };
        own.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(own);

        var def = DeadGoneFactory.BuildGoneDefinition(_alice, resolver: x => x);
        var chosen = new ChosenSpellParams(null, null,
            new[] { new object[] { own } }, ManaPayment.Empty);
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        _alice.Zones.Battlefield.GetCards().Should().Contain(own,
            "Gone cannot return a creature you control (CR 109.5)");
        own.Zone.Should().Be(ZoneType.Battlefield);
    }
}
