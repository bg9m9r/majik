using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Definitions;

/// <summary>
/// PLAN 01 (Slice F) — end-to-end coverage that the three LIVE production JSON
/// cards whose targeted effects used to be no-op <c>*Stub</c> closures now
/// actually apply their effect to a CHOSEN target, threaded through the unified
/// <see cref="Majik.Core.Targeting.TargetCollection"/> pipeline:
///
/// <list type="bullet">
///   <item>Walking Ballista — "Remove a +1/+1 counter: deal 1 damage to any
///   target" (<c>deal_damage</c>).</item>
///   <item>Boseiju, Who Endures — "Destroy target artifact / enchantment /
///   nonbasic land" (<c>destroy_target</c>).</item>
///   <item>Minamo / Voltaic Key — "Untap target …" (<c>untap_target</c>).</item>
/// </list>
///
/// Each test drives the prod path: the named factory builds the runtime card,
/// the ability declares its <see cref="ActivatedAbility.TargetRequests"/>, the
/// shared <see cref="AbilityActivationFlow"/> collects a scripted agent's pick
/// onto <see cref="ActivatedAbility.ChosenTargets"/>, and
/// <see cref="ActivatedAbility.ResolveAsync"/> applies the effect to THAT
/// target — proving it is no longer a no-op. Illegal-target cases (CR 608.2b)
/// fizzle cleanly.
/// </summary>
public class JsonTargetingEffectsTests
{
    private readonly EventBus _bus = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private GameContext NewContext(Majik.Core.Stack.Stack stack) =>
        new(_alice, new[] { _alice, _bob }, _alice, 1, PhaseStateType.PreCombatMain, stack);

    private static Creature OnBattlefield(Creature creature, Player owner)
    {
        creature.SetOwner(owner);
        creature.SetController(owner);
        owner.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        return creature;
    }

    private static T OnBattlefield<T>(T permanent, Player owner) where T : Permanent
    {
        permanent.SetOwner(owner);
        permanent.SetController(owner);
        owner.Zones.Battlefield.AddCard(permanent);
        permanent.SetZone(ZoneType.Battlefield);
        return permanent;
    }

    /// <summary>Activate <paramref name="ability"/> through the prod flow with
    /// the scripted <paramref name="chosen"/> target, then resolve it.</summary>
    private async Task ActivateAndResolve(ActivatedAbility ability, object? chosen)
    {
        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);
        var ctx = NewContext(stack);

        var agent = new ScriptedAgent();
        if (chosen != null)
        {
            agent.QueueTargets(new[] { chosen });
        }
        else
        {
            agent.QueueTargets(System.Array.Empty<object>());
        }

        await flow.ActivateAsync(
            _alice, ability,
            targetRequests: ability.TargetRequests,
            cost: null,
            agent: agent,
            ctx: ctx);

