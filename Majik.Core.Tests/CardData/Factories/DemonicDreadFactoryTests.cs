using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DemonicDreadFactory"/> — Demonic Dread
/// (Alara Reborn, {1}{B}{R}, Sorcery).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)
///    Target creature can't block this turn."
///
/// Covers:
/// - Identity (name, type, cost, mana value, owner/controller).
/// - NamedCardFactory dispatch.
/// - Cascade triggered ability (CR 702.85) firing on cast, type-agnostic
///   (sorcery cascade source), routing through
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 3
///   (mirrors <see cref="ViolentOutburstFactory"/>).
/// - Cascade discovery in <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>.
/// - "Target creature can't block this turn" (CR 509.1c) — single-target
///   <see cref="CombatRestrictionEffect"/> registered on the target, EOT
///   scoped (mirrors <see cref="EarthshakerKhenraFactory"/>).
/// </summary>
[Trait("Color", "M")]
public class DemonicDreadFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeCreature(Player owner, string name = "Grizzly Bears")
    {
        var c = new Creature(name, "1G", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static TriggeredAbility GetCascadeTrigger(Sorcery c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = DemonicDreadFactory.Create(_alice);

        card.Name.Should().Be("Demonic Dread");
        card.ManaCost.Should().Be("{1}{B}{R}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(3);
    }
    // ── Cascade trigger ──────────────────────────────────────────────────

    [Fact]
    public void HasExactlyOneCascadeTrigger()
    {
        var card = DemonicDreadFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Count(t => t.Condition is EventTriggerCondition<SpellCastEvent>)
            .Should().Be(1, "Demonic Dread prints exactly one triggered ability — Cascade.");
    }

    [Fact]
    public void CascadeTrigger_ActiveZones_IncludesStack()
    {
        // Cascade fires while the cascading spell is on the stack — same
        // posture as Violent Outburst / Crashing Footfalls.
        var card = DemonicDreadFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        trigger.ActiveZones.Should().Contain(ZoneType.Stack);
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV3()
    {
        // CR 702.85 — cascade is type-agnostic. Library: Plains (land,
        // bottomed) then Lightning Helix (MV 2, eligible — strictly < 3).
        var plains = NamedCardFactory.Create("Plains", _alice);
        var helix = new Sorcery("Lightning Helix", "{R}{W}");
        helix.SetOwner(_alice);

        _alice.Zones.Library.AddCard(plains);
        plains.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(helix);
        helix.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = DemonicDreadFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var trigger = GetCascadeTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(helix);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(plains);

        helix.Zone.Should().Be(ZoneType.Exile);
        plains.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void CascadeTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = DemonicDreadFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ── Cascade discovery ────────────────────────────────────────────────

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_DemonicDread()
    {
        var card = DemonicDreadFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Demonic Dread is registered in the cascade ship list.");
    }

    // ── Target creature can't block ──────────────────────────────────────

    [Fact]
    public void SpellDefinition_HasSingleTargetCreatureRequest()
    {
        var def = DemonicDreadFactory.BuildDefinition(_alice, o => o);

        def.TargetRequests.Should().ContainSingle();
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Be("target creature");
    }

    [Fact]
    public void Resolve_TargetCreature_GetsCannotBlockRestriction()
    {
        // CR 509.1c — chosen creature can't be declared as a blocker this
        // turn.
        var svc = new ContinuousEffectsService();
        var target = MakeCreature(_bob, "Grizzly Bears");
        target.ActiveEffects = svc;

        DemonicDreadFactory.ApplyCannotBlock(target);

        svc.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeTrue(
            "Demonic Dread makes the target creature unable to block this turn.");
    }

    [Fact]
    public void Resolve_IllegalTarget_OffBattlefield_NoOp()
    {
        // CR 608.2b — target no longer on the battlefield at resolution;
        // restriction is skipped.
        var svc = new ContinuousEffectsService();
        var target = MakeCreature(_bob, "Grizzly Bears");
        target.ActiveEffects = svc;
        target.SetZone(ZoneType.Graveyard);

        DemonicDreadFactory.ApplyCannotBlock(target);

        svc.HasRestriction(target, CombatRestriction.CannotBlock).Should().BeFalse(
            "an illegal (off-battlefield) target fizzles per CR 608.2b.");
    }

    [Fact]
    public void Resolve_NonCreatureTarget_NoOp()
    {
        // A non-Creature resolved token (e.g. a land) is ignored.
        var land = NamedCardFactory.Create("Plains", _alice);
        land.SetZone(ZoneType.Battlefield);

        // Should not throw — simply no-op.
        var act = () => DemonicDreadFactory.ApplyCannotBlock(land);
        act.Should().NotThrow();
    }
}
