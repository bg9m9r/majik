using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Logic Knot (Future Sight, {X}{U}{U}, Instant).
/// Oracle: "Delve. Counter target spell unless its controller pays {X}."
///
/// Coverage:
///   - Card identity + Delve marker + dispatch by name.
///   - SpellDefinition exposes HasVariableX + a single "target spell" request.
///   - Resolve counters target when the controller cannot pay {X} (X &gt; 0).
///   - Resolve does NOT counter when the controller pays {X}.
///   - X = 0 → controller trivially pays {0}; spell is not countered.
/// </summary>
public class LogicKnotTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly ZoneService _zones;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public LogicKnotTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    [Fact]
    public void LogicKnot_Identity_AndDelveKeyword()
    {
        var lk = LogicKnotFactory.Create(_alice);

        lk.Name.Should().Be("Logic Knot");
        lk.ManaCost.Should().Be("{X}{U}{U}");
        lk.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(lk).Should().Contain(ManaColor.Blue);
        lk.Owner.Should().BeSameAs(_alice);
        lk.Controller.Should().BeSameAs(_alice);

        lk.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LogicKnot()
    {
        var card = NamedCardFactory.Create("Logic Knot", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Logic Knot");
        card.ManaCost.Should().Be("{X}{U}{U}");
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Delve");
    }

    [Fact]
    public void BuildDefinition_HasVariableX_AndSingleTargetSpellRequest()
    {
        var def = LogicKnotFactory.BuildDefinition(o => o, null);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Contain("target spell");
    }

    [Fact]
    public void EffectFactory_CountersTargetSpell_WhenControllerCannotPayX()
    {
        // X = 2. Bob has no mana → cannot pay {2} → Knot counters.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = LogicKnotFactory.BuildDefinition(o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobBolt.Zone.Should().Be(ZoneType.Graveyard,
            because: "Bob couldn't pay {2}, so Logic Knot counters his spell");
        _stack.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void EffectFactory_DoesNotCounter_WhenControllerPaysX()
    {
        // X = 2. Bob has {2} → auto-pays → Knot does NOT counter.
        _bob.AddManaToPool(ManaCost.Zero.AddGenericCost(2));

        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = LogicKnotFactory.BuildDefinition(o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "Bob paid {2} so Logic Knot is countered into a no-op");
        // Spell still on the stack — Knot didn't remove it.
        _stack.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void EffectFactory_XZero_DoesNotCounter()
    {
        // X = 0 → controller pays {0} trivially; spell is NOT countered.
        // This matches the printed text (counter unless pay {X}, X = 0).
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var def = LogicKnotFactory.BuildDefinition(o => o, _stack);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 0,
            Targets: new IReadOnlyList<object>[] { new object[] { bobSpell } },
            Mana: ManaPayment.Empty,
            AllPlayers: new[] { _alice, _bob });

        foreach (var eff in def.EffectFactory(chosen)) eff.Execute();

        bobBolt.Zone.Should().NotBe(ZoneType.Graveyard,
            because: "X = 0 means the pay-rider is satisfied trivially (free pay {0})");
    }

    [Fact]
    public async Task LogicKnot_CastViaSpellCastFlow_CountersTargetSpell_WhenControllerCannotPayX()
    {
        // Integration: Alice casts Logic Knot with X=3, paying {3}{U}{U}
        // from her pool. Bob can't pay {3} → Knot counters.
        _alice.AddManaToPool(ManaCost.Parse("{U}{U}").AddGenericCost(3));

        // Bob casts a spell Alice wants to counter.
        var bobBolt = new Instant("Lightning Bolt", "{R}") { Owner = _bob, Controller = _bob };
        var bobSpell = new Majik.Core.Spells.Spell(bobBolt, _bob);
        _stack.Push(bobSpell);

        var lk = LogicKnotFactory.Create(_alice);
        lk.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(lk);

        var agent = new ScriptedAgent();
        agent.QueueX(3);
        agent.QueueTargets(new object[] { bobSpell });
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_alice, new[] { _alice, _bob },
            _alice, 1, PhaseStateType.Main, _stack);

        var spell = await _flow.CastAsync(
            _alice, lk,
            LogicKnotFactory.BuildDefinition(o => o, _stack),
            agent, ctx,
            alternativeCost: null);

        // PendingCastX was stamped onto the card during the cast flow.
        // (SpellCastFlow clears it after, but the value flows through
        // ChosenSpellParams.X into the resolve effect.)

        // Knot sits above Bob's spell.
        lk.Zone.Should().Be(ZoneType.Stack);

        spell.Resolve();

        // Bob couldn't pay {3} (empty pool) → counter fires.
        bobBolt.Zone.Should().Be(ZoneType.Graveyard);
    }
}
