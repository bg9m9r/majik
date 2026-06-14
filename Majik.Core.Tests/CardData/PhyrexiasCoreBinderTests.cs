using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Tests.Helpers;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the typed-nonself-sacrifice-cost-chooser deferral's last residual:
/// Phyrexia's Core's TYPED non-self sacrifice — <c>"{1}, {T}, Sacrifice an
/// artifact: You gain 1 life"</c> (CR 701.16) — through the <b>production binder
/// chain</b> (<see cref="LandActivatedAbilityBinder"/>).
///
/// <para>Lands are NEVER routed through their <c>[CardName]</c> factory in
/// production (named-factory-vs-binder-chain): <see cref="PhyrexiasCoreFactory"/>
/// + its tests exercise the bespoke <see cref="SacrificeAnArtifactCost"/>, which
/// is dead in real games. The Desert/subtype leg of this seam already had a
/// live-path binder test (Ramunap Ruins, <see cref="LandSacrificeBusTests"/>);
/// the artifact CARD-TYPE leg (Phyrexia's Core) was the one branch the deferral
/// note flagged as still unverified on the live path. These tests close it:</para>
/// <list type="bullet">
///   <item>the binder recognises the artifact typed-sac line and binds a real
///   <see cref="SacrificeFilteredCost"/> (NOT a self-only
///   <see cref="AdditionalCost"/> stub — the land is not an artifact);</item>
///   <item>the source land never qualifies (a Land is not an Artifact);</item>
///   <item>with multiple controlled artifacts the live activation prompt
///   (<see cref="SacrificeCostPrompt.ChooseSacrificesAsync"/>) asks the
///   controller WHICH artifact to sacrifice (CR 700.6), stamps the choice, and
///   paying sacrifices exactly that one + publishes
///   <see cref="PermanentSacrificedEvent"/> (CR 701.16a) for aristocrat
///   payoffs.</item>
/// </list>
/// </summary>
[Trait("Color", "Colorless")]
public class PhyrexiasCoreBinderTests
{
    private static (EventBus bus, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, seen);
    }

    private static SacrificeFilteredCost TypedSacCost(ICard land) =>
        land.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<SacrificeFilteredCost>()
            .First();

    private static Artifact ArtifactCard(string name, Player owner)
    {
        var a = new Artifact(name, "2") { Owner = owner, Controller = owner };
        owner.Zones.Battlefield.AddCard(a);
        a.SetZone(ZoneType.Battlefield);
        return a;
    }

    private static Land BindPhyrexiasCore(Player owner, ContinuousEffectsService effects, IEventBus? bus)
    {
        var land = new Land("Phyrexia's Core") { Owner = owner, Controller = owner };
        var entity = new CardEntity
        {
            Name = "Phyrexia's Core",
            TypeLine = "Land",
            // Exact Scryfall oracle text.
            OracleText = "{T}: Add {C}.\n" +
                         "{1}, {T}, Sacrifice an artifact: You gain 1 life.",
        };
        var bound = LandActivatedAbilityBinder.Bind(land, entity, owner, effects, triggers: null, eventBus: bus);
        bound.Should().BeTrue("the binder recognises the {1}, {T}, Sacrifice an artifact: gain-life ability");
        owner.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    [Fact]
    public void TypedSacrifice_PhyrexiasCore_BindsRealFilteredCost_NotSelfStub()
    {
        var alice = new Player("Alice", 20);
        var (bus, _) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var land = BindPhyrexiasCore(alice, effects, bus);

        var ability = land.Abilities.OfType<ActivatedAbility>().Should().ContainSingle().Subject;
        ability.Costs.OfType<SacrificeFilteredCost>().Should().ContainSingle(
            "\"Sacrifice an artifact\" binds a typed non-self SacrificeFilteredCost (CR 701.16)");
        // It must NOT be modelled as a self-only AdditionalCost.Sacrifice stub —
        // the land is not an artifact, so it can never sacrifice itself.
        ability.Costs.OfType<AdditionalCost>()
            .Where(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().BeEmpty("the typed cost is non-self — no self-sac AdditionalCost is added");
    }

    [Fact]
    public void TypedSacrifice_PhyrexiasCore_LandItself_IsNeverEligible()
    {
        var alice = new Player("Alice", 20);
        var (bus, _) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var land = BindPhyrexiasCore(alice, effects, bus);
        var sac = TypedSacCost(land);

        // No artifact in play — a Land is not an Artifact, so nothing qualifies.
        sac.CanPay(alice).Should().BeFalse("the land is not an artifact; it can never pay its own sac");
        sac.EligiblePermanents(alice).Should().BeEmpty();

        // With an artifact in play the cost becomes payable, and the land is
        // still NOT among the eligible permanents.
        var stone = ArtifactCard("Mind Stone", alice);
        sac.CanPay(alice).Should().BeTrue();
        sac.EligiblePermanents(alice).Should().ContainSingle().Which.Should().BeSameAs(stone);
    }

    [Fact]
    public async Task TypedSacrifice_PhyrexiasCore_PromptsControllerForArtifact_AndSacrificesTheChoice()
    {
        var alice = new Player("Alice", 20);
        var (bus, seen) = Wired();
        var effects = new ContinuousEffectsService(bus);

        var land = BindPhyrexiasCore(alice, effects, bus);

        // Two artifacts Alice controls — the controller must choose (CR 700.6).
        var mindStone = ArtifactCard("Mind Stone", alice);
        var manaVault = ArtifactCard("Mana Vault", alice);

        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        var agent = new RecordingArtifactSacAgent(pick: manaVault);

        await SacrificeCostPrompt.ChooseSacrificesAsync(alice, ability, agent, Ctx(alice));

        agent.ChooseCalls.Should().Be(1, "two eligible artifacts means the controller must be prompted (CR 700.6)");
        agent.OfferedCandidates.Should().HaveCount(2);
        agent.OfferedCandidates.Should().Contain(new object[] { mindStone, manaVault });

        var sac = TypedSacCost(land);
        sac.Target.Should().BeSameAs(manaVault, "the agent's choice is stamped onto the cost before Pay runs");

        sac.Pay(alice);

        manaVault.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact is the one sacrificed");
        mindStone.Zone.Should().Be(ZoneType.Battlefield, "the unchosen artifact survives");
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == manaVault
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    private static GameContext Ctx(Player p)
    {
        var stack = new Majik.Core.Stack.Stack(new EventBus());
        return new GameContext(p, new[] { p }, p, 1, StepStateType.PreCombatMain, stack);
    }

    // Records the candidates offered for the sacrifice PickOne prompt and returns
    // a fixed pick. Every other prompt surface throws (DelegatingAgent posture),
    // proving ONLY the sacrifice prompt fires.
    private sealed class RecordingArtifactSacAgent : DelegatingAgent
    {
        private readonly Permanent _pick;
        public List<object> OfferedCandidates { get; } = new();
        public int ChooseCalls { get; private set; }

        public RecordingArtifactSacAgent(Permanent pick) => _pick = pick;

        public override Task<IReadOnlyList<object>> ChooseAsync(
            GameContext ctx, ChoiceRequest req, CancellationToken ct = default)
        {
            ChooseCalls++;
            OfferedCandidates.AddRange(req.Candidates);
            return Task.FromResult<IReadOnlyList<object>>(new object[] { _pick });
        }
    }
}
