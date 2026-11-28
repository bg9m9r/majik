using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 603.3b — when several of a player's triggered abilities would be put on
/// the stack at the same time, that player chooses their order. The
/// order-triggers prompt must ship every trigger id the
/// <see cref="OrderTriggersCommand"/> has to list
/// (<see cref="PromptDto.TriggerOptions"/>); before this the payload carried no
/// ids at all, so a human seat could never answer it (live repro: two opening-
/// hand Chancellor of the Tangle delayed "add {G}" triggers).
/// </summary>
public class OrderTriggersPromptTests
{
    private static RemoteAgent AliceAgent(GameFacade facade) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task OrderTriggersPrompt_ListsEveryTriggerId_InEngineOrder_AndTheyAnswerTheCommand()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var chancellor = new Creature("Chancellor of the Tangle", "{4}{G}{G}{G}", 6, 7);
        chancellor.SetOwner(facade.Alice);
        chancellor.SetController(facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(chancellor);
        chancellor.SetZone(ZoneType.Battlefield);
        var first = new TriggeredAbility(chancellor, facade.Alice, Triggers.OnEnterBattlefieldSelf(chancellor),
            effects: new IEffect[] { new Effect("Add {G}.", () => { }) });
        var second = new TriggeredAbility(chancellor, facade.Alice, Triggers.OnEnterBattlefieldSelf(chancellor));
        var prompts = new List<PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        var task = agent.OrderTriggersAsync(null!, new ITriggeredAbility[] { first, second });

        var prompt = prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(OrderTriggersCommand));
        prompt.TriggerOptions.Should().Equal(
            new TriggerOptionDto(first.Id, chancellor.InstanceId, "Chancellor of the Tangle", "Add {G}."),
            new TriggerOptionDto(second.Id, chancellor.InstanceId, "Chancellor of the Tangle", "Chancellor of the Tangle trigger"));

        var ids = prompt.TriggerOptions!.Select(t => t.TriggerId).Reverse().ToList();
        agent.Submit(new OrderTriggersCommand(ids) { PlayerId = facade.Alice.Id });
        (await task).Should().Equal(second, first);
    }

    [Fact]
    public void NonTriggerPrompts_HaveNoTriggerOptions()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var prompts = new List<PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        _ = agent.DeclareAttackersAsync(null!, Array.Empty<Permanent>());

        prompts.Should().ContainSingle().Which.TriggerOptions.Should().BeNull();
        agent.Submit(new DeclareAttackersCommand(Array.Empty<AttackerDeclarationDto>()) { PlayerId = facade.Alice.Id });
    }

    [Fact]
    public async Task TheSameAbilityTriggeringTwice_GetsTwoDistinctIds_ThatRoundTrip()
    {
        // CR 603.2 / 603.3b — an ability that triggers twice in one batch
        // (Blade of the Bloodchief when two creatures die at once) is two
        // trigger instances the controller orders independently. The engine
        // hands the agent the same ability object twice; the prompt must still
        // give each instance its own id, and the command answering it must
        // round-trip back to both instances.
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var blade = new Creature("Blade of the Bloodchief", "{1}", 0, 0);
        blade.SetOwner(facade.Alice);
        blade.SetController(facade.Alice);
        facade.Alice.Zones.Battlefield.AddCard(blade);
        blade.SetZone(ZoneType.Battlefield);
        var dies = new TriggeredAbility(blade, facade.Alice, Triggers.OnEnterBattlefieldSelf(blade),
            effects: new IEffect[] { new Effect("Put a +1/+1 counter on equipped creature.", () => { }) });
        var other = new TriggeredAbility(blade, facade.Alice, Triggers.OnEnterBattlefieldSelf(blade));
        var prompts = new List<PromptDto>();
        using var sub = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        var task = agent.OrderTriggersAsync(null!, new ITriggeredAbility[] { dies, other, dies });

        var options = prompts.Should().ContainSingle().Subject.TriggerOptions!;
        options.Should().HaveCount(3);
        options.Select(o => o.TriggerId).Should().OnlyHaveUniqueItems("each trigger instance needs its own id");
        options[0].TriggerId.Should().Be(dies.Id, "the first instance keeps the ability's id");

        agent.Submit(new OrderTriggersCommand(new[] { options[2].TriggerId, options[1].TriggerId, options[0].TriggerId })
            { PlayerId = facade.Alice.Id });
        (await task).Should().Equal(dies, other, dies);
    }
}