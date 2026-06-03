using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// CR 701.5b — controller-scoped "spells you control can't be countered"
/// static (Destiny Spinner). A live <see cref="UncounterableControllerStatic"/>
/// marker on the caster's battlefield, whose covered type set includes one of
/// the cast card's types, makes <see cref="SpellCastFlow"/> stamp
/// <see cref="Majik.Core.Spells.Spell.CannotBeCountered"/> on the resolving
/// spell — without any per-spell self marker or targeting.
/// </summary>
public class SpellCastFlowUncounterableControllerStaticTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly SpellCastFlow _flow;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellCastFlowUncounterableControllerStaticTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
        _flow = new SpellCastFlow(_stack, _zones, _bus);
    }

    private GameContext NewContext() =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, StepStateType.PreCombatMain, _stack);

    private static Permanent MakeSource(Player controller, params CardType[] coveredTypes)
    {
        var src = new Enchantment("Destiny Spinner Stand-in", "{1}{G}");
        src.SetOwner(controller);
        src.SetController(controller);
        src.AddAbility(new UncounterableControllerStatic(src, controller, coveredTypes));
        return src;
    }

    [Fact]
    public async Task CoveredCreatureSpell_FromController_IsStampedUncounterable()
    {
        var source = MakeSource(_alice, CardType.Creature, CardType.Enchantment);
        _alice.Zones.Battlefield.AddCard(source);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, bear,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, NewContext());

        spell.CannotBeCountered.Should().BeTrue(
            "CR 701.5b — a creature spell the controller casts is covered by the static");
    }

    [Fact]
    public async Task UncoveredInstantSpell_FromController_IsNotStamped()
    {
        // Destiny Spinner only covers Creature + Enchantment; an instant slips through.
        var source = MakeSource(_alice, CardType.Creature, CardType.Enchantment);
        _alice.Zones.Battlefield.AddCard(source);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, NewContext());

        spell.CannotBeCountered.Should().BeFalse(
            "the static is type-restricted to creature + enchantment spells");
    }

    [Fact]
    public async Task CoveredSpell_CastByOpponent_IsNotStamped()
    {
        // The static is controller-scoped: Bob's spell is unaffected by Alice's source.
        var source = MakeSource(_alice, CardType.Creature, CardType.Enchantment);
        _alice.Zones.Battlefield.AddCard(source);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _bob, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var ctx = new GameContext(_bob, new[] { _alice, _bob }, _bob, 1, StepStateType.PreCombatMain, _stack);
        var spell = await _flow.CastAsync(_bob, bear,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, ctx);

        spell.CannotBeCountered.Should().BeFalse(
            "\"spells YOU control can't be countered\" — only the static's controller benefits");
    }

    [Fact]
    public async Task SourceNotOnBattlefield_DoesNotGrant()
    {
        // Marker source lives in hand, not the battlefield — the static is inactive.
        var source = MakeSource(_alice, CardType.Creature, CardType.Enchantment);
        _alice.Zones.Hand.AddCard(source);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, bear,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, NewContext());

        spell.CannotBeCountered.Should().BeFalse(
            "battlefield gating — a source off the battlefield grants nothing");
    }

    [Fact]
    public async Task UnrestrictedStatic_CoversEverySpellType()
    {
        // Empty covered set = "spells you control can't be countered" (no type filter).
        var source = MakeSource(_alice /* no covered types -> unrestricted */);
        _alice.Zones.Battlefield.AddCard(source);

        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice, Zone = ZoneType.Hand };
        var agent = new ScriptedAgent();
        agent.QueueMana(ManaPayment.Empty);

        var spell = await _flow.CastAsync(_alice, bolt,
            SpellDefinition.Vanilla(_ => System.Array.Empty<IEffect>()), agent, NewContext());

        spell.CannotBeCountered.Should().BeTrue(
            "an unrestricted static covers every spell the controller casts");
    }
}
