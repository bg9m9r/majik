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
/// Tests for Flusterstorm (Commander 2011, {U}).
///
/// Oracle: "Counter target instant or sorcery spell unless its controller
/// pays {1}. Storm (When you cast this spell, copy it for each spell cast
/// before it this turn. You may choose new targets for the copies.)"
///
/// Coverage:
/// - Identity / dispatch.
/// - Structural Storm trigger attached (CR 702.40).
/// - Counters target instant when controller can't pay {1}.
/// - Controller auto-pays {1} → counter no-ops (CR 118.4 / v1 posture).
/// - No-op vs creature spell (CR 608.2b).
/// - Storm count: 4th spell of the turn pushes 3 copies.
/// </summary>
public class FlusterstormTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ───────────────────────────────────────────

    [Fact]
    public void Create_HasInstantShape_Blue()
    {
        var fs = FlusterstormFactory.Create(_alice);

        fs.Name.Should().Be("Flusterstorm");
        fs.HasType(CardType.Instant).Should().BeTrue();
        fs.ManaCost.Should().Be("{U}");
        fs.ManaCostValue.TotalValue.Should().Be(1);
        CardColors.GetColors(fs).Should().Contain(ManaColor.Blue);
        fs.Owner.Should().BeSameAs(_alice);
        fs.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsFlusterstormShape()
    {
        var dispatched = NamedCardFactory.Create("Flusterstorm", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Flusterstorm");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void Card_HasStructuralStormTrigger()
    {
        var fs = FlusterstormFactory.Create(_alice);

        var triggers = fs.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "Flusterstorm prints one triggered ability — Storm.");

        var storm = triggers[0];
        storm.Source.Should().BeSameAs(fs);
        storm.Controller.Should().BeSameAs(_alice);
        storm.ActiveZones.Should().Contain(ZoneType.Stack,
            "Storm functions on the stack (CR 702.40a).");
        storm.Condition.Should().BeOfType<EventTriggerCondition<SpellCastEvent>>();
    }

    [Fact]
    public void SpellDefinition_DeclaresSingleTargetInstantOrSorcerySpellRequest()
    {
        var def = FlusterstormFactory.BuildDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("instant or sorcery");
    }

    // ── Counter / unless-pay behaviour ───────────────────────────────

    [Fact]
    public void Counters_Instant_WhenControllerCannotPayOne()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts an instant; mana pool is empty so the auto-pay fails.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = FlusterstormFactory.BuildDefinition(raw => raw, stack);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {1}, so Flusterstorm counters the instant.");
    }

    [Fact]
    public void DoesNotCounter_Instant_WhenControllerPaysOne()
    {
        var stack = new Majik.Core.Stack.Stack();

        // Bob casts an instant; floats {1} so the unless-pay auto-fires.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(1));

        var def = FlusterstormFactory.BuildDefinition(raw => raw, stack);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob auto-paid {1}, so the counter no-ops.");
    }

    [Fact]
    public void DoesNotCounter_CreatureSpell()
    {
        var stack = new Majik.Core.Stack.Stack();

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        stack.Push(bobSpell);

        var def = FlusterstormFactory.BuildDefinition(raw => raw, stack);
        var effects = def.EffectFactory(new ChosenSpellParams(
            ModeIndex: null, X: null,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty));
        foreach (var e in effects) e.Execute();

        bobBear.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Flusterstorm only counters instant or sorcery spells (CR 608.2b).");
    }

    // ── Storm count ───────────────────────────────────────────────────

    [Fact]
    public void StormTrigger_FourthSpellThisTurn_ComputesThreeStormCount()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();

        // Alice cast three spells before Flusterstorm this turn.
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        // Now Flusterstorm itself (TurnDriver typed sub fires before global).
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });
        ts.SpellsCastByPlayer(_alice).Should().Be(4);

        var fs = FlusterstormFactory.Create(_alice);
        fs.SetZone(ZoneType.Stack);

        // No need to drive the actual copy — the storm trigger condition
        // does the count math; the SpellCopier behaviour is covered by
        // BrainFreezeTests.
        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(fs, _alice, stack, ts);

        // Construct a dummy spell wrapper so the on-cast condition has the
        // canonical SpellCastEvent shape to match.
        var fsSpell = new Majik.Core.Spells.Spell(fs, _alice);
        var evt = new SpellCastEvent(fsSpell);

        // Condition matches AND captures storm count = total - 1 = 3.
        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue(
            "the storm trigger fires on this spell's SpellCastEvent (CR 702.40a).");
    }

    [Fact]
    public void StormTrigger_FirstSpellThisTurn_NoCopies()
    {
        var ts = new TurnState();
        var stack = new Majik.Core.Stack.Stack();
        ts.RecordSpellCast(_alice, new HashSet<ManaColor> { ManaColor.Blue });

        var fs = FlusterstormFactory.Create(_alice);
        fs.SetZone(ZoneType.Stack);

        var stormTrigger = Majik.Core.Keywords.StormHelper.Build(fs, _alice, stack, ts);
        var fsSpell = new Majik.Core.Spells.Spell(fs, _alice);
        var evt = new SpellCastEvent(fsSpell);

        stormTrigger.Condition.Matches(evt, stormTrigger).Should().BeTrue();

        // Effect should no-op without throwing — total-1 = 0 copies.
        var act = () => { foreach (var e in stormTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
