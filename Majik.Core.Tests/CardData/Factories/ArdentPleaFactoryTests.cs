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
/// Unit tests for <see cref="ArdentPleaFactory"/> — Ardent Plea
/// (Alara Reborn, {1}{W}{U}, Enchantment).
///
/// Oracle text:
///   "Exalted (Whenever a creature you control attacks alone, that creature
///    gets +1/+1 until end of turn.)
///    Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)"
///
/// Covers:
/// - Identity (name, type, cost, mana value, owner/controller).
/// - NamedCardFactory dispatch.
/// - Exalted keyword marker (CR 702.90) + Exalted trigger pumping the solo
///   attacker +1/+1 EOT (mirrors <see cref="IgnobleHierarchFactory"/>).
/// - Cascade triggered ability (CR 702.85) firing on cast, type-agnostic
///   (enchantment cascade source), routing through
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 3
///   (mirrors <see cref="ViolentOutburstFactory"/>).
/// - Cascade discovery in <see cref="CascadeAltCostProbe.DefaultIsCascadeCard"/>.
/// </summary>
public class ArdentPleaFactoryTests
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

    private static TriggeredAbility GetExaltedTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<CreatureAttacksEvent>);

    private static TriggeredAbility GetCascadeTrigger(Enchantment c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<SpellCastEvent>);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = ArdentPleaFactory.Create(_alice);

        card.Name.Should().Be("Ardent Plea");
        card.ManaCost.Should().Be("{1}{W}{U}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        card.ManaCostValue.TotalValue.Should().Be(3);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_ArdentPlea()
    {
        var card = NamedCardFactory.Create("Ardent Plea", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Ardent Plea");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
    }

    // ── Exalted keyword marker ────────────────────────────────────────────

    [Fact]
    public void HasExaltedKeywordMarker()
    {
        var card = ArdentPleaFactory.Create(_alice);

        var exalted = card.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => k.Keyword == "Exalted");

        exalted.Should().NotBeNull("Exalted keyword marker must be present (CR 702.90).");
    }

    [Fact]
    public void HasTwoTriggeredAbilities_ExaltedAndCascade()
    {
        var card = ArdentPleaFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "Ardent Plea prints two triggered abilities — Exalted and Cascade.");
    }

    // ── Exalted trigger ──────────────────────────────────────────────────

    [Fact]
    public void Exalted_SoloAttacker_GetsPumped()
    {
        // CR 702.90 — attacker attacks alone; should get +1/+1 EOT.
        var svc = new ContinuousEffectsService();

        var attacker = MakeCreature(_alice, "Grizzly Bears");
        attacker.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker };

        var card = ArdentPleaFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        card.SetZone(ZoneType.Battlefield);

        var trigger = GetExaltedTrigger(card);
        trigger.IsTriggered(new CreatureAttacksEvent(attacker, _bob)).Should().BeTrue(
            "the exalted trigger fires whenever a creature Alice controls attacks.");

        foreach (var e in trigger.Effects) e.Execute();

        attacker.GetPower().Should().Be(2 + 1,
            "Exalted gives the solo attacker +1/+1 until end of turn.");
        attacker.GetToughness().Should().Be(2 + 1);
    }

    [Fact]
    public void Exalted_TwoAttackers_NoPump()
    {
        // CR 702.90b — "attacks alone" requires no other controlled attackers.
        var svc = new ContinuousEffectsService();

        var attacker1 = MakeCreature(_alice, "Bear Alpha");
        var attacker2 = MakeCreature(_alice, "Bear Beta");
        attacker1.ActiveEffects = svc;
        attacker2.ActiveEffects = svc;

        var attackers = new List<Creature> { attacker1, attacker2 };

        var card = ArdentPleaFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: () => attackers);
        card.SetZone(ZoneType.Battlefield);

        var trigger = GetExaltedTrigger(card);
        foreach (var e in trigger.Effects) e.Execute();

        attacker1.GetPower().Should().Be(2, "two attackers — not alone, no pump.");
        attacker2.GetPower().Should().Be(2);
    }

    // ── Cascade trigger ──────────────────────────────────────────────────

    [Fact]
    public void CascadeTrigger_ActiveZones_IncludesStack()
    {
        // Cascade fires while the cascading spell (an enchantment) is on the
        // stack — same posture as Violent Outburst / Crashing Footfalls.
        var card = ArdentPleaFactory.Create(_alice);
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
        var card = ArdentPleaFactory.Create(
            _alice,
            triggers: null,
            attackingCreaturesSource: null,
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
        var card = ArdentPleaFactory.Create(_alice);
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
    public void CascadeDiscovery_DefaultProbeRecognizes_ArdentPlea()
    {
        var card = ArdentPleaFactory.Create(_alice);

        CascadeAltCostProbe.DefaultIsCascadeCard(card).Should().BeTrue(
            "Ardent Plea is registered in the cascade ship list.");
    }
}
