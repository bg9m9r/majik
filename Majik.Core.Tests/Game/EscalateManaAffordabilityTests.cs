using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
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
/// CR 702.121a / CR 601.2f-h — escalate's mana is part of the spell's total
/// cost, so a caster with untapped lands can choose the extra modes without
/// floating mana first. The affordability check is the one the cast path uses
/// for the base cost (floating pool, else auto-selected untapped sources),
/// applied to the base cost plus the escalate mana. The casts below pay the
/// base cost the way the TurnDriver dispatcher does: sources picked for the
/// base cost before the cast, paid at CR 601.2h.
/// </summary>
public class EscalateManaAffordabilityTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly Majik.Core.Effects.ContinuousEffectsService _effects;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public EscalateManaAffordabilityTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _effects = new Majik.Core.Effects.ContinuousEffectsService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    private GameContext Ctx() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private List<Land> Lands(int n, string name = "Mountain")
    {
        var lands = new List<Land>();
        for (var i = 0; i < n; i++)
        {
            var land = (Land)NamedCardFactory.Create(name, _alice);
            land.SetOwner(_alice); land.SetController(_alice);
            land.ActiveEffects = _effects;
            _alice.Zones.Battlefield.AddCard(land); land.SetZone(ZoneType.Battlefield);
            lands.Add(land);
        }
        return lands;
    }

    private Creature Bear(Player owner)
    {
        var c = new Creature("Bear", "{1}{G}", 2, 2);
        c.SetOwner(owner); c.SetController(owner); c.ActiveEffects = _effects;
        owner.Zones.Battlefield.AddCard(c); c.SetZone(ZoneType.Battlefield);
        return c;
    }

    /// <summary>Cast as the dispatcher does: pick sources for the base cost
    /// up front, pay them through the CR 601.2h callback.</summary>
    private Task<Spell> CastLive(ICard card, SpellDefinition def, ScriptedAgent agent)
    {
        var resolver = new ManaPaymentResolver(_effects, _bus);
        var baseCost = CostReduction.GetEffectiveCost(card, _alice, new[] { _alice, _bob });
        resolver.TryAutoSelectSources(_alice, baseCost, out var payment).Should().BeTrue();
        return _flow.CastAsync(_alice, card, def, agent, Ctx(),
            preChosenMana: payment,
            payManaCost: _ => resolver.Pay(_alice, baseCost, payment));
    }

    private Sorcery Defiance()
    {
        var cd = CollectiveDefianceFactory.Create(_alice);
        cd.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(cd);
        return cd;
    }

    private ScriptedAgent DefianceAgent(Creature bear)
    {
        var agent = new ScriptedAgent();
        agent.QueueModes(CollectiveDefianceFactory.ModeDamageFour, CollectiveDefianceFactory.ModeDamageThree);
        agent.QueueTargets(new object[] { bear });
        agent.QueueTargets(new object[] { _bob });
        return agent;
    }

    [Fact]
    public async Task CollectiveDefiance_WithUntappedLandsAndAnEmptyPool_CanEscalate()
    {
        var lands = Lands(4);
        var bear = Bear(_bob);

        var spell = await CastLive(Defiance(),
            CollectiveDefianceFactory.BuildDefinition(_alice, o => o), DefianceAgent(bear));

        lands.Should().OnlyContain(l => l.IsTapped, "{1}{R}{R} plus escalate {1}");
        _alice.ManaPool.Total.Should().Be(0);
        spell.Resolve();
        bear.Damage.Should().Be(4);
        _bob.LifeTotal.Should().Be(17);
    }

    [Fact]
    public async Task CollectiveDefiance_WithOnlyTheBaseCost_IsRefusedBeforeAnythingIsPaid()
    {
        var lands = Lands(3);
        var bear = Bear(_bob);
        var cd = Defiance();

        var act = () => CastLive(cd, CollectiveDefianceFactory.BuildDefinition(_alice, o => o), DefianceAgent(bear));

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*Escalate*");
        lands.Should().OnlyContain(l => !l.IsTapped, "the shortfall is found before any payment (CR 601.2g)");
    }

    [Fact]
    public async Task TemplateBoundEscalate_WithUntappedLands_CanEscalate()
    {
        // Borrowed Malevolence {B}, escalate {2}: +1/+1 on one creature,
        // -1/-1 on another. Bound by ModalChooseOneTemplate.
        var lands = Lands(3, "Swamp");
        var mine = Bear(_alice);
        var theirs = Bear(_bob);
        var card = DeckCardBuilder.Build("Borrowed Malevolence", _alice, Repo,
            replacements: new Majik.Core.Effects.ReplacementBus(), effects: _effects,
            triggers: null, zones: _zones, eventBus: _bus, routeThroughNamedFactories: true);
        card.ManaCost.ToString().Should().Be("{B}");
        card.SetZone(ZoneType.Hand); _alice.Zones.Hand.AddCard(card);
        var def = SpellDefinitionResolverFactory.Create(Repo, effects: _effects, eventBus: _bus, zones: _zones)!(card, _alice, _stack)!;
        def.Escalate.Should().NotBeNull("the template binds escalate");
        var agent = new ScriptedAgent();
        agent.QueueModes(0, 1);
        agent.QueueTargets(new object[] { mine });
        agent.QueueTargets(new object[] { theirs });

        await CastLive(card, def, agent);

        lands.Should().OnlyContain(l => l.IsTapped, "{B} plus escalate {2}");
    }
}