using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

public class TriggeredAbilityTests
{
    private readonly Player _player = new("Alice", 20);

    [Fact]
    public void Constructor_DefaultsActiveZonesToBattlefield()
    {
        var ability = NewAbility(AlwaysFires());

        ability.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield });
    }

    [Fact]
    public void Constructor_AcceptsCustomActiveZones()
    {
        var zones = new[] { ZoneType.Graveyard, ZoneType.Hand };

        var ability = NewAbility(AlwaysFires(), activeZones: zones);

        ability.ActiveZones.Should().BeEquivalentTo(zones);
    }

    [Fact]
    public void IsTriggered_DelegatesToCondition()
    {
        var matchedEvents = new List<GameEvent>();
        var condition = new EventTriggerCondition<CardDrawnEvent>((e, _) =>
        {
            matchedEvents.Add(e);
            return true;
        });
        var ability = NewAbility(condition);
        var card = new Instant("X", "1") { Owner = _player };
        var evt = new CardDrawnEvent(card, _player);

        ability.IsTriggered(evt).Should().BeTrue();
        matchedEvents.Should().ContainSingle().Which.Should().BeSameAs(evt);
    }

    [Fact]
    public void IsTriggered_ReturnsFalse_WhenSourceNotInActiveZone()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Hand };
        var ability = new TriggeredAbility(source, _player, AlwaysFires());
        var card = new Instant("X", "1") { Owner = _player };
        var evt = new CardDrawnEvent(card, _player);

        ability.IsTriggered(evt).Should().BeFalse();
    }

    [Fact]
    public void IsTriggered_ReturnsTrue_WhenSourceInOneOfActiveZones()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Graveyard };
        var ability = new TriggeredAbility(
            source, _player, AlwaysFires(),
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        var card = new Instant("X", "1") { Owner = _player };
        var evt = new CardDrawnEvent(card, _player);

        ability.IsTriggered(evt).Should().BeTrue();
    }

    [Fact]
    public void CanBePutOnStack_Defaults_True()
    {
        var ability = NewAbility(AlwaysFires());

        ability.CanBePutOnStack().Should().BeTrue();
    }

    [Fact]
    public void CanBePutOnStack_ReturnsInterveningIfResult()
    {
        var flag = false;
        var ability = NewAbility(AlwaysFires(), interveningIf: () => flag);

        ability.CanBePutOnStack().Should().BeFalse();
        flag = true;
        ability.CanBePutOnStack().Should().BeTrue();
    }

    [Fact]
    public void Resolve_ExecutesEffectsInOrder()
    {
        var calls = new List<string>();
        var ability = NewAbility(
            AlwaysFires(),
            effects: new IEffect[]
            {
                new Effect("a", () => calls.Add("a")),
                new Effect("b", () => calls.Add("b")),
            });

        ability.Resolve();

        calls.Should().Equal("a", "b");
        ability.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task OptionalPrompt_Null_ByDefault_ResolvesMandatorily()
    {
        // A trigger with no OptionalPrompt is MANDATORY — its effects run
        // unconditionally even when an agent is supplied (no yes/no is asked).
        var calls = new List<string>();
        var ability = NewAbility(
            AlwaysFires(),
            effects: new IEffect[] { new Effect("a", () => calls.Add("a")) });
        var agent = new ScriptedAgent(); // no yes/no queued — must NOT be consulted

        await ability.ResolveAsync(agent, game: null);

        calls.Should().Equal("a");
        ability.OptionalPrompt.Should().BeNull();
    }

    [Fact]
    public async Task OptionalPrompt_Decline_SkipsEffects()
    {
        // CR 603.5 — a "you may" trigger whose controller declines at
        // resolution resolves as a no-op (no effects run).
        var calls = new List<string>();
        var ability = NewOptionalAbility(
            new OptionalTriggerPrompt("do it?", BotIntent.Reanimate),
            effects: new IEffect[] { new Effect("a", () => calls.Add("a")) });
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false);

        await ability.ResolveAsync(agent, game: null);

        calls.Should().BeEmpty();
        ability.IsResolving.Should().BeFalse();
    }

    [Fact]
    public async Task OptionalPrompt_Accept_RunsEffects()
    {
        var calls = new List<string>();
        var ability = NewOptionalAbility(
            new OptionalTriggerPrompt("do it?", BotIntent.Reanimate),
            effects: new IEffect[] { new Effect("a", () => calls.Add("a")) });
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);

        await ability.ResolveAsync(agent, game: null);

        calls.Should().Equal("a");
    }

    [Fact]
    public async Task OptionalPrompt_NoAgent_AutoTakes_PreservingLegacyPosture()
    {
        // No agent registered for the controller ⇒ the "may" is auto-taken,
        // preserving the legacy mandatory-resolution posture every pre-gate
        // "you may" trigger relied on.
        var calls = new List<string>();
        var ability = NewOptionalAbility(
            new OptionalTriggerPrompt("do it?", BotIntent.Reanimate),
            effects: new IEffect[] { new Effect("a", () => calls.Add("a")) });

        await ability.ResolveAsync(agent: null, game: null);

        calls.Should().Equal("a");
    }

    [Fact]
    public void RebindTo_PreservesOptionalPrompt()
    {
        var prompt = new OptionalTriggerPrompt("do it?", BotIntent.Reanimate);
        var ability = NewOptionalAbility(prompt, effects: new IEffect[] { new Effect("a", () => { }) });
        var newSource = new Creature("Copy", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Battlefield };

        var rebound = ability.RebindTo(newSource, _player);

        rebound.OptionalPrompt.Should().Be(prompt);
    }

    [Fact]
    public void Constructor_NullCondition_Throws()
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Battlefield };

        var act = () => new TriggeredAbility(source, _player, condition: null!);

        act.Should().Throw<ArgumentNullException>();
    }

    private TriggeredAbility NewAbility(
        ITriggerCondition condition,
        IEnumerable<IEffect>? effects = null,
        Func<bool>? interveningIf = null,
        IEnumerable<ZoneType>? activeZones = null)
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Battlefield };
        return new TriggeredAbility(source, _player, condition,
            effects: effects, interveningIf: interveningIf, activeZones: activeZones);
    }

    private TriggeredAbility NewOptionalAbility(
        OptionalTriggerPrompt prompt,
        IEnumerable<IEffect> effects)
    {
        var source = new Creature("Bear", "1G", 2, 2) { Owner = _player, Zone = ZoneType.Battlefield };
        return new TriggeredAbility(source, _player, AlwaysFires(),
            effects: effects, optionalPrompt: prompt);
    }

    private static ITriggerCondition AlwaysFires() =>
        new EventTriggerCondition<GameEvent>((_, _) => true);
}
