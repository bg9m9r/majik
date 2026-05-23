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
/// Tests for Brain Freeze (Scourge, {U}{U}, Instant).
///
/// Oracle: "Target player mills three cards. Storm (When you cast this
/// spell, copy it for each spell cast before it this turn. You may
/// choose new targets for the copies.)"
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - Structural Storm trigger attached (CR 702.40).
/// - Mill 3 resolves against the chosen target player.
/// - Cast as 1st spell this turn (no other spells) → 1x mill 3, no copies.
/// - Cast as 4th spell this turn → 1x mill 3 + 3 copies = 12 cards milled.
/// </summary>
public class BrainFreezeTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasInstantShape_BlueDoubleCost()
    {
        var bf = BrainFreezeFactory.Create(_alice);

        bf.Name.Should().Be("Brain Freeze");
        bf.HasType(CardType.Instant).Should().BeTrue();
        bf.ManaCost.Should().Be("{U}{U}");
        bf.ManaCostValue.TotalValue.Should().Be(2);
        CardColors.GetColors(bf).Should().Contain(ManaColor.Blue);
        bf.Owner.Should().BeSameAs(_alice);
        bf.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsBrainFreezeShape()
    {
        var dispatched = NamedCardFactory.Create("Brain Freeze", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Brain Freeze");
        dispatched.HasType(CardType.Instant).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{U}{U}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var bf = BrainFreezeFactory.Create(_alice);

        var triggers = bf.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Brain Freeze prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(bf);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void StormTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var bf = BrainFreezeFactory.Create(_alice);
        var trigger = bf.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Instant("Other Spell", "{U}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);

        var selfSpell = new Majik.Core.Spells.Spell(bf, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Mill resolution
    // ---------------------------------------------------------------

    [Fact]
    public void BuildDefinition_TargetPlayer_MillsThree()
    {
        // Seed Bob's library with 5 cards so we can verify exactly 3 mill.
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var def = BrainFreezeFactory.BuildDefinition(targetResolver: raw => raw);

        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].Description.Should().Be("target player");
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        // Drive resolution with Bob as the chosen target.
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _bob.Zones.Library.Count.Should().Be(2);
        _bob.Zones.Graveyard.Count.Should().Be(3);
    }

    // ---------------------------------------------------------------
    // Storm — first spell this turn (no copies)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFirstSpellThisTurn_Mills3_NoCopies()
    {
        // Seed Bob's library so we can count mills.
        for (int i = 0; i < 10; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var bf = BrainFreezeFactory.Create(_alice);
        // Construct the spell with the mill-3-target-Bob effect baked in
        // (same way SpellCastFlow would build it from BuildDefinition).
        var def = BrainFreezeFactory.BuildDefinition(targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            bf, _alice, targets: null, costs: null, effects: spellEffects);
        bf.SetZone(ZoneType.Stack);

        // Simulate the TurnState bookkeeping that TurnDriver does on cast
        // (CR 700.6 / 702.40a — the spell being cast is counted as it is
        // announced).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        // Build the storm trigger directly with our live ts + stack so the
        // helper sees them at evaluate time. (NamedCardFactory shape-only
        // path attaches a no-stack/no-turnstate trigger we can't read from.)
        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(bf, _alice, stack, ts);

        // Evaluate the trigger condition (same path TriggerManager uses) so
        // it captures the storm count.
        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Resolve the storm trigger's effects (copy creation), then the
        // spell's own effects (mill 3) — Brain Freeze itself is the only
        // spell cast this turn, so storm count is zero and no copies fire.
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _bob.Zones.Graveyard.Count.Should().Be(3, "no copies; just the original mill 3");
    }

    // ---------------------------------------------------------------
    // Storm — fourth spell this turn (3 copies + original = 12 mill)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFourthSpellThisTurn_Mills12_ThreeCopies()
    {
        // Seed Bob's library deeply.
        for (int i = 0; i < 20; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice already cast three spells before Brain Freeze this turn.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        // Now she casts Brain Freeze — TurnDriver increments the tally
        // (typed SpellCastEvent subscriber fires before the global
        // TriggerManager handler).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.SpellsCastByPlayer(_alice).Should().Be(4);

        var bf = BrainFreezeFactory.Create(_alice);
        var def = BrainFreezeFactory.BuildDefinition(targetResolver: raw => raw);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { _bob } },
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            bf, _alice, targets: null, costs: null, effects: spellEffects);
        bf.SetZone(ZoneType.Stack);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(bf, _alice, stack, ts);

        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Resolve order in v1: SpellCopier.PushCopyOfTopSpell re-executes
        // the original spell's effect list in place, then the original
        // spell resolves normally. Either order yields the same observable
        // mill total since each copy is independent and the target is
        // pinned to Bob.
        foreach (var e in stormTrigger.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        // 3 copies × mill 3 + original mill 3 = 12.
        _bob.Zones.Graveyard.Count.Should().Be(12,
            "3 storm copies × 3 + original mill 3 = 12");
    }

    // ---------------------------------------------------------------
    // Storm — null TurnState fallback (no-op count, trigger still fires)
    // ---------------------------------------------------------------

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        // The default Create(owner) overload wires null TurnState + null
        // stack. The trigger should still match the source spell, and the
        // effect should no-op without copies (no crash).
        for (int i = 0; i < 5; i++)
        {
            var c = new Instant($"Junk{i}", "{U}");
            c.SetOwner(_bob);
            _bob.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var bf = BrainFreezeFactory.Create(_alice);
        bf.SetZone(ZoneType.Stack);
        var stormTrigger = bf.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(bf, _alice);
        var evt = new SpellCastEvent(spell);
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Effect should not throw and should not mill.
        var act = () => { foreach (var e in stormTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.Zones.Graveyard.Count.Should().Be(0);
    }
}
