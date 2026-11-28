using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
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
/// CR 117.1 / 701.16a — "Sacrifice an artifact" as a cost (Arcbound Ravager,
/// Phyrexia's Core): the player paying the
/// cost chooses which artifact. The activation asks through the shared
/// sacrifice prompt; the chosen artifact is the one sacrificed.
/// </summary>
public class SacrificeAnArtifactChoiceTests
{
    private sealed class PickingAgent : DelegatingAgent
    {
        private readonly Func<IReadOnlyList<object>, object> _pick;
        public List<object> Offered { get; } = new();
        public int Calls { get; private set; }
        public PickingAgent(Func<IReadOnlyList<object>, object> pick) => _pick = pick;

        public override Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            Calls++;
            Offered.AddRange(req.Candidates);
            return Task.FromResult<IReadOnlyList<object>>(new[] { _pick(req.Candidates) });
        }
    }

    private readonly Player _alice = new("Alice", 20);

    private static GameContext Ctx(Player p) =>
        new(p, new[] { p }, p, 1, StepStateType.PreCombatMain, new Majik.Core.Stack.Stack(new EventBus()));

    private T OnBattlefield<T>(T p) where T : Permanent
    {
        p.SetOwner(_alice); p.SetController(_alice);
        p.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(p);
        return p;
    }

    private (ActivatedAbility Ability, SacrificeAnArtifactCost Cost, Artifact Ravager, Artifact Plating, Artifact Bauble) Ravager()
    {
        var ravager = OnBattlefield(new Artifact("Arcbound Ravager", "{2}"));
        var plating = OnBattlefield(new Artifact("Cranial Plating", "{2}"));
        var bauble = OnBattlefield(new Artifact("Mishra's Bauble", "{0}"));
        var cost = new SacrificeAnArtifactCost();
        var ability = new ActivatedAbility(source: ravager, controller: _alice, costs: new ICost[] { cost },
            effects: new IEffect[] { new Effect("noop", () => { }) });
        return (ability, cost, ravager, plating, bauble);
    }

    [Fact]
    public async Task TheController_ChoosesWhichArtifactToSacrifice()
    {
        var (ability, cost, ravager, plating, bauble) = Ravager();
        var agent = new PickingAgent(c => c.Single(o => ReferenceEquals(o, plating)));

        await SacrificeCostPrompt.ChooseSacrificesAsync(_alice, ability, agent, Ctx(_alice));
        cost.Pay(_alice);

        agent.Calls.Should().Be(1);
        agent.Offered.Should().BeEquivalentTo(new object[] { ravager, plating, bauble });
        plating.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact is the one sacrificed");
        ravager.Zone.Should().Be(ZoneType.Battlefield);
        bauble.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task TheBot_SacrificesTheLeastValuableArtifact_NotTheSource()
    {
        // The bot takes the first candidate; the prompt lists the least
        // valuable first and the ability's own source last.
        var (ability, cost, ravager, _, bauble) = Ravager();

        await SacrificeCostPrompt.ChooseSacrificesAsync(_alice, ability, new HeuristicBotAgent(), Ctx(_alice));
        cost.Pay(_alice);

        bauble.Zone.Should().Be(ZoneType.Graveyard);
        ravager.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public async Task TheSource_IsSacrificed_WhenItIsTheOnlyArtifact()
    {
        var ravager = OnBattlefield(new Artifact("Arcbound Ravager", "{2}"));
        var cost = new SacrificeAnArtifactCost();
        var ability = new ActivatedAbility(source: ravager, controller: _alice, costs: new ICost[] { cost },
            effects: new IEffect[] { new Effect("noop", () => { }) });

        await SacrificeCostPrompt.ChooseSacrificesAsync(_alice, ability, new HeuristicBotAgent(), Ctx(_alice));
        cost.Pay(_alice);

        ravager.Zone.Should().Be(ZoneType.Graveyard);
    }
}
