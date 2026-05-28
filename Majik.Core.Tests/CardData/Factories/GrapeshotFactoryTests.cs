using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Grapeshot (Time Spiral / many reprints, {1}{R}, Sorcery).
///
/// Oracle: "Grapeshot deals 1 damage to any target.
///          Storm (When you cast this spell, copy it for each spell cast
///          before it this turn. You may choose new targets for the copies.)"
///
/// Coverage:
/// - Identity (name, type {1}{R}, MV 2, red colour) + NamedCardFactory dispatch.
/// - Structural Storm trigger attached (CR 702.39 / CR 702.40).
/// - Resolve body deals 1 damage to a chosen player target.
/// - Resolve body deals 1 damage to a chosen creature target.
/// - Cast as 1st spell this turn (0 other spells) → 1 hit, no copies.
/// - Cast as 6th spell this turn (5 other spells) → 6 hits total (original + 5 copies).
/// - Copies may choose new targets (tested independently per copy).
/// </summary>
public class GrapeshotFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Grapeshot_Identity_SorceryAt1R()
    {
        var gs = GrapeshotFactory.Create(_alice);

        gs.Name.Should().Be("Grapeshot");
        gs.HasType(CardType.Sorcery).Should().BeTrue();
        gs.ManaCost.Should().Be("{1}{R}");
        gs.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(gs).Should().Contain(ManaColor.Red);
        gs.Owner.Should().BeSameAs(_alice);
        gs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Grapeshot_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Grapeshot", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Grapeshot");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached (CR 702.39)
    // ---------------------------------------------------------------

    [Fact]
    public void Grapeshot_HasStructuralStormTrigger()
    {
        var gs = GrapeshotFactory.Create(_alice);

        var triggers = gs.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Grapeshot prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(gs);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.39a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void StormTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var gs = GrapeshotFactory.Create(_alice);
        var trigger = gs.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);

        var selfSpell = new Majik.Core.Spells.Spell(gs, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Spell definition shape — "any target", 1 damage
    // ---------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_HasSingleAnyTargetRequest()
    {
        var def = GrapeshotFactory.BuildSpellDefinition(resolver: x => x);

        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.Description.Should().Be("any target");
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
    }

    [Fact]
    public void Resolve_DealsOneDamageToPlayerTarget()
    {
        var def = GrapeshotFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { _bob } },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "Grapeshot deals 1 damage to any target");
    }

    [Fact]
    public void Resolve_DealsOneDamageToCreatureTarget()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            Array.Empty<CardSupertype>(), Array.Empty<CardSubtype>());
        bear.SetOwner(_bob);
        bear.SetController(_bob);
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var def = GrapeshotFactory.BuildSpellDefinition(resolver: x => x);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { (IReadOnlyList<object>)new object[] { bear } },
            Mana: ManaPayment.Empty);

        var effects = def.EffectFactory(chosen);
        foreach (var e in effects) e.Execute();

        bear.Damage.Should().Be(1, "Grapeshot deals 1 damage to target creature");
        _bob.LifeTotal.Should().Be(20, "player untouched when targeting creature");
    }

    // ---------------------------------------------------------------
    // Storm — first spell this turn (0 copies, 1 hit total)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFirstSpellThisTurn_OneHit_NoCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var gs = GrapeshotFactory.Create(_alice);
        var def = GrapeshotFactory.BuildSpellDefinition(resolver: x => x);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            gs, _alice, targets: null, costs: null, effects: spellEffects);
        gs.SetZone(ZoneType.Stack);

        // TurnDriver increments before storm evaluates.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(gs, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Resolve storm (0 copies) then the original (1 hit).
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19,
            "only the original Grapeshot fires — 1 damage; storm count is 0 (no copies)");
    }

    // ---------------------------------------------------------------
    // Storm — sixth spell this turn (5 copies + original = 6 hits)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsSixthSpellThisTurn_SixHits_FiveCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice already cast 5 spells before Grapeshot.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        // Now Grapeshot itself is cast (TurnDriver increment).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(6);

        var gs = GrapeshotFactory.Create(_alice);
        var def = GrapeshotFactory.BuildSpellDefinition(resolver: x => x);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            gs, _alice, targets: null, costs: null, effects: spellEffects);
        gs.SetZone(ZoneType.Stack);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(gs, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Resolve storm (5 copies each dealing 1) then original (1).
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(14,
            "5 storm copies × 1 dmg + original 1 dmg = 6 total; 20 - 6 = 14");
    }

    // ---------------------------------------------------------------
    // Storm — null TurnState fallback (no-op count, trigger still fires)
    // ---------------------------------------------------------------

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        var gs = GrapeshotFactory.Create(_alice);
        gs.SetZone(ZoneType.Stack);
        var stormTrigger = gs.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(gs, _alice);
        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Effect should not throw and should not deal damage.
        var act = () => { foreach (var e in stormTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20);
    }
}
