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
/// Tests for Tendrils of Agony (Scourge, {2}{B}{B}, Sorcery).
///
/// Oracle: "Target opponent loses 2 life and you gain 2 life. Storm
/// (When you cast this spell, copy it for each spell cast before it this
/// turn. You may choose new targets for the copies.)"
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - Structural Storm trigger attached (CR 702.40).
/// - Life-swing resolution against the chosen opponent.
/// - Cast as 1st spell this turn (no other spells) → 2 life swing only.
/// - Cast as 5th spell this turn → 1 original + 4 copies = 10 life swing.
/// </summary>
public class TendrilsOfAgonyTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_BlackDoubleCost()
    {
        var t = TendrilsOfAgonyFactory.Create(_alice);

        t.Name.Should().Be("Tendrils of Agony");
        t.HasType(CardType.Sorcery).Should().BeTrue();
        t.ManaCost.Should().Be("{2}{B}{B}");
        t.ManaCostValue.TotalValue.Should().Be(4);
        CardColors.GetColors(t).Should().Contain(ManaColor.Black);
        t.Owner.Should().BeSameAs(_alice);
        t.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsTendrilsShape()
    {
        var dispatched = NamedCardFactory.Create("Tendrils of Agony", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Tendrils of Agony");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{2}{B}{B}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var t = TendrilsOfAgonyFactory.Create(_alice);

        var triggers = t.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Tendrils of Agony prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(t);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    // ---------------------------------------------------------------
    // Life-swing resolution
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_TargetOpponent_LosesTwo_ControllerGainsTwo()
    {
        var def = TendrilsOfAgonyFactory.BuildDefinition(
            controller: _alice,
            targetResolver: raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target opponent");
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.LifeTotal.Should().Be(18, "target opponent loses 2 life");
        _alice.LifeTotal.Should().Be(22, "controller gains 2 life");
    }

    // ---------------------------------------------------------------
    // Storm — first spell this turn (no copies)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFirstSpellThisTurn_TwoLifeSwing_NoCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var t = TendrilsOfAgonyFactory.Create(_alice);
        var def = TendrilsOfAgonyFactory.BuildDefinition(
            controller: _alice,
            targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            t, _alice, targets: null, costs: null, effects: spellEffects);
        t.SetZone(ZoneType.Stack);

        // Tendrils is the first (and only) spell cast this turn.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Black });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(t, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18, "no copies; just the original 2-life loss");
        _alice.LifeTotal.Should().Be(22, "no copies; just the original 2-life gain");
    }

    // ---------------------------------------------------------------
    // Storm — fifth spell this turn (4 copies + original = 10 life swing)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFifthSpellThisTurn_TenLifeSwing_FourCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice cast four spells before Tendrils this turn.
        for (int i = 0; i < 4; i++)
        {
            ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Black });
        }
        // Now she casts Tendrils — TurnDriver increments the tally
        // (typed SpellCastEvent subscriber fires before the global
        // TriggerManager handler).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Black });
        ts.SpellsCastByPlayer(_alice).Should().Be(5);

        var t = TendrilsOfAgonyFactory.Create(_alice);
        var def = TendrilsOfAgonyFactory.BuildDefinition(
            controller: _alice,
            targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            t, _alice, targets: null, costs: null, effects: spellEffects);
        t.SetZone(ZoneType.Stack);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(t, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // 4 copies via SpellCopier (re-executes the original effect list
        // in place) + 1 original = 5 resolutions × 2 life swing = 10.
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(10, "5 resolutions (1 original + 4 storm copies) × 2 life loss = 10");
        _alice.LifeTotal.Should().Be(30, "5 resolutions × 2 life gain = 10");
    }

    // ---------------------------------------------------------------
    // Storm — null TurnState fallback (no-op count, trigger still fires)
    // ---------------------------------------------------------------

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        var t = TendrilsOfAgonyFactory.Create(_alice);
        t.SetZone(ZoneType.Stack);
        var stormTrigger = t.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(t, _alice);
        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        var act = () => { foreach (var e in stormTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
