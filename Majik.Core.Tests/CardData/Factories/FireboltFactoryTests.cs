using FluentAssertions;
using Majik.Core.Abilities;
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
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FireboltFactory"/> (Odyssey / Eighth Edition, {R}).
///
/// Scryfall oracle (verbatim):
///   "Firebolt deals 2 damage to any target.
///    Flashback {4}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Mirrors <see cref="BumpInTheNightFactoryTests"/> (split-color flashback
/// spike-cycle spell) but the resolve body deals DAMAGE to any target —
/// routed through <see cref="Primitives.Fx.DealDamageAny"/> exactly like
/// <see cref="LightningBoltFactoryTests"/> — rather than life loss.
///
/// Covers:
/// - Identity ({R} Sorcery).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: 1..1 "any target".
/// - Resolve body deals 2 damage to a player target.
/// - Resolve body deals 2 damage to a creature (CR 115.3 — any target).
/// - Resolve body removes loyalty from a planeswalker (CR 306.7).
/// - Flashback cost matches the printed {4}{R} (CR 702.34) and exiles the
///   card after a graveyard cast (CR 702.34b), exercised end-to-end through
///   <see cref="SpellCastFlow"/>.
/// </summary>
public class FireboltFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Firebolt_Identity_SorceryAtR()
    {
        var firebolt = FireboltFactory.Create(_alice);

        firebolt.Name.Should().Be("Firebolt");
        firebolt.HasType(CardType.Sorcery).Should().BeTrue();
        firebolt.ManaCost.ToString().Should().Be("{R}");
        firebolt.Owner.Should().BeSameAs(_alice);
        firebolt.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Firebolt_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Firebolt", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Firebolt");
        card.HasType(CardType.Sorcery).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Spell definition — single "any target" request
    // -----------------------------------------------------------------------

    [Fact]
    public void Firebolt_SpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = FireboltFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
    }

    [Fact]
    public void Firebolt_Resolve_DealsTwoDamageToPlayer()
    {
        var def = FireboltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { _bob },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        _bob.LifeTotal.Should().Be(18, "Firebolt deals 2 damage to any target");
    }

    [Fact]
    public void Firebolt_Resolve_DealsTwoDamageToCreature()
    {
        // 0/4 wall so 2 damage is non-lethal — verifies the damage marker is
        // applied without an SBA wipe interfering.
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = FireboltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { wall },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        wall.Damage.Should().Be(2, "Firebolt deals 2 damage to target creature");
    }

    [Fact]
    public void Firebolt_Resolve_RemovesLoyaltyFromPlaneswalker()
    {
        // CR 306.7 — damage to a planeswalker becomes loyalty removal.
        var walker = new Planeswalker("Test Walker", "{2}{B}", 3,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Liliana });
        walker.SetOwner(_bob);
        walker.SetController(_bob);
        walker.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(walker);

        var def = FireboltFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[]
            {
                (IReadOnlyList<object>)new object[] { walker },
            },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var effect in effects) effect.Execute();

        walker.Loyalty.Should().Be(1,
            "Firebolt to a 3-loyalty planeswalker removes 2 loyalty counters (CR 306.7)");
    }

    // -----------------------------------------------------------------------
    // Flashback cost shape — CR 702.34
    // -----------------------------------------------------------------------

    [Fact]
    public void Firebolt_FlashbackCost_IsFourGenericPlusRed()
    {
        var cost = FireboltFactory.BuildFlashbackCost();

        cost.AlternativeManaCost.IsZero.Should().BeFalse();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{4}{R}"),
            "printed flashback cost is {4}{R} (CR 702.34)");
    }

    // -----------------------------------------------------------------------
    // End-to-end flashback cast — full SpellCastFlow
    // -----------------------------------------------------------------------

    /// <summary>
    /// End-to-end: cast Firebolt from Alice's graveyard via its {4}{R}
    /// flashback cost; on resolution Bob takes 2 damage, and the card is
    /// exiled post-resolution (CR 702.34b).
    /// </summary>
    [Fact]
    public async Task Firebolt_FlashbackCast_FullPath_DealsTwoDamage_ThenExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        // Firebolt in Alice's graveyard.
        var firebolt = FireboltFactory.Create(_alice);
        firebolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(firebolt);

        var def = FireboltFactory.BuildSpellDefinition(resolver: x => x);
        var altCost = FireboltFactory.BuildFlashbackCost();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);

        var spell = await flow.CastAsync(
            _alice, firebolt, def, agent, ctx,
            alternativeCost: altCost);

        // Firebolt on the stack now (flashback move out of graveyard).
        firebolt.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Target took 2 damage.
        _bob.LifeTotal.Should().Be(18);

        // CR 702.34b — flashback exiles the card after resolution, NOT graveyard.
        firebolt.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(firebolt);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(firebolt);
    }
}
