using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Crashing Footfalls (Modern Horizons, {1}{R}{G}{W}, Sorcery).
///
/// Covers:
/// - Identity (name, type, cost, mana value).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Resolve effect (two 4/4 Rhino warrior tokens with Trample).
/// - Cascade trigger fires on cast and routes through
///   <see cref="CascadeAction.Cascade"/> with sourceManaValue = 4.
/// - Cascade with only land/expensive cards yields no free cast.
/// </summary>
public class CrashingFootfallsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Identity_NameTypeCost()
    {
        var card = CrashingFootfallsFactory.Create(_alice);

        card.Name.Should().Be("Crashing Footfalls");
        card.ManaCost.Should().Be("{1}{R}{G}{W}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
        card.Controller.Should().Be(_alice);
        card.ManaCostValue.TotalValue.Should().Be(4);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_CrashingFootfalls()
    {
        var card = NamedCardFactory.Create("Crashing Footfalls", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Crashing Footfalls");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().Be(_alice);
    }

    [Fact]
    public void Card_HasCascadeTriggeredAbility()
    {
        var card = CrashingFootfallsFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Crashing Footfalls prints one triggered ability — Cascade.");
    }

    [Fact]
    public void Resolve_CreatesTwo4_4RhinoWarriorTokensWithTrample()
    {
        // The resolve effect doesn't need a SpellDefinition prompt path —
        // we exercise BuildSpellDefinition + EffectFactory directly so the
        // test mirrors what SpellCastFlow would invoke on resolution.
        var def = CrashingFootfallsFactory.BuildSpellDefinition(_alice);
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        // Drive resolution.
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Rhino))
            .ToList();

        tokens.Should().HaveCount(2);
        foreach (var t in tokens)
        {
            t.Power.Should().Be(4);
            t.Toughness.Should().Be(4);
            t.HasSubtype(CardSubtype.Rhino).Should().BeTrue();
            t.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
            t.Abilities.OfType<KeywordAbility>()
                .Should().Contain(k => k.Keyword == "Trample");
        }
    }

    [Fact]
    public void CascadeTrigger_OnCast_InvokesCascadeAction_WithSourceMV4()
    {
        // Library setup: Mountain on top (will be bottomed), then Lava Spike
        // (MV 1, eligible). Cascade sees Mountain → not eligible (land);
        // exiles Spike → eligible (MV 1 < 4).
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var spike = new Sorcery("Lava Spike", "{R}");
        spike.SetOwner(_alice);
        _alice.Zones.Library.AddCard(mountain);
        mountain.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(spike);
        spike.SetZone(ZoneType.Library);

        CascadeAction.CascadeResult? captured = null;
        var card = CrashingFootfallsFactory.Create(
            _alice,
            triggers: null,
            willCast: _ => true,
            onCascadeResolved: r => captured = r);

        // Pull the cascade trigger out and resolve its effect directly —
        // this exercises the wired effect closure the same way TriggerManager
        // would when the SpellCastEvent fires for this card.
        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeSameAs(spike);
        captured.Exiled.Should().HaveCount(2);
        captured.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);

        // Spike sitting in exile, ready for the caller to drive a
        // CastFromExileAlternativeCost cast.
        spike.Zone.Should().Be(ZoneType.Exile);
        mountain.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void CascadeTrigger_LibraryOnlyLandsOrExpensive_NoFreeCast_FootfallsStillResolves()
    {
        // Library has only lands + a too-expensive spell. Cascade exiles
        // everything, finds no eligible card, bottoms them all in random
        // order. The Crashing Footfalls resolve effect still creates the
        // two Rhino tokens.
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Forest", _alice);
        var heavy = new Sorcery("Big Spell", "{5}");
        heavy.SetOwner(_alice);

        foreach (var c in new ICard[] { m1, m2, heavy })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        CascadeAction.CascadeResult? captured = null;
        var card = CrashingFootfallsFactory.Create(
            _alice, triggers: null, willCast: null,
            onCascadeResolved: r => captured = r);

        // Resolve cascade.
        var cascadeTrigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in cascadeTrigger.Effects) e.Execute();

        captured.Should().NotBeNull();
        captured!.Eligible.Should().BeNull();
        captured.Exiled.Should().HaveCount(3);
        captured.Bottomed.Should().HaveCount(3);
        _alice.Zones.Library.Count.Should().Be(3);
        _alice.Zones.Exile.Count.Should().Be(0);

        // Now drive the spell resolution — Crashing Footfalls still makes
        // its two Rhino tokens.
        var def = CrashingFootfallsFactory.BuildSpellDefinition(_alice);
        foreach (var e in def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: Majik.Core.Players.Agents.ManaPayment.Empty)))
        {
            e.Execute();
        }

        var rhinos = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Rhino))
            .ToList();

        rhinos.Should().HaveCount(2);
    }

    [Fact]
    public void CascadeTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        // Sanity — trigger condition is keyed on this card's own SpellCastEvent
        // (not on any cast of any spell, which would be a Snapcaster-shaped bug).
        var card = CrashingFootfallsFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        trigger.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);

        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        // The condition should match for `card` but not for an unrelated
        // SpellCastEvent.
        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }
}
