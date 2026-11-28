using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Spell = Majik.Core.Spells.Spell;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 115.7 / 608.2b through the real cast path (SpellCastFlow + StackResolver):
/// a spell cast from hand keeps its current targets where redirect effects
/// and the resolution legality recheck see them.
/// (a) Spellskite redirects a hand-cast Lightning Bolt aimed at another creature;
/// (b) a single-target spell whose target became illegal is countered on resolution;
/// (c) a two-target spell with one target gone resolves for the legal one.
/// </summary>
public class LiveCastTargetsTests
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
    private readonly Creature _skite;

    public LiveCastTargetsTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        _skite = OnBattlefield(SpellskiteFactory.Create(_bob), _bob);
    }

    private static T OnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private async Task<(Spell Spell, ICard Card)> Cast(string name, params object[][] targets) =>
        await Cast(name, null, targets);

    private async Task<(Spell Spell, ICard Card)> Cast(string name, SpellDefinition? definition, params object[][] targets)
    {
        var card = NamedCardFactory.Create(name, _alice);
        card.SetZone(ZoneType.Hand);
        _alice.Zones.Hand.AddCard(card);
        // The production cast-time resolver (what TurnDriver uses in a live game).
        var def = definition ?? SpellDefinitionResolverFactory.Create(Repo, eventBus: _bus, zones: _zones)!(card, _alice, _stack)!;
        def.Should().NotBeNull();

        var agent = new ScriptedAgent();
        foreach (var t in targets) agent.QueueTargets(t);
        agent.QueueMana(ManaPayment.Empty);
        var ctx = new GameContext(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);
        var spell = await _flow.CastAsync(_alice, card, def, agent, ctx);
        card.Zone.Should().Be(ZoneType.Stack);
        return ((Spell)spell, card);
    }

    private void LeaveTheBattlefield(Creature c)
    {
        _bob.Zones.Battlefield.RemoveCard(c);
        _bob.Zones.Exile.AddCard(c); c.SetZone(ZoneType.Exile);
    }

    [Fact]
    public async Task Spellskite_RedirectsAHandCastLightningBolt_ToItself()
    {
        var (bolt, _) = await Cast("Lightning Bolt", new object[] { _bear });

        var redirect = _skite.Abilities.OfType<ActivatedAbility>().First();
        redirect.SetChosenTargets(new[] { new object[] { bolt } });
        await redirect.ResolveAsync(null, null);
        bolt.ChosenTargets.Should().Equal(_skite);

        await _resolver.ResolveTopAsync(_stack);

        _skite.Damage.Should().Be(3, "the bolt was redirected to Spellskite (CR 115.7)");
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task Deflection_ARedirectTemplateCard_RedirectsAHandCastLightningBolt()
    {
        // RedirectTemplate binds 4 cards (Deflection, Imp's Mischief, Shunt,
        // Swerve); all go through SpellRedirector.RedirectTopSpellSingleTarget.
        var (bolt, _) = await Cast("Lightning Bolt", new object[] { _bear });
        await Cast("Deflection", new object[] { _skite });

        await _resolver.ResolveTopAsync(_stack); // Deflection
        bolt.ChosenTargets.Should().Equal(_skite);
        await _resolver.ResolveTopAsync(_stack); // Lightning Bolt

        _skite.Damage.Should().Be(3);
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task ASingleTargetSpell_WhoseTargetBecameIllegal_IsCounteredOnResolution()
    {
        var (_, card) = await Cast("Lightning Bolt", new object[] { _bear });
        var countered = new List<SpellCounteredEvent>();
        _bus.Subscribe<SpellCounteredEvent>(countered.Add);
        LeaveTheBattlefield(_bear);

        await _resolver.ResolveTopAsync(_stack);

        countered.Should().ContainSingle().Which.Reason.Should().Be(SpellCounterReason.AllTargetsIllegal);
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
        _alice.Zones.Stack.GetCards().Should().NotContain(card);
    }

    [Fact]
    public async Task ATwoTargetSpell_WithOneTargetGone_ResolvesForTheOther()
    {
        // Arc Trail: 2 damage to any target and 1 damage to another target.
        // Its named factory's two-request definition: the live resolver binds
        // Arc Trail to a one-target template (reported separately).
        var (_, card) = await Cast("Arc Trail", ArcTrailFactory.BuildSpellDefinition(o => o!),
            new object[] { _bear }, new object[] { _skite });
        var countered = new List<SpellCounteredEvent>();
        _bus.Subscribe<SpellCounteredEvent>(countered.Add);
        LeaveTheBattlefield(_bear);

        await _resolver.ResolveTopAsync(_stack);

        countered.Should().BeEmpty("one target is still legal (CR 608.2b)");
        _skite.Damage.Should().Be(1);
        _bear.Damage.Should().Be(0, "an illegal target is not affected (CR 608.2b)");
        card.Zone.Should().Be(ZoneType.Graveyard);
    }
}
