using FluentAssertions;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// CR 508.1a / 509.1a — the declare-attackers and declare-blockers prompts
/// ship the engine-computed eligibility lists the agent is handed
/// (<see cref="PromptDto.EligibleAttackerIds"/>, <see cref="PromptDto.AttackerIds"/>,
/// <see cref="PromptDto.EligibleBlockerIds"/>) instead of dropping them, so a
/// client can highlight exactly the creatures that may attack / block.
/// </summary>
public class CombatPromptEligibilityTests
{
    private static Creature OnBattlefield(Majik.Core.Players.Player owner, string name)
    {
        var creature = new Creature(name, "{1}{G}", 2, 2);
        creature.SetOwner(owner);
        creature.SetController(owner);
        owner.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        return creature;
    }

    private static RemoteAgent AliceAgent(GameFacade facade) =>
        (RemoteAgent)typeof(GameFacade)
            .GetField("_aliceAgent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(facade)!;

    [Fact]
    public async Task DeclareAttackersPrompt_CarriesTheEngineSuppliedEligibleAttackers()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var bear = OnBattlefield(facade.Alice, "Bear");
        var elf = OnBattlefield(facade.Alice, "Elf");
        OnBattlefield(facade.Alice, "Sick Wolf"); // not in the engine's eligible list
        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        var task = agent.DeclareAttackersAsync(null!, new Permanent[] { elf, bear });

        var prompt = prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(DeclareAttackersCommand));
        prompt.EligibleAttackerIds.Should().Equal(elf.InstanceId, bear.InstanceId);
        prompt.AttackerIds.Should().BeNull();
        prompt.EligibleBlockerIds.Should().BeNull();
        prompt.LegalActions.Should().BeNull();

        agent.Submit(new DeclareAttackersCommand(Array.Empty<AttackerDeclarationDto>()) { PlayerId = facade.Alice.Id });
        (await task).Attackers.Should().BeEmpty();
        agent.PendingPayload.Should().BeNull("the combat payload is cleared once the prompt resolves");
    }

    [Fact]
    public async Task DeclareAttackersPrompt_NoEligibleAttackers_ShipsAnEmptyList()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        var task = agent.DeclareAttackersAsync(null!, Array.Empty<Permanent>());

        prompts.Should().ContainSingle().Which.EligibleAttackerIds.Should().NotBeNull().And.BeEmpty();
        agent.Submit(new DeclareAttackersCommand(Array.Empty<AttackerDeclarationDto>()) { PlayerId = facade.Alice.Id });
        await task;
    }

    [Fact]
    public async Task DeclareBlockersPrompt_CarriesAttackersAndEligibleBlockers()
    {
        var facade = GameFacade.Create("Alice", "Bob", Array.Empty<ICard>(), Array.Empty<ICard>());
        var attackerA = OnBattlefield(facade.Bob, "Goblin");
        var attackerB = OnBattlefield(facade.Bob, "Orc");
        var wall = OnBattlefield(facade.Alice, "Wall");
        var bear = OnBattlefield(facade.Alice, "Bear");
        var prompts = new List<PromptDto>();
        using var _ = facade.SubscribePrompts(prompts.Add);
        var agent = AliceAgent(facade);

        var task = agent.DeclareBlockersAsync(
            null!, new Permanent[] { attackerB, attackerA }, new Permanent[] { wall, bear });

        var prompt = prompts.Should().ContainSingle().Subject;
        prompt.ExpectedKinds.Should().Equal(nameof(DeclareBlockersCommand));
        prompt.AttackerIds.Should().Equal(attackerB.InstanceId, attackerA.InstanceId);
        prompt.EligibleBlockerIds.Should().Equal(wall.InstanceId, bear.InstanceId);
        prompt.EligibleAttackerIds.Should().BeNull();

        agent.Submit(new DeclareBlockersCommand(Array.Empty<BlockerDeclarationDto>()) { PlayerId = facade.Alice.Id });
        (await task).Blockers.Should().BeEmpty();
    }
}
