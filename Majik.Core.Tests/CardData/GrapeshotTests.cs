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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Grapeshot (Time Spiral, {1}{R}, Sorcery).
///
/// Oracle: "Grapeshot deals 1 damage to any target. Storm (When you cast
/// this spell, copy it for each spell cast before it this turn. You may
/// choose new targets for the copies.)"
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - Structural Storm trigger attached (CR 702.40).
/// - 1-damage resolution against the chosen player target.
/// - Cast as 1st spell this turn (no other spells) → 1 damage only.
/// - Cast as 6th spell this turn → 1 original + 5 copies = 6 damage total
///   (the classic "lethal Grapeshot" storm-pile finisher).
/// </summary>
public class GrapeshotTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_RedCost()
    {
        var g = GrapeshotFactory.Create(_alice);

        g.Name.Should().Be("Grapeshot");
        g.HasType(CardType.Sorcery).Should().BeTrue();
        g.ManaCost.Should().Be("{1}{R}");
        g.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(g).Should().Contain(ManaColor.Red);
        g.Owner.Should().BeSameAs(_alice);
        g.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsGrapeshotShape()
    {
        var dispatched = NamedCardFactory.Create("Grapeshot", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Grapeshot");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{1}{R}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var g = GrapeshotFactory.Create(_alice);

        var triggers = g.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Grapeshot prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(g);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    // ---------------------------------------------------------------
    // 1-damage resolution
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_AnyTarget_DealsOneDamageToPlayer()
    {
        var def = GrapeshotFactory.BuildDefinition(targetResolver: raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("any target");
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "1 damage to any target");
        _alice.LifeTotal.Should().Be(20);
    }

    // ---------------------------------------------------------------
    // Storm — first spell this turn (no copies)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFirstSpellThisTurn_OneDamage_NoCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var g = GrapeshotFactory.Create(_alice);
        var def = GrapeshotFactory.BuildDefinition(targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            g, _alice, targets: null, costs: null, effects: spellEffects);
        g.SetZone(ZoneType.Stack);

        // Grapeshot is the first (and only) spell cast this turn.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(g, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(19, "no copies; just the original 1-damage ping");
    }

    // ---------------------------------------------------------------
    // Storm — sixth spell this turn (5 copies + original = 6 damage)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsSixthSpellThisTurn_SixDamage_FiveCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice cast five spells before Grapeshot this turn.
        for (int i = 0; i < 5; i++)
        {
            ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        }
        // Now she casts Grapeshot — TurnDriver increments the tally
        // (typed SpellCastEvent subscriber fires before the global
        // TriggerManager handler).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(6);

        var g = GrapeshotFactory.Create(_alice);
        var def = GrapeshotFactory.BuildDefinition(targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            g, _alice, targets: null, costs: null, effects: spellEffects);
        g.SetZone(ZoneType.Stack);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(g, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // 5 copies via SpellCopier (re-executes the original effect list in
        // place) + 1 original = 6 resolutions × 1 damage = 6.
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(14, "6 resolutions (1 original + 5 storm copies) × 1 damage = 6");
    }

    // ---------------------------------------------------------------
    // Storm — null TurnState fallback (no-op count, trigger still fires)
    // ---------------------------------------------------------------

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        var g = GrapeshotFactory.Create(_alice);
        g.SetZone(ZoneType.Stack);
        var stormTrigger = g.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(g, _alice);
        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        var act = () => { foreach (var e in stormTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
