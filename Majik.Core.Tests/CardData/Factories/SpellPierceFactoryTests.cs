using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpellPierceFactory"/> (Zendikar).
///
/// Oracle: "Counter target noncreature spell unless its controller pays {2}."
///
/// Coverage (factory surface — the broader oracle-binder path is exercised by
/// <see cref="SpellPierceTests"/>):
///   * Identity ({U} Instant, owner/controller, blue).
///   * <see cref="NamedCardFactory"/> dispatch.
///   * SpellDefinition shape (1..1 target noncreature spell).
///   * Controller cannot pay {2} → spell countered to graveyard.
///   * Controller pays {2} → spell NOT countered.
///   * Creature spell target → no-op at resolution (CR 608.2b).
/// </summary>
public class SpellPierceFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SpellPierce_Identity_InstantAtU()
    {
        var pierce = SpellPierceFactory.Create(_alice);

        pierce.Name.Should().Be("Spell Pierce");
        pierce.HasType(CardType.Instant).Should().BeTrue();
        pierce.ManaCost.ToString().Should().Be("{U}");
        CardColors.GetColors(pierce).Should().Contain(ManaColor.Blue);
        pierce.Owner.Should().BeSameAs(_alice);
        pierce.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpellPierce_DispatchesViaNamedCardFactory()
    {
        var dispatched = NamedCardFactory.Create("Spell Pierce", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("Spell Pierce");
        dispatched.ManaCost.Should().Be("{U}");
    }

    [Fact]
    public void SpellPierce_SpellDefinition_DeclaresSingleTargetNoncreatureSpell()
    {
        var def = SpellPierceFactory.BuildSpellDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("noncreature");
    }

    [Fact]
    public void SpellPierce_Resolve_ControllerCannotPayTwo_SpellCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        var def = SpellPierceFactory.BuildSpellDefinition(o => o, stack);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        stack.Count.Should().Be(0, "Spell Pierce countered the spell (Bob couldn't pay {2})");
        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            "countered spell's card goes to the graveyard (CR 701.5)");
    }

    [Fact]
    public void SpellPierce_Resolve_ControllerPaysTwo_SpellNotCountered()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        stack.Push(bobSpell);

        // Pre-stage Bob's mana pool with exactly {2} so the unless-pay
        // rider succeeds and the counter no-ops.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var def = SpellPierceFactory.BuildSpellDefinition(o => o, stack);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        stack.Count.Should().Be(1,
            "Bob paid {2}; Spell Pierce's counter no-ops and the spell stays on the stack");
        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            "the spell was not countered since its controller paid {2}");
    }

    [Fact]
    public void SpellPierce_Resolve_CreatureSpellTarget_NoOp()
    {
        var bus = new EventBus();
        var stack = new Majik.Core.Stack.Stack(bus);

        var bobBear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBear, _bob);
        stack.Push(bobSpell);

        var def = SpellPierceFactory.BuildSpellDefinition(o => o, stack);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty);

        foreach (var effect in def.EffectFactory(chosen)) effect.Execute();

        // CR 608.2b — creature spell at resolution → illegal target; effect
        // does nothing for it.
        stack.Count.Should().Be(1,
            "Spell Pierce does not counter creature spells");
        bobBear.Zone.Should().NotBe(ZoneType.Graveyard);
    }
}
