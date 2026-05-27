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
/// Tests for Empty the Warrens (Time Spiral, {3}{R}, Sorcery).
///
/// Oracle: "Create two 1/1 red Goblin creature tokens. Storm (When you
/// cast this spell, copy it for each spell cast before it this turn.)"
///
/// Coverage:
/// - Identity (name, type, cost, colour) + NamedCardFactory dispatch.
/// - Structural Storm trigger attached (CR 702.40).
/// - Two 1/1 red Goblin tokens enter on resolution.
/// - Cast as 1st spell this turn → 2 tokens total, no storm copies.
/// - Cast as 4th spell this turn → 2 + 3 copies × 2 = 8 tokens total.
/// </summary>
public class EmptyTheWarrensTests
{
    private readonly Player _alice = new("Alice", 20);

    // ---------------------------------------------------------------
    // Identity / dispatch
    // ---------------------------------------------------------------

    [Fact]
    public void Create_HasSorceryShape_RedFourMana()
    {
        var card = EmptyTheWarrensFactory.Create(_alice);

        card.Name.Should().Be("Empty the Warrens");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{3}{R}");
        card.ManaCostValue.TotalValue.Should().Be(4);
        CardColors.GetColors(card).Should().Contain(ManaColor.Red);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsEmptyTheWarrensShape()
    {
        var dispatched = NamedCardFactory.Create("Empty the Warrens", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Empty the Warrens");
        dispatched.HasType(CardType.Sorcery).Should().BeTrue();
        dispatched.ManaCost.Should().Be("{3}{R}");
    }

    // ---------------------------------------------------------------
    // Structural shape — Storm trigger attached
    // ---------------------------------------------------------------

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var card = EmptyTheWarrensFactory.Create(_alice);

        var triggers = card.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Empty the Warrens prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(card);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void StormTrigger_Condition_MatchesOnlyThisCardsSpellCastEvent()
    {
        var card = EmptyTheWarrensFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var other = new Sorcery("Other Spell", "{R}");
        other.SetOwner(_alice);
        var otherSpell = new Majik.Core.Spells.Spell(other, _alice);
        var selfSpell = new Majik.Core.Spells.Spell(card, _alice);

        var cond = (EventTriggerCondition<SpellCastEvent>)trigger.Condition;
        cond.Matches(new SpellCastEvent(selfSpell), trigger).Should().BeTrue();
        cond.Matches(new SpellCastEvent(otherSpell), trigger).Should().BeFalse();
    }

    // ---------------------------------------------------------------
    // Resolution — two 1/1 red Goblin tokens
    // ---------------------------------------------------------------

    [Fact]
    public void BuildResolveEffect_CreatesTwoOneOneRedGoblinTokens()
    {
        var effects = EmptyTheWarrensFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        var bf = _alice.Zones.Battlefield.GetCards().OfType<Creature>().ToList();
        bf.Should().HaveCount(2, "Empty the Warrens creates exactly 2 tokens.");

        foreach (var tok in bf)
        {
            tok.Name.Should().Be("Goblin");
            tok.BasePower.Should().Be(1);
            tok.BaseToughness.Should().Be(1);
            tok.IsToken.Should().BeTrue();
            tok.Subtypes.Should().Contain(CardSubtype.Goblin);
            CardColors.GetColors(tok).Should().Contain(ManaColor.Red);
            CardColors.GetColors(tok).Should().HaveCount(1,
                "1/1 RED Goblin tokens — single colour stamp.");
            tok.Controller.Should().BeSameAs(_alice);
            tok.Owner.Should().BeSameAs(_alice);
        }
    }

    [Fact]
    public void BuildDefinition_HasNoTargets_ResolvesToTwoTokens()
    {
        var def = EmptyTheWarrensFactory.BuildDefinition(_alice);

        def.TargetRequests.Should().BeEmpty("Empty the Warrens has no printed targets.");
        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();

        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));

        foreach (var e in effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().HaveCount(2);
    }

    // ---------------------------------------------------------------
    // Storm — first spell this turn (no copies)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFirstSpellThisTurn_TwoTokens_NoCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        var card = EmptyTheWarrensFactory.Create(_alice);
        var def = EmptyTheWarrensFactory.BuildDefinition(_alice);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            card, _alice, targets: null, costs: null, effects: spellEffects);
        card.SetZone(ZoneType.Stack);

        // CR 700.6 / 702.40a — the spell being cast is counted on
        // announcement (TurnDriver bookkeeping).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(1);

        var storm = Majik.Core.Keywords.StormHelper.Build(card, _alice, stack, ts);
        var evt = new SpellCastEvent(spell);
        storm.Condition.Matches(evt, storm).Should().BeTrue();

        foreach (var e in storm.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().HaveCount(2, "first spell — no storm copies, just the printed 2 tokens.");
    }

    // ---------------------------------------------------------------
    // Storm — fourth spell this turn (3 copies × 2 + original 2 = 8)
    // ---------------------------------------------------------------

    [Fact]
    public void Cast_AsFourthSpellThisTurn_EightTokens_ThreeCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice already cast three other spells before Empty the Warrens.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Red });
        ts.SpellsCastByPlayer(_alice).Should().Be(4);

        var card = EmptyTheWarrensFactory.Create(_alice);
        var def = EmptyTheWarrensFactory.BuildDefinition(_alice);
        var spellEffects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: Array.Empty<IReadOnlyList<object>>(),
            Mana: ManaPayment.Empty));
        var spell = new Majik.Core.Spells.Spell(
            card, _alice, targets: null, costs: null, effects: spellEffects);
        card.SetZone(ZoneType.Stack);

        var storm = Majik.Core.Keywords.StormHelper.Build(card, _alice, stack, ts);
        var evt = new SpellCastEvent(spell);
        storm.Condition.Matches(evt, storm).Should().BeTrue();

        // SpellCopier.PushCopyOfTopSpell re-executes the original effect
        // list per copy; observable contract is 3 copies × 2 + original
        // 2 = 8 tokens.
        foreach (var e in storm.Effects) e.Execute();
        foreach (var e in spell.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards().OfType<Creature>()
            .Should().HaveCount(8, "3 storm copies × 2 + original 2 = 8 tokens.");
    }

    // ---------------------------------------------------------------
    // Storm — null TurnState fallback (no-op count, no crash)
    // ---------------------------------------------------------------

    [Fact]
    public void StormTrigger_NullTurnState_FiresWithoutCopies()
    {
        var card = EmptyTheWarrensFactory.Create(_alice);
        card.SetZone(ZoneType.Stack);
        var storm = card.Abilities.OfType<TriggeredAbility>().Single();

        var spell = new Majik.Core.Spells.Spell(card, _alice);
        var evt = new SpellCastEvent(spell);
        storm.Condition.Matches(evt, storm).Should().BeTrue();

        var act = () => { foreach (var e in storm.Effects) e.Execute(); };
        act.Should().NotThrow();

        // Storm itself created no tokens (the resolve effect is on the
        // spell, not the storm trigger). Battlefield should be empty.
        _alice.Zones.Battlefield.GetCards().OfType<Creature>().Should().BeEmpty();
    }
}
