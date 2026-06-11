using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Api;
using Majik.Core.Api.Commands;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Api.Tests;

/// <summary>
/// Prod-path reproduction for the Dredger's Insight mill-then-pick fidelity
/// bug (the <c>mill_then_pick_first_matching_to_hand</c> effect type).
///
/// Dredger's Insight reads "mill four cards. You MAY put an artifact,
/// creature, or land card from among the milled cards into your hand."
/// (CR 116.1b — a player choice). The card is built through the real
/// production factory path (<see cref="NamedCardFactory.Create"/> →
/// <c>DredgersInsightFactory</c> → <c>CardDefRuntime.BuildMillThenPickEffect</c>)
/// and its ETB effect resolved with the live-match <see cref="RemoteAgent"/>.
///
/// The bug: the engine milled four cards then SILENTLY auto-picked the first
/// matching card into hand with no prompt — the player could not choose which
/// card (or decline). These tests assert the agent is genuinely prompted with
/// the reveal-and-choose prompt (<see cref="ChooseFromRevealedCommand"/>) and
/// that the player's submitted choice — including the "decline" branch — is
/// honoured.
/// </summary>
public class MillThenPickInteractiveProdPathTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Enchantment BuildDredgersInsight(Player owner) =>
        (Enchantment)NamedCardFactory.Create("Dredger's Insight", owner);

    private static TriggeredAbility EtbTrigger(Enchantment enchant) =>
        enchant.Abilities.OfType<TriggeredAbility>().First();

    [Fact]
    public async Task DredgersInsight_EtbResolve_PromptsAgentWithRevealChoice_PickGoesToHand()
    {
        AgentRegistry.Clear();
        // Top of library: instant (not eligible), creature, land — two
        // eligible cards milled. The agent must be prompted and able to pick
        // a SPECIFIC eligible card (the land — not the first matching one).
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var forest = new Land("Forest") { Owner = _alice };
        var plains = new Land("Plains") { Owner = _alice };
        foreach (var c in new ICard[] { bolt, bear, forest, plains })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var agent = new RemoteAgent(_alice);
        var prompted = false;
        agent.PromptRequested += _ => prompted = true;

        var enchant = BuildDredgersInsight(_alice);
        var ctx = ResolutionContext.For(
            _alice, agent, game: null, chosenTargets: null);

        // Resolve the ETB effect — milling happens, then the effect awaits the
        // agent's reveal-and-choose prompt (it must NOT complete synchronously).
        var task = ResolveEtbAsync(enchant, ctx);
        agent.HasPending.Should().BeTrue(
            "the effect must prompt the controller — not auto-pick silently");
        prompted.Should().BeTrue("a PromptRequested fired for the reveal choice");
        agent.ExpectedCommandKinds.Should().ContainSingle()
            .Which.Should().Be(typeof(ChooseFromRevealedCommand),
                "the reveal-and-choose prompt is reused — no new contract");

        // Player picks the land (the second eligible card, proving real choice).
        agent.Submit(new ChooseFromRevealedCommand(forest.InstanceId) { PlayerId = _alice.Id });
        await task;

        _alice.Zones.Hand.GetCards().Should().Contain(forest,
            "the player's chosen card goes to hand");
        _alice.Zones.Hand.GetCards().Should().NotContain(bear,
            "the unchosen eligible card stays in the graveyard");
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { bolt, bear },
            "the milled-but-unchosen cards remain in the graveyard");
        _alice.Zones.Graveyard.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public async Task DredgersInsight_EtbResolve_AgentDeclines_AllMilledStayInGraveyard()
    {
        AgentRegistry.Clear();
        var bear = new Creature("Bear", "1G", 2, 2) { Owner = _alice };
        var forest = new Land("Forest") { Owner = _alice };
        var sol = new Artifact("Sol Ring", "1") { Owner = _alice };
        var bolt = new Instant("Bolt", "R") { Owner = _alice };
        foreach (var c in new ICard[] { bear, forest, sol, bolt })
        {
            _alice.Zones.Library.AddCard(c);
            c.SetZone(ZoneType.Library);
        }

        var agent = new RemoteAgent(_alice);
        var enchant = BuildDredgersInsight(_alice);
        var ctx = ResolutionContext.For(
            _alice, agent, game: null, chosenTargets: null);

        var task = ResolveEtbAsync(enchant, ctx);
        agent.HasPending.Should().BeTrue();

        // CR 116.1b — decline the "you may" clause.
        agent.Submit(new ChooseFromRevealedCommand(InstanceId: null) { PlayerId = _alice.Id });
        await task;

        _alice.Zones.Hand.GetCards().Should().BeEmpty(
            "the controller declined — nothing goes to hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(
            new ICard[] { bear, forest, sol, bolt },
            "all four milled cards remain in the graveyard when the player declines");
    }

    private static async Task ResolveEtbAsync(Enchantment enchant, ResolutionContext ctx)
    {
        foreach (var effect in EtbTrigger(enchant).Effects)
        {
            await effect.ExecuteAsync(ctx);
        }
    }
}
