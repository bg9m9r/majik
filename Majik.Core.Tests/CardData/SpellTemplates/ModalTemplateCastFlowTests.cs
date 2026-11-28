using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.SpellTemplates;

/// <summary>
/// CR 700.2 / 601.2c — modal spells bound by <c>ModalChooseOneTemplate</c>
/// (the live path: the cast-time resolver binds by oracle text), cast through
/// the real <see cref="SpellCastFlow"/> and resolved by the real
/// <see cref="Majik.Core.Services.StackResolver"/>. Targets are chosen only for
/// the chosen modes, and each mode's body reads the targets chosen for it.
/// </summary>
public class ModalTemplateCastFlowTests
{
    private static readonly EmbeddedCardRepository Repo = new();
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly SpellCastFlow _flow;
    private readonly Majik.Core.Services.StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ModalTemplateCastFlowTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        var zones = new Majik.Core.Services.ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, zones, _bus);
        _resolver = new Majik.Core.Services.StackResolver(_bus, zones);
    }

    private GameContext Ctx => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private Creature OnBattlefield(string name, Player owner, int power, int toughness, string cost = "{1}")
    {
        var c = new Creature(name, cost, power, toughness);
        c.SetOwner(owner); c.SetController(owner);
        owner.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private T InHand<T>(T card) where T : Card
    {
        card.SetOwner(_alice); card.SetController(_alice);
        _alice.Zones.Hand.AddCard(card); card.SetZone(ZoneType.Hand);
        return card;
    }

    private SpellDefinition Bind(CardEntity entity) =>
        OracleSpellBinder.Bind(entity, _alice, raw => raw, _stack)
        ?? throw new InvalidOperationException($"{entity.Name} did not bind");

    private async Task CastAndResolve(Card card, SpellDefinition def, ScriptedAgent agent)
    {
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, def, agent, Ctx);
        await _resolver.ResolveTopAsync(_stack, game: Ctx);
    }

    [Fact]
    public async Task DrownInTheLoch_DestroyMode_DestroysTheChosenCreature()
    {
        var def = Bind(Repo.GetByName("Drown in the Loch")!);
        var destroy = def.Modes.ToList().FindIndex(m => m.StartsWith("Destroy", StringComparison.OrdinalIgnoreCase));
        destroy.Should().Be(1);
        for (var i = 0; i < 3; i++)
        {
            var g = new Land("Forest"); g.SetOwner(_bob); _bob.Zones.Graveyard.AddCard(g); g.SetZone(ZoneType.Graveyard);
        }
        var bear = OnBattlefield("Grizzly Bears", _bob, 2, 2, "{1}{G}");
        var card = InHand(new Instant("Drown in the Loch", "{U}{B}"));

        var agent = new ScriptedAgent();
        agent.QueueMode(destroy);
        agent.QueueTargets(new object[] { bear });
        await CastAndResolve(card, def, agent);

        bear.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task FirstModeUntargeted_ChosenTargetedMode_HitsItsTarget()
    {
        var def = Bind(new CardEntity
        {
            Name = "Test Modal",
            ManaCost = "{1}{B}",
            TypeLine = "Instant",
            OracleText = "Choose one — • You gain 3 life. • Destroy target creature.",
        });
        def.Modes.Should().HaveCount(2);
        var bear = OnBattlefield("Grizzly Bears", _bob, 2, 2);
        var card = InHand(new Instant("Test Modal", "{1}{B}"));

        var agent = new ScriptedAgent();
        agent.QueueMode(1);
        agent.QueueTargets(new object[] { bear });
        await CastAndResolve(card, def, agent);

        bear.Zone.Should().Be(ZoneType.Graveyard);
        _alice.LifeTotal.Should().Be(20, "the unchosen life-gain mode does not run");
    }

    [Fact]
    public async Task Bushwhack_FightMode_TheTwoChosenCreaturesFight()
    {
        var def = Bind(Repo.GetByName("Bushwhack")!);
        var fight = def.Modes.ToList().FindIndex(m => m.Contains("fights", StringComparison.OrdinalIgnoreCase));
        fight.Should().Be(1);
        var mine = OnBattlefield("Centaur", _alice, 3, 3);
        var theirs = OnBattlefield("Grizzly Bears", _bob, 2, 2);
        var card = InHand(new Sorcery("Bushwhack", "{G}"));

        var agent = new ScriptedAgent();
        agent.QueueMode(fight);
        agent.QueueTargets(new object[] { mine });
        agent.QueueTargets(new object[] { theirs });
        await CastAndResolve(card, def, agent);

        theirs.Damage.Should().Be(3, "each fighter deals damage equal to its power to the other (CR 701.14a)");
        mine.Damage.Should().Be(2);
    }

    [Fact]
    public async Task DromokasCommand_LiveCast_FightModeAfterThreeSingleTargetModes_ReadsItsOwnTargets()
    {
        // Mode 3 (your creature fights theirs) owns the slots after modes 0-2.
        // Only its two targets are prompted. The template now sets the "choose
        // two" bounds (CR 700.2); this test casts a one-mode copy so it keeps
        // checking only the fight mode's slots.
        var bound = Bind(Repo.GetByName("Dromoka's Command")!);
        bound.Modes.Should().HaveCount(4);
        bound.IsMultiMode.Should().BeTrue("choose two (CR 700.2)");
        (bound.MinModes, bound.MaxModes).Should().Be((2, 2));
        var def = bound with { MinModes = 1, MaxModes = 1 };
        var mine = OnBattlefield("Centaur", _alice, 3, 3);
        var theirs = OnBattlefield("Grizzly Bears", _bob, 2, 2);
        var card = InHand(new Instant("Dromoka's Command", "{G}{W}"));

        var agent = new ScriptedAgent();
        agent.QueueMode(3);
        agent.QueueTargets(new object[] { mine });
        agent.QueueTargets(new object[] { theirs });
        await CastAndResolve(card, def, agent);

        theirs.Damage.Should().Be(3);
        mine.Damage.Should().Be(2);
    }

    [Fact]
    public async Task DromokasCommand_ChooseTwo_EachChosenModeReadsItsOwnTargets_UnchosenModesNotPrompted()
    {
        // CR 700.2d / 601.2c — with two modes chosen, targets are chosen only
        // for the chosen modes, in mode order: mode 2 (+1/+1 counter on target
        // creature) and mode 3 (fight). The template sets the bounds itself.
        var def = Bind(Repo.GetByName("Dromoka's Command")!);
        (def.MinModes, def.MaxModes).Should().Be((2, 2));
        var mine = OnBattlefield("Centaur", _alice, 2, 2);
        var theirs = OnBattlefield("Grizzly Bears", _bob, 2, 2);
        var card = InHand(new Instant("Dromoka's Command", "{G}{W}"));

        var agent = new ScriptedAgent();
        agent.QueueModes(2, 3);
        agent.QueueTargets(new object[] { mine });    // mode 2: the counter
        agent.QueueTargets(new object[] { mine });    // mode 3: your fighter
        agent.QueueTargets(new object[] { theirs });  // mode 3: their creature
        await CastAndResolve(card, def, agent);

        // The harness has no layer service, so the counter is not folded into
        // Power (CR 613.4c); assert the counter itself.
        mine.Counters.Count(Majik.Core.Counters.CounterType.PlusOnePlusOne).Should().Be(1, "mode 2's counter landed on the creature chosen for mode 2");
        theirs.Damage.Should().Be(2, "mode 3's fight hit the creature chosen for mode 3");
        mine.Damage.Should().Be(2);
    }
}