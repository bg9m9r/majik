using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 614.10 — "as this enters, you may reveal a Dragon card from your hand"
/// replacement (Temple of the Dragon Queen). The async path (the production ETB
/// entry path) prompts the controller's agent and, on a "yes", stamps the shared
/// <see cref="RevealedFromHandFlag"/> that the paired
/// <see cref="ConditionalEntersTappedReplacement"/> reads; the sync / no-agent /
/// declined / no-Dragon-in-hand paths leave the flag false. Mirrors
/// <see cref="ChooseColorReplacementTests"/>'s "prompt only on the async path"
/// posture.
/// </summary>
public class RevealCardFromHandReplacementTests : IDisposable
{
    public RevealCardFromHandReplacementTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static Creature DragonCard(Player owner)
    {
        var dragon = new Creature("Atarka, World Render", "{5}{R}{G}", 6, 4,
            subtypes: new[] { CardSubtype.Dragon });
        dragon.SetOwner(owner);
        owner.Zones.Hand.AddCard(dragon);
        dragon.SetZone(ZoneType.Hand);
        return dragon;
    }

    private static (Player alice, Land land, RevealedFromHandFlag flag, ReplacementBus bus) MakeWorld()
    {
        var alice = new Player("Alice", 20);
        var land = new Land("Temple of the Dragon Queen") { Owner = alice, Zone = ZoneType.Hand };
        var flag = new RevealedFromHandFlag();
        var bus = new ReplacementBus();
        bus.Register(new RevealCardFromHandReplacement(
            land, CardSubtype.Dragon, "a Dragon card", flag));
        return (alice, land, flag, bus);
    }

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(land, ZoneType.Hand, ZoneType.Battlefield, Controller: controller);

    [Fact]
    public void SyncPath_DoesNotPrompt_LeavesFlagFalse_AndPassesIntentThrough()
    {
        var (alice, land, flag, bus) = MakeWorld();
        DragonCard(alice);
        var agent = new ScriptedAgent();
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("the reveal replacement never taps");
        flag.Revealed.Should().BeFalse("sync path never prompts to reveal");
    }

    [Fact]
    public async Task AsyncPath_AgentRevealsDragon_StampsFlag_IntentUnchanged()
    {
        var (alice, land, flag, bus) = MakeWorld();
        DragonCard(alice);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(true); // reveal a Dragon
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("revealing doesn't change how the land enters");
        flag.Revealed.Should().BeTrue("the agent revealed a Dragon this way");
    }

    [Fact]
    public async Task AsyncPath_AgentDeclines_LeavesFlagFalse()
    {
        var (alice, land, flag, bus) = MakeWorld();
        DragonCard(alice);
        var agent = new ScriptedAgent();
        agent.QueueYesNo(false); // decline to reveal
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        flag.Revealed.Should().BeFalse("declined to reveal (CR 614.10 — reveal is a 'may')");
    }

    [Fact]
    public async Task AsyncPath_NoDragonInHand_DoesNotPrompt_LeavesFlagFalse()
    {
        var (alice, land, flag, bus) = MakeWorld();
        // No Dragon in hand — the engine must not prompt and the flag stays false.
        var agent = new ScriptedAgent();
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        flag.Revealed.Should().BeFalse("no Dragon card to reveal");
    }

    [Fact]
    public async Task AsyncPath_NoAgent_LeavesFlagFalse()
    {
        var (alice, land, flag, bus) = MakeWorld();
        DragonCard(alice);
        var ctx = ResolutionContext.For(alice, agent: null, game: null, chosenTargets: null);

        await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        flag.Revealed.Should().BeFalse("no agent → nothing revealed");
    }
}
