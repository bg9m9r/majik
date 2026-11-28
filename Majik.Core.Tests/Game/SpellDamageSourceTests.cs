using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
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
/// CR 120.1 / 609.7 — the source of a spell's damage is the spell, not the
/// player who cast it. A replacement keyed on the source's characteristics
/// (colour, card type, controller) reads the spell's card.
/// </summary>
public class SpellDamageSourceTests
{
    private static readonly EmbeddedCardRepository Repo = new();

    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly StackResolver _resolver;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellDamageSourceTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
        _resolver = new StackResolver(_bus, _zones);
    }

    private GameContext Ctx(Player self) => new(self, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private async Task<Spell> Cast(Player caster, string name, params object[][] targets)
    {
        var card = NamedCardFactory.Create(name, caster);
        card.SetZone(ZoneType.Hand);
        caster.Zones.Hand.AddCard(card);
        var def = SpellDefinitionResolverFactory.Create(Repo, replacements: _replacements, eventBus: _bus, zones: _zones)!(card, caster, _stack)!;
        var agent = new ScriptedAgent();
        foreach (var t in targets) agent.QueueTargets(t);
        agent.QueueMana(ManaPayment.Empty);
        return (Spell)await _flow.CastAsync(caster, card, def, agent, Ctx(caster));
    }

    [Fact]
    public async Task PreventingDamageFromRedSources_PreventsLightningBolt()
    {
        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_bob); bear.SetController(_bob);
        _bob.Zones.Battlefield.AddCard(bear); bear.SetZone(ZoneType.Battlefield);
        _replacements.Register(new PreventAllDamageFromColoredSourcesToCreatureShield(bear, ManaColor.Red));

        await Cast(_alice, "Lightning Bolt", new object[] { bear });
        await _resolver.ResolveTopAsync(_stack, null, Ctx(_alice));

        bear.Damage.Should().Be(0, "Lightning Bolt is a red source (CR 120.1, 105.2)");
        bear.Zone.Should().Be(ZoneType.Battlefield);
    }
}
