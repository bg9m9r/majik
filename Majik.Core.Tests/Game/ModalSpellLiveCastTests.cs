using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 700.2 — modal spells through the real cast path (SpellCastFlow +
/// StackResolver, with the production spell-definition resolver). The pick word
/// sets how many modes are chosen ("choose two" 2/2, "one or both" 1/2, "one or
/// more" 1/N; no mode twice, CR 700.2d); only the chosen modes' targets are
/// chosen, in printed order; the chosen modes resolve in printed order
/// (CR 608.2c).
/// </summary>
public class ModalSpellLiveCastTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly Majik.Core.Effects.ContinuousEffectsService _effects;
    private readonly SpellCastFlow _flow;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public ModalSpellLiveCastTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _effects = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private T OnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        if (card is Permanent p) p.ActiveEffects = _effects;
        owner.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        if (card is Creature c) c.ClearSummoningSickness();
        return card;
    }

    private SpellDefinition Definition(ICard card) =>
        SpellDefinitionResolverFactory.Create(Repo, effects: _effects, eventBus: _bus, zones: _zones)!(card, _alice, _stack)!;

    private async Task<Spell> CastAndResolve(string name, int[] modes, params object[][] targets)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        var def = Definition(card);
        var agent = new ScriptedAgent();
        agent.QueueModes(modes);
        foreach (var t in targets) agent.QueueTargets(t);
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var spell = (Spell)await _flow.CastAsync(_alice, card, def, agent, ctx);
        await _resolver.ResolveTopAsync(_stack);
        return spell;
    }

    private (int Min, int Max) Bounds(string name)
    {
        var def = Definition(NamedCardFactory.Create(name, _alice));
        return (def.MinModes, def.MaxModes);
    }

    [Fact]
    public void ThePickWord_SetsTheModeBounds()
    {
        Bounds("Dromoka's Command").Should().Be((2, 2), "choose two");
        Bounds("Kolaghan's Command").Should().Be((2, 2), "choose two");
        Bounds("Artful Takedown").Should().Be((1, 2), "choose one or both");
        Bounds("Casualties of War").Should().Be((1, 5), "choose one or more");
        Bounds("Lightning Helix").Should().Be((1, 1), "not modal: the default");
    }

    [Fact]
    public async Task DromokasCommand_ChoosesTwoModes_AndResolvesThemInPrintedOrder()
    {
        // Modes: 2 = +1/+1 counter on target creature, 3 = your creature fights
        // one you don't control. Announced 3 then 2. In printed order the
        // counter lands first: a 3/3 deals 3 to the 2/3 (lethal) and survives
        // its 2. In announced order the 2/2 would deal only 2.
        var mine = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _alice);
        var theirs = OnBattlefield(new Creature("Wall Bear", "{1}{G}", 2, 3), _bob);

        await CastAndResolve("Dromoka's Command", new[] { 3, 2 },
            new object[] { mine }, new object[] { mine }, new object[] { theirs });

        mine.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        theirs.Damage.Should().Be(3, "the counter was put on before the fight (CR 608.2c)");
        mine.Damage.Should().Be(2);
    }

    [Fact]
    public async Task KolaghansCommand_DiscardAndDamage()
    {
        // Live modes (oracle order): 0 return creature card, 1 target player
        // discards, 2 destroy artifact, 3 2 damage to any target.
        var bear = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _bob);
        var held = new Creature("Held", "{G}", 1, 1);
        held.SetOwner(_bob); _bob.Zones.Hand.AddCard(held); held.SetZone(ZoneType.Hand);

        await CastAndResolve("Kolaghan's Command", new[] { 1, 3 },
            new object[] { _bob }, new object[] { bear });

        _bob.Zones.Hand.GetCards().Should().BeEmpty("Bob discarded a card");
        _bob.Zones.Graveyard.GetCards().Should().Contain(held);
        bear.Damage.Should().Be(2);
    }

    [Fact]
    public async Task ArtfulTakedown_OneOrBoth_ChoosingOne()
    {
        var a = OnBattlefield(new Creature("A", "{1}{G}", 2, 2), _bob);
        var b = OnBattlefield(new Creature("B", "{1}{G}", 3, 5), _bob);

        await CastAndResolve("Artful Takedown", new[] { 1 }, new object[] { b });

        (b.Power, b.Toughness).Should().Be((1, 1), "-2/-4");
        a.IsTapped.Should().BeFalse("the tap mode was not chosen");
    }

    [Fact]
    public async Task ArtfulTakedown_OneOrBoth_ChoosingBoth()
    {
        var a = OnBattlefield(new Creature("A", "{1}{G}", 2, 2), _bob);
        var b = OnBattlefield(new Creature("B", "{1}{G}", 3, 5), _bob);

        await CastAndResolve("Artful Takedown", new[] { 0, 1 }, new object[] { a }, new object[] { b });

        a.IsTapped.Should().BeTrue();
        (b.Power, b.Toughness).Should().Be((1, 1));
    }

    [Fact]
    public async Task CasualtiesOfWar_OneOrMore_ChoosingTwo()
    {
        // Modes: 1 destroy target creature, 3 destroy target land.
        var bear = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _bob);
        var land = OnBattlefield(new Land("Forest"), _bob);

        await CastAndResolve("Casualties of War", new[] { 3, 1 }, new object[] { bear }, new object[] { land });

        bear.Zone.Should().Be(ZoneType.Graveyard);
        land.Zone.Should().Be(ZoneType.Graveyard);
    }
}
