using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 614.12 — "as this land enters, choose a color" replacement. The async
/// path (<c>ReplaceAsync</c>, the production ETB entry path) prompts the
/// controller's agent and stamps the pick onto the shared
/// <see cref="ColorChoice"/> the land's synthesized
/// <see cref="ManaAbility"/> reads; the sync / no-agent path keeps the seeded
/// default (one producible colour — strictly narrower than the old
/// over-permissive five-WUBRG binding). Mirrors
/// <see cref="ShockLandReplacementTests"/>'s "prompt only on the async path"
/// posture.
/// </summary>
public class ChooseColorReplacementTests : IDisposable
{
    public ChooseColorReplacementTests() => AgentRegistry.Clear();
    public void Dispose() => AgentRegistry.Clear();

    private static (Player alice, Land land, ColorChoice choice, ReplacementBus bus) MakeWorld()
    {
        var alice = new Player("Alice", 20);
        var land = new Land("Sunken Citadel") { Owner = alice, Zone = ZoneType.Hand };
        var choice = new ColorChoice(ManaColor.White);
        var bus = new ReplacementBus();
        bus.Register(new ChooseColorReplacement(land, choice));
        return (alice, land, choice, bus);
    }

    private static ZoneMoveIntent EtbIntent(Land land, Player controller) =>
        new(land, ZoneType.Hand, ZoneType.Battlefield, Controller: controller);

    [Fact]
    public void SyncPath_DoesNotPrompt_KeepsSeededDefault_AndPassesIntentThrough()
    {
        var (alice, land, choice, bus) = MakeWorld();
        // Even with an agent registered, the sync path never prompts.
        var agent = new ScriptedAgent();
        AgentRegistry.Set(alice, agent);

        var after = bus.Apply(EtbIntent(land, alice));

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("the choose-color replacement never taps");
        choice.Chosen.Should().Be(ManaColor.White, "sync path keeps the seeded default colour");
    }

    [Fact]
    public async Task AsyncPath_AgentPicksColor_StampsChoice_IntentUnchanged()
    {
        var (alice, land, choice, bus) = MakeWorld();
        var agent = new ScriptedAgent();
        // ChooseColorAsync routes through the declarative ChooseAsync PickOne
        // over [W,U,B,R,G]; pick the Blue candidate.
        agent.QueueChoice(cands => new[] { cands.First(c => (ManaColor)c == ManaColor.Blue) });
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        after!.EntersTapped.Should().BeFalse("choosing a colour doesn't change how the land enters");
        choice.Chosen.Should().Be(ManaColor.Blue, "the agent's pick was stamped onto the holder");
    }

    [Fact]
    public async Task AsyncPath_NoAgent_KeepsSeededDefault()
    {
        var (alice, land, choice, bus) = MakeWorld();
        // No agent registered, none on the context.
        var ctx = ResolutionContext.For(alice, agent: null, game: null, chosenTargets: null);

        var after = await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        after.Should().NotBeNull();
        choice.Chosen.Should().Be(ManaColor.White, "no agent → seeded default colour stands");
    }

    [Fact]
    public async Task EndToEnd_BinderBoundLand_AgentChoice_DrivesManaProduction()
    {
        // The full binder-chain shape: OracleManaBinder binds the dynamic
        // chosen-colour abilities + stashes the holder; the ETB replacement
        // (what ChooseColorLandBinder registers in prod) stamps the agent's
        // pick; the bound mana ability then produces that colour.
        var alice = new Player("Alice", 20);
        var repo = new EmbeddedCardRepository();
        var entity = repo.GetByName("Sunken Citadel");
        entity.Should().NotBeNull();
        var parsed = TypeLineParser.Parse(entity!.TypeLine);
        var land = new Land("Sunken Citadel", parsed.Supertypes, parsed.Subtypes);
        land.SetOwner(alice);
        land.SetController(alice);
        land.SetZone(ZoneType.Hand);

        OracleManaBinder.Bind(land, entity, alice);
        var bus = new ReplacementBus();
        ChooseColorLandBinder.Bind(land, bus).Should().BeTrue(
            "Sunken Citadel is a chosen-colour land");

        var agent = new ScriptedAgent();
        agent.QueueChoice(cands => new[] { cands.First(c => (ManaColor)c == ManaColor.Red) });
        var ctx = ResolutionContext.For(alice, agent, game: null, chosenTargets: null);

        await bus.ApplyAsync(EtbIntent(land, alice), ctx);

        var single = land.Abilities.OfType<ManaAbility>().Single(a => a.ManaGenerated.TotalValue == 1);
        single.Activate().Red.Should().Be(1, "the {T}: Add one mana ability produces the chosen colour (red)");
    }
}
