using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
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
/// Unit tests for <see cref="BloodbraidMarauderFactory"/> — Bloodbraid
/// Marauder (Modern Horizons 3, {1}{R}, Creature — Human Berserker 3/1).
///
/// Oracle text (verified against Scryfall 2026-05-29):
///   "This creature can't block.
///    Delirium — This spell has cascade as long as there are four or more
///    card types among cards in your graveyard. (When you cast this spell,
///    exile cards from the top of your library until you exile a nonland
///    card that costs less. You may cast it without paying its mana cost.
///    Put the exiled cards on the bottom in a random order.)"
///
/// Covers:
/// - Identity (name, type, subtypes, cost, mana value, P/T, owner/controller).
/// - NamedCardFactory dispatch.
/// - "This creature can't block." (CR 509.1c) — non-expiring CannotBlock
///   restriction registered on the supplied ContinuousEffectsService
///   (mirrors <see cref="GravecrawlerFactory"/>).
/// - Delirium-gated Cascade (CR 702.85 / CR 702.105): the cascade trigger
///   fires on cast ONLY when there are 4+ card types in the controller's
///   graveyard (delirium). Cascade routing mirrors
///   <see cref="ArdentPleaFactory"/> / <see cref="BloodbraidElfFactory"/>;
///   the delirium gate reuses
///   <see cref="DragonsRageChannelerFactory.IsDeliriumActive"/>.
/// - Cascade discovery in <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>.
/// </summary>
public class BloodbraidMarauderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static TriggeredAbility GetCascadeTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    /// <summary>
    /// Seed <paramref name="owner"/>'s graveyard with cards spanning four
    /// distinct card types so delirium (CR 702.105) is satisfied.
    /// </summary>
    private static void SeedDelirium(Player owner)
    {
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        var instant = new Instant("Lightning Bolt", "{R}");
        var sorcery = new Sorcery("Cleansing Wildfire", "{R}");
        var artifact = new Artifact("Ornithopter", "{0}");
        foreach (var card in new ICard[] { creature, instant, sorcery, artifact })
        {
            card.SetOwner(owner);
            owner.Zones.Graveyard.AddCard(card);
            card.SetZone(ZoneType.Graveyard);
        }

        DragonsRageChannelerFactory.IsDeliriumActive(owner).Should().BeTrue(
            "the graveyard now holds four distinct card types — delirium is on.");
    }

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeSubtypesCostBody()
    {
        var card = BloodbraidMarauderFactory.Create(_alice);

        card.Name.Should().Be("Bloodbraid Marauder");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Human).Should().BeTrue();
        card.HasSubtype(CardSubtype.Berserker).Should().BeTrue();

        var creature = card.Should().BeOfType<Creature>().Subject;
        creature.BasePower.Should().Be(3);
        creature.BaseToughness.Should().Be(1);
        creature.ManaCostValue.TotalValue.Should().Be(2);
        creature.Owner.Should().BeSameAs(_alice);
        creature.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BloodbraidMarauder()
    {
        var card = NamedCardFactory.Create("Bloodbraid Marauder", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bloodbraid Marauder");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
    }

    // ── "This creature can't block." ─────────────────────────────────────

    [Fact]
    public void CantBlock_RegistersNonExpiringCannotBlockRestriction()
    {
        // CR 509.1c — a non-expiring CannotBlock restriction scoped to this
        // creature; CombatValidator consults it directly.
        var svc = new ContinuousEffectsService();
        var card = BloodbraidMarauderFactory.Create(_alice, svc);
        card.SetZone(ZoneType.Battlefield);

        svc.HasRestriction(card, CombatRestriction.CannotBlock).Should().BeTrue(
            "Bloodbraid Marauder can't block (CR 509.1c).");
    }

    [Fact]
    public void CantBlock_ShapeOnlyPath_DoesNotThrow()
    {
        // Single-arg path has no effects service — restriction is skipped,
        // card shape is still correct (mirrors Gravecrawler).
        var card = BloodbraidMarauderFactory.Create(_alice);
        card.Name.Should().Be("Bloodbraid Marauder");
    }

    // ── Cascade trigger shape ────────────────────────────────────────────

    [Fact]
    public void HasSingleCascadeTriggeredAbility()
    {
        var card = BloodbraidMarauderFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Bloodbraid Marauder prints one triggered ability — the "
            + "delirium-gated Cascade.");
    }

    [Fact]
    public void CascadeTrigger_ActiveZones_IncludesStack()
    {
        // Cascade fires while the cascading spell is on the stack.
        var card = BloodbraidMarauderFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        trigger.ActiveZones.Should().Contain(ZoneType.Stack);
    }

    // ── Delirium gate on cascade ─────────────────────────────────────────

    [Fact]
    public void Cascade_DeliriumOff_TriggerDoesNotMatch()
    {
        // CR 702.105 — with fewer than 4 card types in the graveyard the
        // spell has no cascade; the trigger must NOT match its own cast.
        var card = BloodbraidMarauderFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;

        cond.Matches(new SpellCastEvent(spell), trigger).Should().BeFalse(
            "delirium is off (empty graveyard) — the spell has no cascade.");
    }

    [Fact]
    public void Cascade_DeliriumOn_TriggerMatchesOwnCast()
    {
        // CR 702.105 — with 4+ card types in the graveyard the spell has
        // cascade; the trigger matches its own cast.
        SeedDelirium(_alice);

        var card = BloodbraidMarauderFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;

        cond.Matches(new SpellCastEvent(spell), trigger).Should().BeTrue(
            "delirium is on (4 card types) — the spell has cascade.");
    }

    [Fact]
    public void Cascade_DeliriumOn_DoesNotMatchOtherSpell()
    {
        SeedDelirium(_alice);

        var card = BloodbraidMarauderFactory.Create(_alice);
        var trigger = GetCascadeTrigger(card);

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;

        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse(
            "cascade is keyed to Bloodbraid Marauder's own cast.");
    }

    [Fact]
    public void Cascade_DeliriumOn_OnCast_InvokesCascadeAction_WithSourceMV2()
    {
        // CR 702.85 — cascade exiles until a nonland card with MV < 2.
        // Library: Mountain (land, bottomed) then Memnite (artifact creature,
        // MV 0, eligible — strictly < 2).
        SeedDelirium(_alice);

        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var memnite = new Creature("Memnite", "{0}", 1, 1);
        memnite.SetOwner(_alice);

        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(memnite);
        memnite.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = BloodbraidMarauderFactory.Create(
            _alice,
            effects: null,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var trigger = GetCascadeTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(memnite);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);

        memnite.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void Cascade_DeliriumOff_OnCast_DoesNotCascade()
    {
        // Delirium off — even if the effect body runs, no cascade happens.
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = BloodbraidMarauderFactory.Create(
            _alice,
            effects: null,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        var trigger = GetCascadeTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        captured.Should().BeNull(
            "delirium is off — the spell has no cascade, so the resolution "
            + "is a no-op even if the effect body executes.");
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    // ── Cascade discovery ────────────────────────────────────────────────

    [Fact]
    public void CascadeDiscovery_DefaultProbeRecognizes_BloodbraidMarauder()
    {
        var card = BloodbraidMarauderFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Bloodbraid Marauder is registered in the cascade ship list "
            + "(it cascades whenever delirium is active).");
    }
}
