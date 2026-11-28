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
/// CR 115.7 — changing a spell's target. The new target must be legal for
/// that spell (else the target does not change), and it is then held to the
/// same 608.2b recheck as an announced one. The redirect family ("Change the
/// target of target spell with a single target": Deflection, Imp's Mischief,
/// Shunt, Swerve) targets the SPELL when cast and chooses the new target as
/// it resolves.
/// </summary>
public class SpellRedirectTests
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

    public SpellRedirectTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
        _bear = OnBattlefield(new Creature("Grizzly Bears", "{1}{G}", 2, 2), _bob);
        _elk = OnBattlefield(new Creature("Elk", "{1}{G}", 3, 3), _bob);
    }

    private static T OnBattlefield<T>(T card, Player owner) where T : Card
    {
        card.SetOwner(owner); card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card); card.SetZone(ZoneType.Battlefield);
        return card;
    }

    private GameContext Ctx(Player self) => new(self, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private async Task<Spell> Cast(Player caster, string name, params object[][] targets)
    {
        var card = NamedCardFactory.Create(name, caster);
        card.SetZone(ZoneType.Hand);
        caster.Zones.Hand.AddCard(card);
        var def = SpellDefinitionResolverFactory.Create(Repo, eventBus: _bus, zones: _zones)!(card, caster, _stack)!;
        var agent = new ScriptedAgent();
        foreach (var t in targets) agent.QueueTargets(t);
        agent.QueueMana(ManaPayment.Empty);
        return (Spell)await _flow.CastAsync(caster, card, def, agent, Ctx(caster));
    }

    [Fact]
    public async Task TheRedirector_RejectsANewTargetTheSpellCannotTarget()
    {
        var bolt = await Cast(_alice, "Lightning Bolt", new object[] { _bear });
        _elk.AddAbility(new KeywordAbility("Hexproof", _elk, _bob));

        SpellRedirector.RedirectTopSpellSingleTarget(_stack, _elk, Ctx(_alice)).Should().BeFalse();
        SpellRedirector.RedirectTopSpellSingleTarget(_stack, bolt, Ctx(_alice)).Should().BeFalse("a spell can't target itself (CR 115.5)");

        bolt.ChosenTargets.Should().Equal(_bear);
    }

    [Fact]
    public async Task ARedirectedTarget_IsRecheckedOnResolution_LikeAnAnnouncedOne()
    {
        var bolt = await Cast(_alice, "Lightning Bolt", new object[] { _bear });
        SpellRedirector.RedirectTopSpellSingleTarget(_stack, _elk, Ctx(_alice)).Should().BeTrue();
        _zones.MoveCard(_elk, ZoneType.Battlefield, ZoneType.Exile);
        _zones.MoveCard(_elk, ZoneType.Exile, ZoneType.Battlefield, _bob);
        var countered = new List<SpellCounteredEvent>();
        _bus.Subscribe<SpellCounteredEvent>(countered.Add);

        await _resolver.ResolveTopAsync(_stack, null, Ctx(_alice));

        countered.Should().ContainSingle("the redirected target was flickered: a new object (CR 400.7)");
        _elk.Damage.Should().Be(0);
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task Deflection_TargetsTheSpellItChanges_NotTheTopSpell_AndPicksTheNewTargetOnResolution()
    {
        // Bottom: Alice's Lightning Bolt at the bear. Top: Bob's Shock at Alice.
        var bolt = await Cast(_alice, "Lightning Bolt", new object[] { _bear });
        var shock = await Cast(_bob, "Shock", new object[] { _alice });
        // Bob casts Deflection targeting the BOLT (not the top spell).
        var deflection = await Cast(_bob, "Deflection", new object[] { bolt });
        deflection.ChosenTargets.Should().Equal(bolt);

        var bobAgent = new ScriptedAgent();
        bobAgent.QueueTargets(new object[] { _alice });
        await _resolver.ResolveTopAsync(_stack, p => ReferenceEquals(p, _bob) ? bobAgent : null, Ctx(_alice));

        bolt.ChosenTargets.Should().Equal(_alice);
        shock.ChosenTargets.Should().ContainSingle().Which.Should().Be(_alice, "the other spell is untouched");
        await _resolver.ResolveTopAsync(_stack, null, Ctx(_alice)); // Shock
        await _resolver.ResolveTopAsync(_stack, null, Ctx(_alice)); // Bolt
        _alice.LifeTotal.Should().Be(20 - 2 - 3);
        _bear.Damage.Should().Be(0);
    }

    [Fact]
    public async Task Deflection_OffersOnlyLegalNewTargets_AndLeavesTheTargetWhenTheChoiceIsIllegal()
    {
        var bolt = await Cast(_alice, "Lightning Bolt", new object[] { _bear });
        _elk.AddAbility(new KeywordAbility("Hexproof", _elk, _bob));
        await Cast(_bob, "Deflection", new object[] { bolt });

        var bobAgent = new ScriptedAgent();
        bobAgent.QueueTargets(new object[] { _elk });
        await _resolver.ResolveTopAsync(_stack, p => ReferenceEquals(p, _bob) ? bobAgent : null, Ctx(_alice));

        bobAgent.TargetRequestsSeen.Should().ContainSingle().Which.LegalCandidates
            .Should().NotContain(_elk).And.NotContain(_bear).And.NotContain(bolt);
        bolt.ChosenTargets.Should().ContainSingle().Which.Should().Be(_bear, "an illegal new target leaves the target unchanged (CR 115.7)");
    }
}