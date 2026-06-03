using Majik.Core.CardData;
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
    // fight (CR 701.12) — source: "self".
    // ------------------------------------------------------------------

    /// <summary>Build a "this creature fights target creature" activated
    /// ability on <paramref name="source"/> from the declarative
    /// <see cref="Majik.Core.CardData.Definitions.FightEffectDef"/> verb.</summary>
    private static ActivatedAbility SelfFightAbility(Creature source, Player controller)
    {
        var def = new Majik.Core.CardData.Definitions.FightEffectDef { Source = "self" };
        var request = def.ToTargetRequest()!;
        var effect = def.ToResolveEffect()(source, controller, null, 0);
        return new ActivatedAbility(
            source: source,
            controller: controller,
            effects: new[] { effect },
            targetRequests: new[] { request });
    }

    [Fact]
    public async Task SelfFight_SourceAndTarget_TakeEachOthersPower()
    {
        var mine = OnBattlefield(new Creature("Mine", "{G}", 4, 5), _alice);
        var theirs = OnBattlefield(new Creature("Theirs", "{G}", 2, 3), _bob);

        var fight = SelfFightAbility(mine, _alice);
        fight.TargetRequests.Should().HaveCount(1, "self-source declares one target (the other creature)");

        await ActivateAndResolve(fight, theirs);

        mine.Damage.Should().Be(2, "Theirs has power 2");
        theirs.Damage.Should().Be(4, "Mine has power 4");
    }

    [Fact]
    public async Task SelfFight_DeathtouchSource_MarksTarget()
    {
        var snake = OnBattlefield(new Creature("Snake", "{G}", 1, 1), _alice);
        snake.AddAbility(new KeywordAbility("Deathtouch", snake, _alice));
        var giant = OnBattlefield(new Creature("Giant", "{G}", 0, 8), _bob);

        await ActivateAndResolve(SelfFightAbility(snake, _alice), giant);

        giant.MarkedForDestructionByDeathtouch.Should().BeTrue(
            "deathtouch applies to fight damage (CR 702.2b)");
    }

    [Fact]
    public async Task SelfFight_NoTarget_FizzlesCleanly()
    {
        var mine = OnBattlefield(new Creature("Mine", "{G}", 4, 5), _alice);

        // CR 608.2b / 701.12c — no other creature → the fight does nothing.
        await ActivateAndResolve(SelfFightAbility(mine, _alice), chosen: null);

        mine.Damage.Should().Be(0);
    }

    // ------------------------------------------------------------------
    // Minamo / Voltaic Key — untap_target.
    // ------------------------------------------------------------------

    [Fact]
    public async Task Minamo_UntapsChosenLegendaryPermanent()
    {
        var minamo = (Land)NamedCardFactory.Create("Minamo, School at Water's Edge", _alice);
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
        var key = (Artifact)NamedCardFactory.Create("Voltaic Key", _alice);
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
        var key = (Artifact)NamedCardFactory.Create("Voltaic Key", _alice);
        var untap = key.Abilities.OfType<ActivatedAbility>().Single();

        var artifact = OnBattlefield(new Artifact("Mana Rock", "{2}"), _alice);
        artifact.Tap();

        await ActivateAndResolve(untap, chosen: null);

        artifact.IsTapped.Should().BeTrue("no target chosen → the untap fizzles, leaving it tapped");
    }

    // ------------------------------------------------------------------
    // Karakas — return_to_hand (bounce target legendary creature).
    // ------------------------------------------------------------------

    [Fact]
    public void Karakas_BounceAbility_DeclaresLegendaryCreatureTargetRequest()
    {
        var karakas = (Land)NamedCardFactory.Create("Karakas", _alice);
        var bounce = karakas.Abilities.OfType<ActivatedAbility>().Single();

        bounce.TargetRequests.Should().HaveCount(1);
        bounce.TargetRequests[0].MinTargets.Should().Be(1);
        bounce.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task Karakas_ReturnsChosenLegendaryCreature_ToOwnersHand()
    {
        var karakas = (Land)NamedCardFactory.Create("Karakas", _alice);
        var bounce = karakas.Abilities.OfType<ActivatedAbility>().Single();

        // Bob owns a legendary creature; only the chosen one is bounced.
        var legend = OnBattlefield(
            new Creature("Emrakul", "{15}", 15, 15, new[] { CardSupertype.Legendary }), _bob);
        var bystander = OnBattlefield(
            new Creature("Other Legend", "{2}{G}", 2, 2, new[] { CardSupertype.Legendary }), _bob);

        await ActivateAndResolve(bounce, legend);

        legend.Zone.Should().Be(ZoneType.Hand, "the chosen legendary creature returns to its owner's hand (CR 701.20)");
        _bob.Zones.Hand.GetCards().Should().Contain(legend);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(legend);
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is bounced");
    }

    [Fact]
    public async Task Karakas_NoTarget_FizzlesCleanly()
    {
        var karakas = (Land)NamedCardFactory.Create("Karakas", _alice);
        var bounce = karakas.Abilities.OfType<ActivatedAbility>().Single();

        var legend = OnBattlefield(
            new Creature("Emrakul", "{15}", 15, 15, new[] { CardSupertype.Legendary }), _bob);

        await ActivateAndResolve(bounce, chosen: null);

        legend.Zone.Should().Be(ZoneType.Battlefield, "nothing is bounced when no target was chosen (CR 608.2b)");
    }

    [Fact]
    public void LegendaryCreatureFilter_OnlyOffersLegendaryCreatures()
    {
        var legend = OnBattlefield(
            new Creature("Legendary Bear", "{1}{G}", 2, 2, new[] { CardSupertype.Legendary }), _bob);
        var plain = OnBattlefield(new Creature("Plain Bear", "{1}{G}", 2, 2), _bob);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("legendary_creature", "return to its owner's hand");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(legend);
        candidates.Should().NotContain(plain, "non-legendary creatures are not legal targets for the legendary-creature bounce");
    }

    // ------------------------------------------------------------------
    // Goldmeadow Harrier — tap_target (tap target creature).
    // ------------------------------------------------------------------

    [Fact]
    public void GoldmeadowHarrier_TapAbility_DeclaresCreatureTargetRequest()
    {
        var harrier = (Creature)NamedCardFactory.Create("Goldmeadow Harrier", _alice);
        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();

        tap.TargetRequests.Should().HaveCount(1);
        tap.TargetRequests[0].MinTargets.Should().Be(1);
        tap.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task GoldmeadowHarrier_TapsChosenCreature_NotABystander()
    {
        var harrier = (Creature)NamedCardFactory.Create("Goldmeadow Harrier", _alice);
        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();

        var victim = OnBattlefield(new Creature("Victim", "{G}", 2, 2), _bob);
        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        await ActivateAndResolve(tap, victim);

        victim.IsTapped.Should().BeTrue("the chosen creature is tapped (CR 701.21a)");
        bystander.IsTapped.Should().BeFalse("only the chosen target is tapped, not a no-op spray");
    }

    [Fact]
    public async Task GoldmeadowHarrier_AlreadyTappedTarget_IsANoOp()
    {
        var harrier = (Creature)NamedCardFactory.Create("Goldmeadow Harrier", _alice);
        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();

        var victim = OnBattlefield(new Creature("Victim", "{G}", 2, 2), _bob);
        victim.Tap();

        // CR 701.21b — "taps" with no effect; Permanent.Tap is idempotent.
        await ActivateAndResolve(tap, victim);

        victim.IsTapped.Should().BeTrue();
    }

    [Fact]
    public async Task GoldmeadowHarrier_NoTarget_FizzlesCleanly()
    {
        var harrier = (Creature)NamedCardFactory.Create("Goldmeadow Harrier", _alice);
        var tap = harrier.Abilities.OfType<ActivatedAbility>().Single();

        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        // CR 608.2b — no legal target supplied → the tap fizzles.
        await ActivateAndResolve(tap, chosen: null);

        bystander.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void CreatureFilter_OnlyOffersCreatures()
    {
        var creature = OnBattlefield(new Creature("Bear", "{1}{G}", 2, 2), _bob);
        var artifact = OnBattlefield(new Artifact("Rock", "{2}"), _bob);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("creature", "tap");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(creature);
        candidates.Should().NotContain(artifact, "a tap-target-creature filter offers no artifacts");
        candidates.Should().NotContain((object)_alice, "a creature filter offers no players");
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

    // ------------------------------------------------------------------
    // exile_target — targeted exile of a permanent (ability path).
    // CR 701.21 — Exile. Mirrors destroy_target onto the exile primitive.
    // ------------------------------------------------------------------

    /// <summary>Build a 1-effect activated ability whose single effect is the
    /// given <see cref="Majik.Core.CardData.Definitions.EffectDefinition"/>,
    /// wired through the declarative ability path (the same path the JSON
    /// ToCardDefAbility pairs ToTargetRequest + ToResolveEffect).</summary>
    private ActivatedAbility BuildExileAbility(
        Majik.Core.CardData.Definitions.EffectDefinition effect, ICard source)
    {
        var request = effect.ToTargetRequest();
        var built = effect.ToResolveEffect()(source, _alice, null, request is null ? -1 : 0);
        return new ActivatedAbility(
            source: source,
            controller: _alice,
            costs: System.Array.Empty<Majik.Core.Costs.ICost>(),
            effects: new[] { built },
            targetRequests: request is null
                ? System.Array.Empty<Majik.Core.Players.Agents.TargetRequest>()
                : new[] { request });
    }

    [Fact]
    public async Task ExileTarget_ExilesChosenPermanent_NotABystander()
    {
        var source = OnBattlefield(new Artifact("Exiler", "{2}"), _alice);
        var ability = BuildExileAbility(
            new Majik.Core.CardData.Definitions.ExileTargetEffectDef { TargetFilter = "permanent" },
            source);

        var victim = OnBattlefield(new Creature("Victim", "{G}", 2, 2), _bob);
        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        await ActivateAndResolve(ability, victim);

        victim.Zone.Should().Be(ZoneType.Exile, "the chosen permanent is exiled (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(victim);
        bystander.Zone.Should().Be(ZoneType.Battlefield, "only the chosen target is exiled");
    }

    [Fact]
    public async Task ExileTarget_NonbasicLandFilter_RejectsBasicLandAtResolution()
    {
        var source = OnBattlefield(new Artifact("Exiler", "{2}"), _alice);
        var ability = BuildExileAbility(
            new Majik.Core.CardData.Definitions.ExileTargetEffectDef { TargetFilter = "nonbasic_land" },
            source);

        // A basic land is NOT a legal nonbasic-land target — CR 608.2b re-check
        // at resolution fizzles even when the agent hands it directly.
        var basic = OnBattlefield(new Land("Forest", new[] { CardSupertype.Basic }, null), _bob);

        await ActivateAndResolve(ability, basic);

        basic.Zone.Should().Be(ZoneType.Battlefield,
            "a basic land is not a legal nonbasic-land exile target (CR 608.2b)");
    }

    [Fact]
    public async Task ExileTarget_NoTargetChosen_FizzlesCleanly()
    {
        var source = OnBattlefield(new Artifact("Exiler", "{2}"), _alice);
        var ability = BuildExileAbility(
            new Majik.Core.CardData.Definitions.ExileTargetEffectDef { TargetFilter = "permanent" },
            source);

        var bystander = OnBattlefield(new Creature("Bystander", "{G}", 2, 2), _bob);

        await ActivateAndResolve(ability, chosen: null);

        bystander.Zone.Should().Be(ZoneType.Battlefield, "nothing is exiled when no target was chosen (CR 608.2b)");
    }

    // ------------------------------------------------------------------
    // exile_target — exile target card from a graveyard (Part 2).
    // CR 701.21 — Soul Guide Lantern / Scavenging Ooze / Boggart Trawler
    // piece "exile target card from a graveyard".
    // ------------------------------------------------------------------

    [Fact]
    public async Task ExileTarget_ExilesChosenCardFromGraveyard()
    {
        var source = OnBattlefield(new Artifact("Lantern", "{1}"), _alice);
        var ability = BuildExileAbility(
            new Majik.Core.CardData.Definitions.ExileTargetEffectDef { TargetFilter = "card_in_graveyard" },
            source);

        var inGrave = new Creature("Dead Bear", "{1}{G}", 2, 2);
        inGrave.SetOwner(_bob);
        inGrave.SetController(_bob);
        inGrave.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(inGrave);

        await ActivateAndResolve(ability, inGrave);

        inGrave.Zone.Should().Be(ZoneType.Exile, "the chosen graveyard card is exiled (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(inGrave);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(inGrave);
    }

    [Fact]
    public void GraveyardCardFilter_OnlyOffersCardsInGraveyards()
    {
        var inGrave = new Creature("Dead Bear", "{1}{G}", 2, 2);
        inGrave.SetOwner(_bob);
        inGrave.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(inGrave);
        var onField = OnBattlefield(new Creature("Live Bear", "{1}{G}", 2, 2), _bob);
        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("card_in_graveyard", "exile");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(inGrave);
        candidates.Should().NotContain(onField, "battlefield cards are not in a graveyard");
        candidates.Should().NotContain((object)_alice, "a graveyard-card filter offers no players");
    }

    [Fact]
    public void CreatureCardInGraveyardFilter_OnlyOffersCreatureCards()
    {
        var deadBear = new Creature("Dead Bear", "{1}{G}", 2, 2);
        deadBear.SetOwner(_bob);
        deadBear.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(deadBear);

        var deadBolt = new Instant("Spent Bolt", "{R}");
        deadBolt.SetOwner(_bob);
        deadBolt.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(deadBolt);

        var ctx = NewContext(new Majik.Core.Stack.Stack(_bus));

        var request = Majik.Core.CardData.Definitions.TargetFilters
            .ToTargetRequest("creature_card_in_graveyard", "exile");
        var candidates = request.ResolveCandidates(ctx);

        candidates.Should().Contain(deadBear);
        candidates.Should().NotContain(deadBolt, "a creature-card-in-graveyard filter offers no instants");
    }

    // ------------------------------------------------------------------
    // ABILITY-PATH shared-slot rider (lose_life_target, Subject "controller").
    // Mirrors the SPELL-path Vapor Snag rider, but proves the SAME
    // "Return target creature to its owner's hand. Its controller loses N
    // life." rider works on an ACTIVATED / TRIGGERED ability — i.e. the
    // SharesPreviousTargetSlot wiring is threaded through
    // CardDefAbilityEffects.Materialize, not only the spell bridge.
    // ------------------------------------------------------------------

    /// <summary>Build a live activated ability "{0}: Return target creature to
    /// its owner's hand. Its controller loses 1 life." through the prod
    /// JSON-def → factory path and return its single
    /// <see cref="ActivatedAbility"/>.</summary>
    private ActivatedAbility BuildBounceDrainAbility(int loseAmount = 1)
    {
        var def = new Majik.Core.CardData.Definitions.CardDefinition
        {
            Name = "Vapor Drone",
            Types = new List<string> { "Artifact" },
            ManaCost = "2",
            Abilities = new List<Majik.Core.CardData.Definitions.AbilityDefinition>
            {
                new Majik.Core.CardData.Definitions.ActivatedAbilityDefinition
                {
                    Costs = new List<Majik.Core.CardData.Definitions.CostDefinition>
                    {
                        new Majik.Core.CardData.Definitions.ManaCostDef { Amount = "0" },
                    },
                    Effects = new List<Majik.Core.CardData.Definitions.EffectDefinition>
                    {
                        new Majik.Core.CardData.Definitions.ReturnToHandEffectDef { TargetFilter = "creature" },
                        new Majik.Core.CardData.Definitions.LoseLifeTargetEffectDef
                        {
                            Amount = loseAmount,
                            Subject = "controller",
                        },
                    },
                },
            },
        };

        var card = Majik.Core.CardData.Definitions.CardDefinitionFactory.Build(def, _alice);
        return card.Abilities.OfType<ActivatedAbility>().Single();
    }

    [Fact]
    public void AbilityRider_BounceThenLoseLife_DeclaresExactlyOneTargetSlot()
    {
        var ability = BuildBounceDrainAbility();

        // CR 601.2c — the bounce + "its controller loses N life" rider share a
        // single printed target (the bounced creature). The rider must NOT add
        // its own slot, so the ability declares exactly one TargetRequest.
        ability.TargetRequests.Should().HaveCount(1, "the lose-life rider shares the bounce's target slot");
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    [Fact]
    public async Task AbilityRider_BounceThenLoseLife_BouncesAndDrainsThatControllersLife()
    {
        var ability = BuildBounceDrainAbility(loseAmount: 1);
        var victim = OnBattlefield(new Creature("Bounced Bear", "{1}{G}", 2, 2), _bob);

        await ActivateAndResolve(ability, victim);

        victim.Zone.Should().Be(ZoneType.Hand, "the chosen creature is returned to its owner's hand");
        _bob.Zones.Hand.GetCards().Should().Contain(victim);
        _bob.LifeTotal.Should().Be(19, "its controller loses 1 life (CR 119.3), reading the SHARED bounce slot");
    }

    [Fact]
    public async Task AbilityRider_IllegalTarget_NeitherBounceNorLifeLoss()
    {
        var ability = BuildBounceDrainAbility(loseAmount: 1);

        // CR 608.2b — no legal target supplied: both the bounce and the shared
        // "its controller" rider fizzle cleanly (no life loss off an empty slot).
        await ActivateAndResolve(ability, chosen: null);

        _bob.LifeTotal.Should().Be(20, "no shared target → the rider has no controller to drain");
    }

    [Fact]
    public async Task AbilityRider_TargetLeftBattlefieldBeforeResolution_RiderFizzles()
    {
        // CR 608.2g / 608.2b — the chosen creature left the battlefield (e.g.
        // died in response) BEFORE the ability resolved. The resolution-start
        // snapshot never captured a battlefield controller for the shared slot,
        // so the bounce no-ops AND the "its controller loses N life" rider
        // fizzles — no spurious life loss off a last-known controller.
        var ability = BuildBounceDrainAbility(loseAmount: 1);
        var victim = OnBattlefield(new Creature("Doomed Bear", "{1}{G}", 2, 2), _bob);

        var stack = new Majik.Core.Stack.Stack(_bus);
        var flow = new AbilityActivationFlow(stack, _bus);
        var ctx = NewContext(stack);
        var agent = new ScriptedAgent();
        agent.QueueTargets(new[] { (object)victim });

        await flow.ActivateAsync(
            _alice, ability,
            targetRequests: ability.TargetRequests,
            cost: null, agent: agent, ctx: ctx);

        // The target leaves the battlefield AFTER it was chosen but BEFORE the
        // ability resolves (it's now in the graveyard / hand — no longer legal).
        _bob.Zones.Battlefield.RemoveCard(victim);
        victim.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(victim);

        await ability.ResolveAsync(agent, ctx);

        _bob.LifeTotal.Should().Be(20, "the shared target was illegal at resolution start → the rider fizzles (CR 608.2b)");
    }
}
