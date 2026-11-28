using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
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
/// Arc Trail and Clash of the Eikons through the live binder
/// (SpellDefinitionResolverFactory → OracleSpellBinder), not their factories.
/// </summary>
public class LiveBoundSpellShapeTests
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

    public LiveBoundSpellShapeTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _effects = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private Creature Bear(Player owner, int power = 2, int toughness = 2)
    {
        var c = new Creature("Bear", "{1}{G}", power, toughness);
        c.SetOwner(owner); c.SetController(owner); c.ActiveEffects = _effects;
        owner.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private (ICard Card, SpellDefinition Def) Live(string name)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(card);
        var def = SpellDefinitionResolverFactory.Create(Repo, effects: _effects, eventBus: _bus, zones: _zones)!(card, _alice, _stack)!;
        return (card, def);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    [Fact]
    public async Task ArcTrail_TwoDistinctTargets_TwoAndOneDamage()
    {
        var bear = Bear(_bob);
        var (card, def) = Live("Arc Trail");
        def.TargetRequests.Should().HaveCount(2, "any target, and any other target");

        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueTargets(new object[] { _bob });
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, def, agent, Ctx());
        await _resolver.ResolveTopAsync(_stack);

        bear.Damage.Should().Be(2);
        _bob.LifeTotal.Should().Be(19);
    }

    [Fact]
    public async Task ArcTrail_TheSecondTargetMustBeADifferentTarget()
    {
        var bear = Bear(_bob);
        var (card, def) = Live("Arc Trail");
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { bear });
        agent.QueueTargets(new object[] { bear });

        var act = () => _flow.CastAsync(_alice, card, def, agent, Ctx());

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*different target*");
        agent.TargetRequestsSeen.Should().HaveCount(2);
        agent.TargetRequestsSeen[1].LegalCandidates.Should().NotContain(bear, "CR 601.2c — any OTHER target")
            .And.Contain(_bob);
        card.Zone.Should().Be(ZoneType.Hand);
    }

    private Enchantment Saga(int lore, List<int> chapters)
    {
        var saga = new Enchantment("Test Saga", "{2}{R}");
        saga.SetOwner(_alice); saga.SetController(_alice); saga.ActiveEffects = _effects;
        _alice.Zones.Battlefield.AddCard(saga); saga.SetZone(ZoneType.Battlefield);
        saga.AttachSagaState(new Majik.Core.CardData.Sagas.SagaState(saga, 3, chapters.Add));
        if (lore > 0) saga.Counters.Add(Majik.Core.Counters.CounterType.Loyalty, lore);
        return saga;
    }

    [Fact]
    public async Task ClashOfTheEikons_RemoveALoreCounter_FromTargetSagaYouControl()
    {
        var chapters = new List<int>();
        var saga = Saga(2, chapters);
        var (card, def) = Live("Clash of the Eikons");
        def.TargetRequests.Where(r => r.ModeIndex == 1).Should().ContainSingle();

        var agent = new ScriptedAgent();
        agent.QueueModes(1);
        agent.QueueTargets(new object[] { saga });
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, def, agent, Ctx());
        await _resolver.ResolveTopAsync(_stack);

        saga.SagaState!.LoreCounters.Should().Be(1);
        chapters.Should().BeEmpty("removing lore counters doesn't trigger chapters (CR 714.2b)");
    }

    [Fact]
    public async Task ClashOfTheEikons_PutALoreCounter_OnTargetSagaYouControl_TriggersThatChapter()
    {
        var chapters = new List<int>();
        var saga = Saga(1, chapters);
        var theirs = new Enchantment("Their Saga", "{2}{R}");
        theirs.SetOwner(_bob); theirs.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(theirs); theirs.SetZone(ZoneType.Battlefield);
        theirs.AttachSagaState(new Majik.Core.CardData.Sagas.SagaState(theirs, 3));
        var (card, def) = Live("Clash of the Eikons");
        var request = def.TargetRequests.Single(r => r.ModeIndex == 2);
        request.ResolveCandidates(Ctx()).Should().ContainSingle().Which.Should().Be(saga, "a Saga you control");

        var agent = new ScriptedAgent();
        agent.QueueModes(2);
        agent.QueueTargets(new object[] { saga });
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, def, agent, Ctx());
        await _resolver.ResolveTopAsync(_stack);

        saga.SagaState!.LoreCounters.Should().Be(2);
        chapters.Should().ContainSingle().Which.Should().Be(2, "CR 714.2b — chapter II triggers");
    }

    private T OnField<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        if (card is Permanent p) p.ActiveEffects = _effects;
        owner.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private IReadOnlyList<object> Pool(TargetRequest r) =>
        Majik.Core.Targeting.TargetCollection.ResolveLivePool(r, Ctx(), synthesizeWhenEmpty: true);

    [Fact]
    public void DestroyTargetArtifact_OrEnchantment_BindsThePrintedTypes()
    {
        // CR 115.1. The template matched all three phrasings and bound every
        // one as "target artifact or enchantment", which classified as an
        // artifact pool: Cursebreak ("Destroy target enchantment") offered
        // artifacts and no enchantments, and Disenchant offered no enchantments.
        var rock = OnField(new Artifact("Rock", "{2}"), _bob);
        var rune = OnField(new Enchantment("Rune", "{2}{W}"), _bob);

        Pool(Live("Shatter").Def.TargetRequests[0]).Should().Equal(rock);
        Pool(Live("Cursebreak").Def.TargetRequests[0]).Should().Equal(rune);
        Pool(Live("Disenchant").Def.TargetRequests[0]).Should().BeEquivalentTo(new object[] { rock, rune });
    }

    [Fact]
    public async Task Shatter_ChosenOnAnEnchantment_DoesNotDestroyIt()
    {
        var rune = OnField(new Enchantment("Rune", "{2}{W}"), _bob);
        var (card, def) = Live("Shatter");
        var agent = new ScriptedAgent();
        agent.QueueTargets(new object[] { rune });
        agent.QueueMana(ManaPayment.Empty);
        await _flow.CastAsync(_alice, card, def, agent, Ctx());
        await _resolver.ResolveTopAsync(_stack);

        rune.Zone.Should().Be(ZoneType.Battlefield, "Shatter destroys target artifact");
    }

    [Fact]
    public void AControllerQualifierInTheDescription_RestrictsTheSynthesizedPool()
    {
        var mine = Bear(_alice);
        var theirs = Bear(_bob);
        Pool(new TargetRequest("target creature you control", 1, 1, Array.Empty<object>()))
            .Should().Equal(mine);
        Pool(new TargetRequest("target creature you don't control", 1, 1, Array.Empty<object>()))
            .Should().Equal(theirs);
        Pool(new TargetRequest("target creature an opponent controls", 1, 1, Array.Empty<object>()))
            .Should().Equal(theirs);
    }
}