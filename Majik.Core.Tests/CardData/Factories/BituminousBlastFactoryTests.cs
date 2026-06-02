using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BituminousBlastFactory"/> — Bituminous Blast
/// (Alara Reborn, {3}{B}{R}, Instant).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)
///    Bituminous Blast deals 4 damage to target creature."
///
/// Covers (combines the Ardent Plea cascade posture + Abrade/Play with Fire
/// burn posture):
/// - Identity ({3}{B}{R} Instant, name, mana value 5, owner/controller) loaded
///   from the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Single Cascade triggered ability (CR 702.85), ActiveZones = { Stack },
///   condition matches only this card's SpellCastEvent, routing through
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 5.
/// - Cascade discovery in <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>.
/// - Spell definition shape: single 1..1 "target creature" request, no X.
/// - Resolve deals 4 damage to a creature target (CR 120.3).
/// - Resolve no-ops when the target is no longer a creature on the battlefield
///   (CR 608.2b).
/// </summary>
[Trait("Color", "M")]
public class BituminousBlastFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    private static TriggeredAbility GetCascadeTrigger(Instant c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = BituminousBlastFactory.Create(_alice);

        card.Name.Should().Be("Bituminous Blast");
        card.ManaCost.Should().Be("{3}{B}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(5);
    }
    // ── Cascade trigger ──────────────────────────────────────────────────

    [Fact]
    public void HasSingleCascadeTriggeredAbility()
    {
        var card = BituminousBlastFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Bituminous Blast prints one triggered ability — Cascade.");
    }

    [Fact]
    public void CascadeTrigger_ActiveZones_IncludesStack()
    {
        // Cascade fires while the cascading spell (an instant) is on the stack
        // — same posture as Ardent Plea / Bloodbraid Elf.
        var card = BituminousBlastFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        trigger.ActiveZones.Should().Contain(ZoneType.Stack);
    }

    [Fact]
    public void CascadeTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = BituminousBlastFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV5()
    {
        // CR 702.85 — cascade is type-agnostic. Library: Mountain (land,
        // bottomed) then Maelstrom Pulse (MV 3, eligible — strictly < 5).
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var pulse = new Sorcery("Maelstrom Pulse", "{1}{B}{G}");
        pulse.SetOwner(_alice);

        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(pulse);
        pulse.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = BituminousBlastFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var trigger = GetCascadeTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(pulse,
            "Maelstrom Pulse (MV 3) is strictly less than Bituminous Blast's MV 5.");
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);

        pulse.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    // ── Cascade discovery ────────────────────────────────────────────────

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_BituminousBlast()
    {
        var card = BituminousBlastFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Bituminous Blast is registered in the cascade ship list.");
    }

    // ── Spell definition shape ────────────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest_NoX()
    {
        var def = BituminousBlastFactory.BuildSpellDefinition(targetResolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
        def.HasVariableX.Should().BeFalse();
    }

    // ── Damage ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DealsFourDamageToCreature()
    {
        var wall = new Creature("Wall of Wood", "{G}", 0, 4,
            Array.Empty<CardSupertype>(), new[] { CardSubtype.Wall });
        wall.SetOwner(_bob);
        wall.SetController(_bob);
        wall.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(wall);

        var def = BituminousBlastFactory.BuildSpellDefinition(targetResolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { wall } },
            Mana:      ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        wall.Damage.Should().Be(4, "Bituminous Blast deals 4 damage to target creature (CR 120.3)");
    }

    [Fact]
    public void Resolve_TargetNoLongerOnBattlefield_NoOps()
    {
        // CR 608.2b — resolution-time legality re-check. A creature that has
        // left the battlefield is no longer a legal target; resolve no-ops.
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Graveyard);

        var def = BituminousBlastFactory.BuildSpellDefinition(targetResolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X:         null,
            Targets:   new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana:      ManaPayment.Empty);

        Action act = () => { foreach (var effect in def.EffectFactory(chosen)) effect.Execute(); };

        act.Should().NotThrow();
        bear.Damage.Should().Be(0, "the target is no longer on the battlefield, so no damage is dealt");
    }
}