        await ability.ResolveAsync(agent, ctx);
    }

    // ------------------------------------------------------------------
    // Walking Ballista — deal_damage to any target.
    // ------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_DamageAbility_DeclaresAnyTargetRequest()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var pingAbility = ballista.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.RemovePlusOnePlusOneCounterCost>().Any());

        pingAbility.TargetRequests.Should().HaveCount(1);
        pingAbility.TargetRequests[0].MinTargets.Should().Be(1);
        pingAbility.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task WalkingBallista_DealsDamage_ToChosenCreature()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var pingAbility = ballista.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.RemovePlusOnePlusOneCounterCost>().Any());

        // Two creatures on the battlefield — only the chosen one takes damage.
        var victim = OnBattlefield(new Creature("Victim", "{G}", 2, 2), _bob);
        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        await ActivateAndResolve(pingAbility, victim);

        victim.Damage.Should().Be(1, "the agent chose the victim as the damage target");
        bystander.Damage.Should().Be(0, "damage must hit ONLY the chosen target, not a no-op spray");
    }

    [Fact]
    public async Task WalkingBallista_DealsDamage_ToChosenPlayer()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var pingAbility = ballista.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.RemovePlusOnePlusOneCounterCost>().Any());

        await ActivateAndResolve(pingAbility, _bob);

        _bob.LifeTotal.Should().Be(19, "1 damage to a player is 1 life lost (CR 119.3)");
    }

    [Fact]
    public async Task WalkingBallista_NoTargetChosen_FizzlesCleanly()
    {
        var ballista = WalkingBallistaFactory.Create(_alice);
        var pingAbility = ballista.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<Majik.Core.Costs.RemovePlusOnePlusOneCounterCost>().Any());

        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        // CR 608.2b — no legal target supplied → the damage half fizzles.
        await ActivateAndResolve(pingAbility, chosen: null);

        bystander.Damage.Should().Be(0);
        _bob.LifeTotal.Should().Be(20);
    }

    // ------------------------------------------------------------------
    // Boseiju — destroy_target.
    // ------------------------------------------------------------------

    [Fact]
    public void Boseiju_DestroyAbility_DeclaresTargetRequest()
    {
        var boseiju = BoseijuFactory.Create(_alice);
        var destroy = boseiju.Abilities.OfType<ActivatedAbility>().Single();

        destroy.TargetRequests.Should().HaveCount(1);
        destroy.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task Boseiju_DestroysChosenArtifact_NotABystander()
    {
        var boseiju = BoseijuFactory.Create(_alice);
        var destroy = boseiju.Abilities.OfType<ActivatedAbility>().Single();

        var victim = OnBattlefield(new Artifact("Doomed Relic", "{2}"), _bob);
        var bystander = OnBattlefield(new Artifact("Safe Relic", "{2}"), _bob);

        await ActivateAndResolve(destroy, victim);

        victim.Zone.Should().Be(ZoneType.Graveyard, "the chosen artifact is destroyed");
        _bob.Zones.Graveyard.GetCards().Should().Contain(victim);
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is destroyed");
    }

    [Fact]
    public async Task Boseiju_DestroysChosenNonbasicLand()
    {
        var boseiju = BoseijuFactory.Create(_alice);
        var destroy = boseiju.Abilities.OfType<ActivatedAbility>().Single();

        // A nonbasic land is a legal target for the artifact/enchantment/
        // nonbasic-land filter.
        var nonbasic = OnBattlefield(new Land("Mishra's Factory", null, null), _bob);

        await ActivateAndResolve(destroy, nonbasic);

        nonbasic.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public async Task Boseiju_NoTarget_FizzlesCleanly()
    {
        var boseiju = BoseijuFactory.Create(_alice);
        var destroy = boseiju.Abilities.OfType<ActivatedAbility>().Single();

        var bystander = OnBattlefield(new Artifact("Safe Relic", "{2}"), _bob);

        await ActivateAndResolve(destroy, chosen: null);

        bystander.Zone.Should().Be(ZoneType.Battlefield, "nothing is destroyed when no target was chosen");
    }

    // ------------------------------------------------------------------
    // Minamo / Voltaic Key — untap_target.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Minamo_UntapsChosenLegendaryPermanent()
    {
        var minamo = MinamoSchoolAtWatersEdgeFactory.Create(_alice);
        var untap = minamo.Abilities.OfType<ActivatedAbility>().Single();

        untap.TargetRequests.Should().HaveCount(1);

        // A tapped legendary creature — the legal untap target.
        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }),
            _alice);
        legend.Tap();
        legend.IsTapped.Should().BeTrue();

        await ActivateAndResolve(untap, legend);

        legend.IsTapped.Should().BeFalse("the chosen legendary permanent is untapped (CR 701.21)");
    }

    [Fact]
    public async Task VoltaicKey_UntapsChosenArtifact()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var untap = key.Abilities.OfType<ActivatedAbility>().Single();

        untap.TargetRequests.Should().HaveCount(1);

        var artifact = OnBattlefield(new Artifact("Mana Rock", "{2}"), _alice);
        artifact.Tap();
        artifact.IsTapped.Should().BeTrue();

        await ActivateAndResolve(untap, artifact);

        artifact.IsTapped.Should().BeFalse("the chosen artifact is untapped");
    }

    [Fact]
    public async Task VoltaicKey_NoTarget_FizzlesCleanly()
    {
        var key = VoltaicKeyFactory.Create(_alice);
        var untap = key.Abilities.OfType<ActivatedAbility>().Single();

        var artifact = OnBattlefield(new Artifact("Mana Rock", "{2}"), _alice);
        artifact.Tap();

        await ActivateAndResolve(untap, chosen: null);

        artifact.IsTapped.Should().BeTrue("no target chosen → the untap fizzles, leaving it tapped");
    }

    // ------------------------------------------------------------------
    // TargetFilters — the TargetFilter string → CandidateGatherer translation
    // that drives which objects the agent is offered (CR 608.2b legality).
    // ------------------------------------------------------------------

    [Fact]
    public void AnyTargetFilter_GathersCreaturesAndPlayers()
    {
        var creature = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _bob);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("any", "deal 1 damage");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(creature);
        candidates.Should().Contain(_alice);
        candidates.Should().Contain(_bob);
    }

    [Fact]
    public void NonbasicLandFilter_ExcludesBasicLands()
    {
        var nonbasic = OnBattlefield(new Land("Mishra's Factory", null, null), _bob);
        var basic = OnBattlefield(new Land("Forest", new[] { CardSupertype.Basic }, null), _bob);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("artifact_enchantment_nonbasic_land", "destroy");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(nonbasic);
        candidates.Should().NotContain(basic, "basic lands are not legal destroy targets here");
        candidates.Should().NotContain(_alice, "a destroy filter offers no players");
    }

    [Fact]
    public void LegendaryPermanentFilter_OnlyOffersLegendaries()
    {
        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }), _alice);
        var plain = OnBattlefield(new Creature("Plain Bear", "{1}{G}", 2, 2), _alice);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("legendary_permanent", "untap");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(legend);
        candidates.Should().NotContain(plain, "non-legendary permanents are not legal untap targets");
    }
}
