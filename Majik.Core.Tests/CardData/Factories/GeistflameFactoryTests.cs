using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
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
/// Unit tests for <see cref="GeistflameFactory"/> (Innistrad, {R}).
///
/// Geistflame — Instant.
/// Oracle text (verified against Scryfall):
///   "Geistflame deals 1 damage to any target.
///    Flashback {3}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Geistflame = Lava Dart's "1 damage to any target" body (CR 115.3 /
/// CR 120.3) composed with the mana-cost Flashback rider (CR 702.34) — the
/// same {N}{R}-flashback shape as Bump in the Night.
///
/// Covers:
/// - Identity ({R} Instant, name, owner / controller) loaded from the
///   embedded JSON def via <see cref="Majik.Core.CardData.Definitions.CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Spell definition shape: single 1..1 "any target" request, no X.
/// - Resolve deals 1 damage to a player target (CR 120.3).
/// - Resolve deals 1 damage to a creature target.
/// - Flashback cost matches the printed {3}{R} (CR 702.34) and exiles the
///   card after a graveyard cast (CR 702.34b), exercised end-to-end through
///   <see cref="SpellCastFlow"/>.
/// </summary>
public class GeistflameFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity + dispatch ───────────────────────────────────────────────────

    [Fact]
    public void Geistflame_Identity_InstantAtR()
    {
        var card = GeistflameFactory.Create(_alice);

        card.Name.Should().Be("Geistflame");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Geistflame()
    {
        var card = NamedCardFactory.Create("Geistflame", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Geistflame");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.ToString().Should().Be("{R}");
    }

    // ── Spell definition shape ────────────────────────────────────────────────

    [Fact]
    public void Geistflame_SpellDefinition_HasSingleAnyTargetRequest_NoX()
    {
        var def = GeistflameFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("any target");
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    // ── Damage ────────────────────────────────────────────────────────────────

    [Fact]
    public void Geistflame_Resolve_DealsOneDamageToPlayer()
    {
        var def = GeistflameFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        _bob.LifeTotal.Should().Be(19, "Geistflame deals 1 damage to any target (CR 120.3)");
    }

    [Fact]
    public void Geistflame_Resolve_DealsOneDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = GeistflameFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(1, "Geistflame deals 1 damage to target creature");
    }

    // ── Flashback cost shape — CR 702.34 ──────────────────────────────────────

    [Fact]
    public void Geistflame_FlashbackCost_IsThreeGenericPlusRed()
    {
        var cost = GeistflameFactory.BuildFlashbackCost();

        cost.AlternativeManaCost.IsZero.Should().BeFalse();
        cost.AlternativeManaCost.Should().Be(ManaCost.Parse("{3}{R}"),
            "printed flashback cost is {3}{R} (CR 702.34)");
    }

    // ── End-to-end flashback cast — full SpellCastFlow ────────────────────────

    /// <summary>
    /// End-to-end: cast Geistflame from Alice's graveyard via its {3}{R}
    /// flashback cost; on resolution Bob takes 1 damage, and the card is
    /// exiled post-resolution (CR 702.34b).
    /// </summary>
    [Fact]
    public async Task Geistflame_FlashbackCast_FullPath_DealsOne_ThenExiled()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);
        var flow = new SpellCastFlow(stack, new ZoneService(bus), bus);

        var geistflame = GeistflameFactory.Create(_alice);
        geistflame.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(geistflame);

        var def = GeistflameFactory.BuildSpellDefinition(resolver: x => x);
        var altCost = GeistflameFactory.BuildFlashbackCost();

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1,
            PhaseStateType.PreCombatMain, stack);

        var spell = await flow.CastAsync(
            _alice, geistflame, def, agent, ctx,
            alternativeCost: altCost);

        geistflame.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        _bob.LifeTotal.Should().Be(19, "Geistflame deals 1 damage to any target");

        // CR 702.34b — flashback exiles the card after resolution, NOT graveyard.
        geistflame.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(geistflame);
        _alice.Zones.Graveyard.GetCards().Should().NotContain(geistflame);
    }
}
