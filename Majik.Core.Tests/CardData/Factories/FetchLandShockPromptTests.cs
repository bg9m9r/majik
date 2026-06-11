using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// CR 614.1c / CR 701.19a — fetching a shock land (e.g. Overgrown Tomb)
/// onto the battlefield via a fetchland must run the shock land's
/// "you may pay 2 life; if you don't, it enters tapped" replacement
/// through the SAME agent-prompting async path the direct play-land path
/// uses, NOT the deterministic sync auto-pay path.
///
/// Before the fix, <see cref="FetchLandCycleFactory"/> moved the tutored
/// land with the synchronous <c>ZoneService.MoveCard</c> → the shock
/// replacement's sync <c>Replace</c> auto-paid 2 life with no prompt and
/// the land entered untapped regardless of the agent's wishes. The fix
/// routes the move through <c>MoveCardToAsync(... ctx ...)</c> so the
/// agent on the resolution context is consulted.
/// </summary>
[Trait("Color", "C")]
public class FetchLandShockPromptTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly IEventBus _bus = new EventBus();

    public FetchLandShockPromptTests()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    public void Dispose()
    {
        AgentRegistry.Clear();
        ZoneServiceRegistry.Clear();
    }

    /// <summary>
    /// Builds the live service graph + a fetchland able to find a shock
    /// land in the library, with the prod <see cref="ShockLandReplacement"/>
    /// registered on the ZoneService's bus (mirrors ShockLandBinder).
    /// Returns the shock land and the fetchland's tutor effect.
    /// </summary>
    private (Land shock, IEffect fetchEffect) BuildFetchIntoShock()
    {
        var replacements = new ReplacementBus();
        var zones = new ZoneService(_bus, replacements);
        ZoneServiceRegistry.Set(_alice, zones);

        // Overgrown Tomb — Swamp/Forest shock land. Build its land identity
        // via the cycle factory (no replacement wired by the factory), then
        // register the PROD ShockLandReplacement on the bus, exactly as
        // ShockLandBinder does on the binder-chain load path.
        var shock = ShockLandCycleFactory.Create(
            _alice,
            new[] { "Overgrown Tomb", "Swamp", "Forest", "B", "G" },
            replacements: null);
        replacements.Register(new ShockLandReplacement(shock));

        _alice.Zones.Library.AddCard(shock);
        shock.SetZone(ZoneType.Library);

        // Verdant Catacombs — fetches Swamp or Forest, finds Overgrown Tomb.
        var fetch = FetchLandCycleFactory.Create(
            _alice, new[] { "Verdant Catacombs", "Swamp", "Forest" });
        var fetchAbility = fetch.Abilities.OfType<ActivatedAbility>().Single();
        var fetchEffect = fetchAbility.Effects.Single();

        return (shock, fetchEffect);
    }

    [Fact]
    public async Task Fetch_AgentDeclines_ShockEntersTapped_NoLifePaid()
    {
        var (shock, fetchEffect) = BuildFetchIntoShock();

        var agent = new ScriptedAgent();
        // Only land in library — ChooseLibraryPickAsync falls to first match;
        // queue the pay-2-life decline.
        agent.QueueYesNo(false);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

        await fetchEffect.ExecuteAsync(ctx);

        shock.Zone.Should().Be(ZoneType.Battlefield);
        shock.IsTapped.Should().BeTrue(
            "agent declined the optional 2-life payment → shock land enters tapped (CR 614.1c)");
        _alice.LifeTotal.Should().Be(20, "no life paid when the player declines");
    }

    [Fact]
    public async Task Fetch_AgentPays_ShockEntersUntapped_Pays2Life()
    {
        var (shock, fetchEffect) = BuildFetchIntoShock();

        var agent = new ScriptedAgent();
        agent.QueueYesNo(true);
        var ctx = ResolutionContext.For(_alice, agent, game: null, chosenTargets: null);

        await fetchEffect.ExecuteAsync(ctx);

        shock.Zone.Should().Be(ZoneType.Battlefield);
        shock.IsTapped.Should().BeFalse(
            "agent paid 2 life → shock land enters untapped");
        _alice.LifeTotal.Should().Be(18, "the yes path debits 2 life (CR 118.8)");
    }
}
