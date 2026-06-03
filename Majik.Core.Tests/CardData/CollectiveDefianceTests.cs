using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// CR 700.2d ("choose one or more") + CR 702.121 (Escalate {1}).
/// Collective Defiance (Eldritch Moon, {1}{R}{R}, Sorcery). Three modes:
///   0 — target player discards their hand, then draws that many.
///   1 — deals 4 damage to target creature.
///   2 — deals 3 damage to target opponent or planeswalker.
///
/// Cast end-to-end through <see cref="SpellCastFlow"/> so the multi-mode
/// prompt + escalate (mana) additional-cost machinery is exercised, plus the
/// chosen-mode MinTargets tightening (CR 601.2c).
/// </summary>
public class CollectiveDefianceTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public CollectiveDefianceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _flow = new SpellCastFlow(_stack, new ZoneService(_bus), _bus);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, _stack);

    private SpellDefinition Def() =>
        CollectiveDefianceFactory.BuildDefinition(_alice, o => o);

    private Sorcery NewDefiance()
    {
        var cd = CollectiveDefianceFactory.Create(_alice);
        cd.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(cd);
        return cd;
    }

    [Fact]
    public void Create_HasSorceryShape_Red()
    {
        var cd = CollectiveDefianceFactory.Create(_alice);

        cd.Name.Should().Be("Collective Defiance");
        cd.HasType(CardType.Sorcery).Should().BeTrue();
        CardColors.GetColors(cd).Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void NamedCardFactory_DispatchByName_ReturnsDefianceShape()
    {
        var dispatched = NamedCardFactory.Create("Collective Defiance", _alice);

        dispatched.Should().BeOfType<Sorcery>();
        dispatched.Name.Should().Be("Collective Defiance");
    }

    [Fact]
    public void BuildDefinition_IsMultiMode_ChooseOneToThree_WithEscalate()
    {
        var def = Def();

        def.Modes.Should().HaveCount(3);
        def.MinModes.Should().Be(1);
        def.MaxModes.Should().Be(3);
        def.IsMultiMode.Should().BeTrue();
        def.Escalate.Should().NotBeNull();
        def.Escalate!.Description.Should().Be("{1}");
    }

    [Fact]
    public async Task ChooseOneMode_DamageThree_ToOpponent_NoEscalate()
    {
        // One mode (3 damage to Bob) → zero escalate mana. Free cast.
        var cd = NewDefiance();

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveDefianceFactory.ModeDamageThree);
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cd, Def(), agent, Ctx());
        spell.Resolve();

        _bob.LifeTotal.Should().Be(17, because: "mode 2 deals 3 damage to the target opponent");
    }

    [Fact]
    public async Task ChooseTwoModes_PayOneEscalateMana_BothResolveInPrintedOrder()
    {
        // Modes 1 (4 dmg to creature) + 2 (3 dmg to opponent) → one escalate {1}.
        var cd = NewDefiance();
        _alice.AddManaToPool(ManaCost.Parse("{R}")); // escalate {1} for the extra mode

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Controller = _bob };
        bear.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bear);

        var agent = new ScriptedAgent();
        // Announce in reverse; resolution must run mode 1 before mode 2 (printed
        // order, CR 608.2c).
        agent.QueueModes(CollectiveDefianceFactory.ModeDamageThree,
                         CollectiveDefianceFactory.ModeDamageFour);
        // Mode-aware target collection prompts only chosen modes, in request
        // declaration order: mode 1 (creature), then mode 2 (opponent).
        agent.QueueTargets(new object[] { bear }); // mode 1 — creature
        agent.QueueTargets(new object[] { _bob });  // mode 2 — opponent
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cd, Def(), agent, Ctx());

        // Escalate {1} consumed the pooled mana before resolution.
        _alice.ManaPool.Total.Should().Be(0, because: "two modes cost one escalate {1}");

        spell.Resolve();

        bear.Damage.Should().Be(4, because: "mode 1 deals 4 damage to the creature");
        _bob.LifeTotal.Should().Be(17, because: "mode 2 deals 3 damage to the opponent");
    }

    [Fact]
    public async Task CannotPayEscalate_NoMana_CastIllegal()
    {
        // Two modes need one escalate {1}, but Alice's pool is empty.
        // CR 601.2g — the cast is illegal and throws.
        var cd = NewDefiance();

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveDefianceFactory.ModeDamageThree,
                         CollectiveDefianceFactory.ModeDamageFour);

        Func<Task> act = async () => await _flow.CastAsync(_alice, cd, Def(), agent, Ctx());

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*Escalate*");
    }

    [Fact]
    public async Task ChosenTargetedMode_NoLegalTarget_CastIllegal_Rewinds()
    {
        // CR 601.2c — a chosen targeted mode with no legal target makes the
        // WHOLE cast illegal. Alice chooses mode 1 (4 damage to target
        // creature) but no creature exists; the agent returns an empty slot.
        var cd = NewDefiance();

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveDefianceFactory.ModeDamageFour);
        agent.QueueTargets(System.Array.Empty<object>()); // no legal creature
        agent.QueueMana(ManaPayment.Empty);

        Func<Task> act = async () => await _flow.CastAsync(_alice, cd, Def(), agent, Ctx());

        await act.Should().ThrowAsync<System.InvalidOperationException>()
            .WithMessage("*target creature*");
    }

    [Fact]
    public async Task ModeWheel_TargetPlayerDiscardsHand_DrawsThatMany()
    {
        // Mode 0 — Bob discards his whole hand (2 cards), then draws 2.
        var cd = NewDefiance();

        var bobCard1 = new Instant("Bob Spell 1", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        var bobCard2 = new Instant("Bob Spell 2", "{R}") { Owner = _bob, Zone = ZoneType.Hand };
        _bob.Zones.Hand.AddCard(bobCard1);
        _bob.Zones.Hand.AddCard(bobCard2);

        // Bob has 3 cards in library to draw from.
        for (var i = 0; i < 3; i++)
        {
            var lib = new Land("Mountain") { Owner = _bob, Zone = ZoneType.Library };
            _bob.Zones.Library.AddCard(lib);
        }

        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveDefianceFactory.ModeWheel);
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, cd, Def(), agent, Ctx());
        spell.Resolve();

        bobCard1.Zone.Should().Be(ZoneType.Graveyard);
        bobCard2.Zone.Should().Be(ZoneType.Graveyard);
        _bob.Zones.Hand.GetCards().Should().HaveCount(2,
            because: "discarded 2, then drew that many (2)");
    }
}
