using FluentAssertions;
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
/// Unit tests for <see cref="ElectrickeryFactory"/> (Return to Ravnica, {R}).
///
/// Electrickery — Instant.
/// Oracle text (verified against Scryfall):
///   "Electrickery deals 1 damage to target creature you don't control.
///    Overload {1}{R} (You may cast this spell for its overload cost. If you
///    do, change \"target\" in its text to \"each.\")"
///   After the overload substitution (CR 702.96b) the overloaded cast reads:
///   "Electrickery deals 1 damage to each creature you don't control."
///
/// Covers:
/// - Identity ({R} Instant, name, owner/controller) loaded from the embedded
///   JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "target creature you don't control"
///   request, no X (default not-overloaded).
/// - Resolve default-not-overloaded → 1 damage to the targeted creature only.
/// - Resolve structural overloaded branch → 1 damage to each creature the
///   controller does NOT control (controller's own creatures untouched,
///   non-creature permanents untouched).
///
/// Overload (CR 702.96) is an alternative cost. As with
/// <see cref="MizziumMortarsFactory"/>, the OverloadAlternativeCost primitive
/// is not yet plumbed through SpellCastFlow, so production casts ship
/// not-overloaded; the overloaded branch is exercised here by passing
/// <c>wasOverloaded: true</c> through the spell-definition builder directly.
/// </summary>
public class ElectrickeryFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Electrickery_Identity_InstantAtR()
    {
        var card = ElectrickeryFactory.Create(_alice);

        card.Name.Should().Be("Electrickery");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Electrickery()
    {
        var card = NamedCardFactory.Create("Electrickery", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Electrickery");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Spell definition shape (default, not overloaded) ──────────────────────

    [Fact]
    public void Electrickery_SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = ElectrickeryFactory.BuildSpellDefinition(
            controller: _alice,
            allPlayers: new[] { _alice, _bob },
            resolver:   x => x,
            wasOverloaded: false);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature you don't control");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Resolution — default (not overloaded) ─────────────────────────────────

    [Fact]
    public void Electrickery_NotOverloaded_DealsOneDamage_ToTargetCreature()
    {
        var target    = NewCreatureOnBattlefield(_bob, "Tarmogoyf", "{1}{G}", 4, 5);
        var bystander  = NewCreatureOnBattlefield(_bob, "Wild Mongrel", "{1}{G}", 2, 2);
        var aliceOwn   = NewCreatureOnBattlefield(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var def = ElectrickeryFactory.BuildSpellDefinition(
            controller: _alice,
            allPlayers: new[] { _alice, _bob },
            resolver:   x => x,
            wasOverloaded: false);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        target.Damage.Should().Be(1, "Electrickery deals 1 damage to the targeted creature");
        bystander.Damage.Should().Be(0, "non-targets are untouched by the printed cast");
        aliceOwn.Damage.Should().Be(0, "controller's creatures are untouched by the printed cast");
    }

    // ── Resolution — overloaded branch ────────────────────────────────────────

    [Fact]
    public void Electrickery_Overloaded_DealsOne_ToEachCreature_YouDontControl()
    {
        // Bob (opponent) creatures — all should take 1.
        var bobOne   = NewCreatureOnBattlefield(_bob, "Memnite", "{0}", 1, 1);
        var bobTwo   = NewCreatureOnBattlefield(_bob, "Goblin Guide", "{R}", 2, 2);

        // Alice (controller) creatures — must NOT take damage (CR 702.96b
        // rewrites to "each creature you don't control"; the spell's
        // controller is the "you" reference per CR 109.5).
        var aliceOne = NewCreatureOnBattlefield(_alice, "Llanowar Elves", "{G}", 1, 1);

        // Non-creature permanent on opponent's side — must not be hit.
        var bobArtifact = new Artifact("Mishra's Bauble", "{0}");
        bobArtifact.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bobArtifact);
        bobArtifact.SetZone(ZoneType.Battlefield);

        var def = ElectrickeryFactory.BuildSpellDefinition(
            controller: _alice,
            allPlayers: new[] { _alice, _bob },
            resolver:   t => t,
            wasOverloaded: true);

        // No targets — overloaded branch carries no TargetRequests
        // (CR 702.96b — "target" is rewritten to "each").
        def.TargetRequests.Count.Should().Be(0);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   Array.Empty<IReadOnlyList<object>>(),
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        bobOne.Damage.Should().Be(1, "Bob's Memnite is an 'each creature you don't control' hit");
        bobTwo.Damage.Should().Be(1, "Bob's Goblin Guide is hit too");
        aliceOne.Damage.Should().Be(0, "Alice (controller) is the 'you'; her creatures are spared");
        bobArtifact.Zone.Should().Be(ZoneType.Battlefield, "non-creature permanents are not affected");

        // SBA-style sanity: 1-toughness creatures took lethal.
        bobOne.IsDead().Should().BeTrue("1 damage on a 1/1 is lethal");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Creature NewCreatureOnBattlefield(
        Player owner, string name, string manaCost, int power, int toughness)
    {
        var c = new Creature(name, manaCost, power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
