using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 608.2b — on resolution each target is rechecked: it must still be the
/// same object (CR 400.7: a card that changed zones, even back to the same
/// zone, is a new object), still targetable by the spell (hexproof, shroud,
/// protection: CR 115.5, 702.11, 702.16), and still meet its own target
/// request (the "creature you don't control" a controller change breaks).
/// </summary>
public class TargetRecheckOnResolutionTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Creature _bear;
    private readonly Creature _elk;
    private readonly List<SpellCounteredEvent> _countered = new();

    public TargetRecheckOnResolutionTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        _elk = OnBattlefield(new Creature("Elk", "{1}{G}", 3, 3), _bob);
        _bus.Subscribe<SpellCounteredEvent>(_countered.Add);
    }

    private static T OnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private GameContext Ctx => new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private async Task<Spell> Cast(string name, SpellDefinition? definition, params object[][] targets)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        var def = definition ?? SpellDefinitionResolverFactory.Create(Repo, eventBus: _bus, zones: _zones)!(card, _alice, _stack)!;
        var agent = new ScriptedAgent();
        foreach (var t in targets) agent.QueueTargets(t);
        agent.QueueMana(ManaPayment.Empty);
        return (Spell)await _flow.CastAsync(_alice, card, def, agent, Ctx);
    }

    [Fact]
    public async Task ATargetThatGainsHexproofInResponse_IsIllegal_AndTheSpellIsCountered()
    {
        await Cast("Lightning Bolt", null, new object[] { _bear });
        _bear.AddAbility(new KeywordAbility("Hexproof", _bear, _bob));

        await _resolver.ResolveTopAsync(_stack, null, Ctx);

        _countered.Should().ContainSingle().Which.Reason.Should().Be(SpellCounterReason.AllTargetsIllegal);
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task AFlickeredTarget_IsANewObject_AndTheSpellIsCountered()
    {
        // Ephemerate in response: exiled, then returned. Back on the
        // battlefield, but a new object (CR 400.7).
        await Cast("Lightning Bolt", null, new object[] { _bear });
        _zones.MoveCard(_bear, ZoneType.Battlefield, ZoneType.Exile);
        _zones.MoveCard(_bear, ZoneType.Exile, ZoneType.Battlefield, _bob);
        _bear.Zone.Should().Be(ZoneType.Battlefield);

        await _resolver.ResolveTopAsync(_stack, null, Ctx);

        _countered.Should().ContainSingle().Which.Reason.Should().Be(SpellCounterReason.AllTargetsIllegal);
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task ATwoTargetSpell_WithProtectionOnOneTarget_ResolvesForTheOther()
    {
        await Cast("Arc Trail", ArcTrailFactory.BuildSpellDefinition(o => o!),
            new object[] { _bear }, new object[] { _elk });
        _bear.AddAbility(new KeywordAbility("Protection from Red", _bear, _bob));

        await _resolver.ResolveTopAsync(_stack, null, Ctx);

        _countered.Should().BeEmpty("one target is still legal (CR 608.2b)");
        _bear.Damage.Should().Be(0, "protection from red: an illegal target is not affected");
        _elk.Damage.Should().Be(1);
    }

    [Fact]
    public async Task ATargetWhoseControllerChanged_NoLongerMeetsItsRequest()
    {
        // "Target creature you don't control": the caster gains control of
        // it in response, so it no longer meets the request.
        var hit = new List<object>();
        var request = new TargetRequest("target creature you don't control", 1, 1, Array.Empty<object>(),
            CandidateGatherer: ctx => ctx.AllPlayers.Where(p => !ReferenceEquals(p, ctx.Self))
                .SelectMany(p => p.Zones.Battlefield.GetCards()).OfType<Creature>().Cast<object>().ToList());
        var def = new SpellDefinition(Array.Empty<string>(), false, new[] { request },
            chosen => new IEffect[] { new Effect("hit", () => hit.AddRange(chosen.Targets[0])) });
        await Cast("Lightning Bolt", def, new object[] { _bear });
        _bob.Zones.Battlefield.RemoveCard(_bear);
        _alice.Zones.Battlefield.AddCard(_bear);
        _bear.ChangeController(_alice);

        await _resolver.ResolveTopAsync(_stack, null, Ctx);

        _countered.Should().ContainSingle().Which.Reason.Should().Be(SpellCounterReason.AllTargetsIllegal);
        hit.Should().BeEmpty();
    }

    [Fact]
    public async Task AnUnchangedTarget_StillResolves()
    {
        await Cast("Lightning Bolt", null, new object[] { _bear });

        await _resolver.ResolveTopAsync(_stack, null, Ctx);

        _countered.Should().BeEmpty();
        _bear.Damage.Should().Be(3);
    }
}