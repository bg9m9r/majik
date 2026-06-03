using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// End-to-end tests for Fireblast (Visions / Tempest, {4}{R}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "You may sacrifice two Mountains rather than pay this spell's mana cost.
///    Fireblast deals 4 damage to any target."
///
/// Covers:
/// - Identity ({4}{R}{R} Instant, name, owner/controller, mana value 6)
///   loaded from the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 4 damage to a player target (CR 120.3).
/// - Resolve deals 4 damage to a creature target.
/// - Pitch cast via the "sacrifice two Mountains" alternative cost
///   (<see cref="SacrificeTwoLandsAlternativeCost"/>) — both Mountains move
///   Battlefield → Graveyard, no mana spent, and the spell still deals 4
///   damage on resolution.
/// - The alt-cost rejects when fewer than two Mountains are supplied / the
///   subtype or controller predicate fails.
/// </summary>
[Trait("Color", "R")]
public class FireblastFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public FireblastFactoryTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    // ── Identity ─────────────────────────────────────────────────────────────

    [Fact]
    public void Fireblast_Identity_InstantAtFourRR()
    {
        var card = FireblastFactory.Create(_alice);

        card.Name.Should().Be("Fireblast");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{4}{R}{R}");
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Fireblast_ManaValue_IsSix()
    {
        var card = FireblastFactory.Create(_alice);

        card.ManaCostValue.TotalValue.Should().Be(6);
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void Fireblast_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = FireblastFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void Fireblast_Resolve_DealsFourDamageToPlayer()
    {
        var def = FireblastFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(16, "Fireblast deals 4 damage to any target (CR 120.3)");
    }

    [Fact]
    public void Fireblast_Resolve_DealsFourDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 5,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = FireblastFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(4, "Fireblast deals 4 damage to target creature");
    }

    // ── Alternative cost: sacrifice two Mountains (CR 118.9) ──────────────────

    [Fact]
    public async Task CastViaPitch_SacrificesTwoMountains_NoManaPaid_StillDeals4()
    {
        // Alice has Fireblast in hand + two Mountains. Casts via the
        // "sacrifice two Mountains" alt cost; both Mountains go to her
        // graveyard, no mana is spent, and on resolution Bob takes 4.
        var fireblast = FireblastFactory.Create(_alice);
        fireblast.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(fireblast);

        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Mountain", _alice);
        foreach (var m in new[] { m1, m2 })
        {
            ((Card)m).SetController(_alice);
            m.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(m);
        }

        var startingMana = _alice.ManaPool.Total;

        var pitchCost = FireblastFactory.BuildSacrificeMountainsCost(new[] { m1, m2 });

        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)_bob });
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _bob, 2, StepStateType.PreCombatMain, _stack);

        await _flow.CastAsync(
            _alice, fireblast,
            FireblastFactory.BuildSpellDefinition(o => o),
            agent, ctx,
            alternativeCost: pitchCost);

        _resolver.ResolveTop(_stack);

        m1.Zone.Should().Be(ZoneType.Graveyard, because: "the pitch cost sacrifices both Mountains");
        m2.Zone.Should().Be(ZoneType.Graveyard, because: "the pitch cost sacrifices both Mountains");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(m1).And.NotContain(m2);
        _alice.ManaPool.Total.Should().Be(startingMana, because: "pitch pays no mana");
        _bob.LifeTotal.Should().Be(16, because: "Fireblast still deals 4 damage when cast via its alt cost");
    }

    [Fact]
    public void SacrificeMountainsCost_Rejects_WhenFewerThanTwoMountains()
    {
        var fireblast = FireblastFactory.Create(_alice);

        var m1 = NamedCardFactory.Create("Mountain", _alice);
        ((Card)m1).SetController(_alice);
        m1.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(m1);

        // Only one Mountain supplied — alt cost rejected.
        var oneMountain = FireblastFactory.BuildSacrificeMountainsCost(new[] { m1 });
        oneMountain.CanCastFor(fireblast, _alice).Should().BeFalse();
    }

    [Fact]
    public void SacrificeMountainsCost_Rejects_WhenWrongSubtypeOrController()
    {
        var fireblast = FireblastFactory.Create(_alice);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        ((Card)mountain).SetController(_alice);
        mountain.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mountain);

        // A Forest is not a Mountain — subtype predicate fails.
        var forest = NamedCardFactory.Create("Forest", _alice);
        ((Card)forest).SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(forest);

        var wrongSubtype = FireblastFactory.BuildSacrificeMountainsCost(new[] { mountain, forest });
        wrongSubtype.CanCastFor(fireblast, _alice).Should().BeFalse();

        // Bob controls the second Mountain — controller predicate fails.
        var bobMountain = NamedCardFactory.Create("Mountain", _bob);
        ((Card)bobMountain).SetController(_bob);
        bobMountain.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobMountain);

        var wrongController = FireblastFactory.BuildSacrificeMountainsCost(new[] { mountain, bobMountain });
        wrongController.CanCastFor(fireblast, _alice).Should().BeFalse();
    }
}
