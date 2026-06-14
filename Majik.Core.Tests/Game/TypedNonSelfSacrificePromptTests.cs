using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Game;

/// <summary>
/// Pays down the typed-non-self-desert-land-sacrifice-cost seam's last residual:
/// the typed NON-self land sacrifice ("Sacrifice a Desert" — Ramunap Ruins,
/// CR 701.16) is now <b>agent-prompted</b> in the live activation dispatch.
///
/// <para>The cost type (<see cref="SacrificeFilteredCost"/>) and its binder
/// wiring already shipped (#2735); but the cost only implemented
/// <see cref="ICost"/>, so the live dispatch's
/// <see cref="SacrificeCostPrompt.ChooseSacrificesAsync"/> — which prompted only
/// <see cref="IChooseCreatureToSacrificeCost"/> — never offered the controller a
/// choice. With multiple Deserts on the battlefield the cost silently auto-picked
/// the first eligible one (CR 700.6 violated). It now implements
/// <see cref="IChoosePermanentToSacrificeCost"/> and the shared prompt seam asks
/// the controller WHICH eligible permanent to sacrifice.</para>
/// </summary>
public class TypedNonSelfSacrificePromptTests
{
    // Records the candidates offered and returns a fixed pick. Every other
    // prompt surface throws (DelegatingAgent posture) — proving ONLY the
    // sacrifice PickOne prompt fires.
    private sealed class RecordingAgent : DelegatingAgent
    {
        private readonly Permanent _pick;
        public List<object> OfferedCandidates { get; } = new();
        public int ChooseCalls { get; private set; }

        public RecordingAgent(Permanent pick) => _pick = pick;

        public override Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            ChooseCalls++;
            OfferedCandidates.AddRange(req.Candidates);
            return Task.FromResult<IReadOnlyList<object>>(new object[] { _pick });
        }
    }

    private static GameContext Ctx(Player p)
    {
        var stack = new Majik.Core.Stack.Stack(new EventBus());
        return new GameContext(p, new[] { p }, p, 1, StepStateType.PreCombatMain, stack);
    }

    private static Land DesertLand(string name, Player owner)
    {
        var l = new Land(name, subtypes: new[] { CardSubtype.Desert })
        {
            Owner = owner,
            Controller = owner,
        };
        owner.Zones.Battlefield.AddCard(l);
        return l;
    }

    private static (ActivatedAbility ability, SacrificeFilteredCost sac) RamunapAbility(
        Land source, Player controller, IEventBus? bus)
    {
        var sac = SacrificeFilteredCost.ForSubtype(CardSubtype.Desert, bus);
        var ability = new ActivatedAbility(
            source: source,
            controller: controller,
            costs: new ICost[] { sac },
            effects: new IEffect[] { new Effect("noop", () => { }) });
        return (ability, sac);
    }

    [Fact]
    public async Task TypedSac_WithMultipleDeserts_PromptsControllerAndStampsChoice()
    {
        var alice = new Player("Alice", 20);
        var ruins = DesertLand("Ramunap Ruins", alice);
        var sunscorched = DesertLand("Sunscorched Desert", alice);
        var (ability, sac) = RamunapAbility(ruins, alice, bus: null);

        var agent = new RecordingAgent(pick: sunscorched);

        await SacrificeCostPrompt.ChooseSacrificesAsync(alice, ability, agent, Ctx(alice));

        agent.ChooseCalls.Should().Be(1, "two Deserts means the controller must be prompted (CR 700.6)");
        agent.OfferedCandidates.Should().HaveCount(2);
        agent.OfferedCandidates.Should().Contain(new object[] { ruins, sunscorched });
        sac.Target.Should().BeSameAs(sunscorched, "the agent's choice is stamped onto the cost before Pay runs");
    }

    [Fact]
    public async Task TypedSac_WithSingleDesert_DoesNotPrompt_AndStampsTheOnlyChoice()
    {
        var alice = new Player("Alice", 20);
        var ruins = DesertLand("Ramunap Ruins", alice);
        var (ability, sac) = RamunapAbility(ruins, alice, bus: null);

        var agent = new RecordingAgent(pick: ruins);

        await SacrificeCostPrompt.ChooseSacrificesAsync(alice, ability, agent, Ctx(alice));

        agent.ChooseCalls.Should().Be(0, "a single legal Desert needs no prompt");
        sac.Target.Should().BeSameAs(ruins, "the only eligible permanent is stamped automatically");
    }

    [Fact]
    public async Task TypedSac_ChosenDesert_IsTheOneSacrificedOnPay()
    {
        var (bus, seen) = WiredBus();
        var alice = new Player("Alice", 20);
        var ruins = DesertLand("Ramunap Ruins", alice);
        var sunscorched = DesertLand("Sunscorched Desert", alice);
        var (ability, sac) = RamunapAbility(ruins, alice, bus);

        var agent = new RecordingAgent(pick: sunscorched);
        await SacrificeCostPrompt.ChooseSacrificesAsync(alice, ability, agent, Ctx(alice));

        sac.Pay(alice);

        sunscorched.Zone.Should().Be(ZoneType.Graveyard, "the chosen Desert is the one sacrificed");
        ruins.Zone.Should().Be(ZoneType.Battlefield, "Ramunap Ruins survives — it was not chosen");
        seen.Should().ContainSingle(e => e.SacrificedCard == sunscorched,
            "CR 701.16a — paying the sacrifice cost publishes PermanentSacrificedEvent crediting the payer");
    }

    private static (EventBus bus, List<PermanentSacrificedEvent> seen) WiredBus()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, seen);
    }
}
