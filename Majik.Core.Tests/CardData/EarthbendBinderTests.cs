using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Behavioural verification of the Earthbend activated-ability wiring in
/// <see cref="LandActivatedAbilityBinder"/> — the production path for Ba Sing
/// Se's "{2}{G}, {T}: Earthbend 2. Activate only as a sorcery." (CR 701.59).
///
/// Lands are NEVER routed through their [CardName] factory in prod (the factory
/// instance-swap is gated on !shell.HasType(Land)), so the binder is the only
/// live path. This pays down the v1 deferral
/// ba-sing-se-earthbend-target-land-animate: Earthbend targets ANOTHER land you
/// control rather than animating the source land, so the manland AnimateLine
/// ("this land becomes …") never matched it — the Earthbend KEYWORD action
/// (<see cref="Majik.Core.Keywords.EarthbendAction"/>) was already a built
/// primitive; this binds Ba Sing Se's activated ability to it via a
/// TargetRequest over "target land you control".
/// </summary>
public class EarthbendBinderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EmbeddedCardRepository _repo = new();

    private Land MakeBaSingSe(ContinuousEffectsService effects)
    {
        var entity = _repo.GetByName("Ba Sing Se");
        entity.Should().NotBeNull("Ba Sing Se should exist in the embedded pool");
        var parsed = TypeLineParser.Parse(entity!.TypeLine);
        var land = new Land("Ba Sing Se", parsed.Supertypes, parsed.Subtypes);
        land.SetOwner(_alice);
        land.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        LandActivatedAbilityBinder.Bind(land, entity, _alice, effects);
        return land;
    }

    private Land AddPlainLand(Player p, ContinuousEffectsService effects)
    {
        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        land.SetOwner(p);
        land.SetController(p);
        land.ActiveEffects = effects;
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        return land;
    }

    private static GameContext Ctx(Player active, params Player[] players)
        => new(active, players, active, 1,
            Majik.Core.StateMachine.StepStateType.PreCombatMain,
            new Majik.Core.Stack.Stack());

    private static ActivatedAbility EarthbendAbility(Land land)
        => land.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0
                      && a.Effects.Any(e => e.Description.Contains("Earthbend", System.StringComparison.OrdinalIgnoreCase)));

    // -----------------------------------------------------------------------
    // Binding shape — cost, sorcery speed, target request
    // -----------------------------------------------------------------------

    [Fact]
    public void BaSingSe_BindsEarthbendActivatedAbility_SorcerySpeed_TargetLandYouControl()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeBaSingSe(effects);

        var ability = EarthbendAbility(land);

        // CR 117.1a / 307.5 — "Activate only as a sorcery."
        ability.IsSorcerySpeed.Should().BeTrue();

        // {2}{G}, {T} — a ManaCostCost + a Tap cost.
        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle();

        var request = ability.TargetRequests.Single();
        request.MinTargets.Should().Be(1);
        request.MaxTargets.Should().Be(1);
    }

    [Fact]
    public void EarthbendGatherer_OffersOnlyLandsYouControl()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeBaSingSe(effects);
        var myForest = AddPlainLand(_alice, effects);
        var oppForest = AddPlainLand(_bob, effects);

        var request = EarthbendAbility(land).TargetRequests.Single();
        var candidates = request.ResolveCandidates(Ctx(_alice, _alice, _bob));

        candidates.Should().Contain(new object[] { land, myForest },
            "Ba Sing Se itself and the controller's other land are both legal — 'target land you control'");
        candidates.Should().NotContain(oppForest,
            "a land an opponent controls is not a legal Earthbend target (CR 701.59 — 'land you control')");
    }

    // -----------------------------------------------------------------------
    // Resolution — animate the chosen land to a 2/2 with haste, still a land
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_AnimatesChosenLandToTwoTwoWithHaste_StillALand()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeBaSingSe(effects);
        var target = AddPlainLand(_alice, effects);

        var ability = EarthbendAbility(land);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.Resolve();

        // CR 701.59a/b — Earthbend 2: 0/0 + two +1/+1 counters = 2/2.
        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);

        var chars = effects.Compute(target);
        chars.Types.Should().Contain(CardType.Creature, "Earthbend grants the Creature type");
        chars.Types.Should().Contain(CardType.Land, "the land is still a land (CR 701.59a)");
        chars.Keywords.Should().Contain("Haste");

        var cc = chars.Should().BeOfType<CreatureCharacteristics>().Subject;
        cc.Power.Should().Be(2, "0/0 base + two +1/+1 counters = 2/2");
        cc.Toughness.Should().Be(2);

        // Step 3 — the return-tapped delayed trigger is attached to the target.
        target.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the 'when it dies or is exiled, return it tapped' delayed trigger");
    }

    [Fact]
    public void Resolve_TargetNotControlled_DoesNothing()
    {
        // CR 608.2b — resolve-time legality recheck: a chosen land that is no
        // longer one the controller controls is an illegal target; Earthbend
        // does nothing to it.
        var effects = new ContinuousEffectsService();
        var land = MakeBaSingSe(effects);
        var oppForest = AddPlainLand(_bob, effects);

        var ability = EarthbendAbility(land);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { oppForest } });
        ability.Resolve();

        oppForest.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0);
        effects.Compute(oppForest).Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void Resolve_CanTargetBaSingSeItself()
    {
        var effects = new ContinuousEffectsService();
        var land = MakeBaSingSe(effects);

        var ability = EarthbendAbility(land);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { land } });
        ability.Resolve();

        land.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        ((CreatureCharacteristics)effects.Compute(land)).Power.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // [CardName] factory — dispatch + IsImplemented + parity with the binder
    // -----------------------------------------------------------------------

    [Fact]
    public void NamedCardFactory_Dispatches_BaSingSe_AsLand()
    {
        var card = NamedCardFactory.Create("Ba Sing Se", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Ba Sing Se");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().ContainSingle("{T}: Add {G}");
    }

    [Fact]
    public void BaSingSe_IsImplemented_InEmbeddedPool()
    {
        var entity = _repo.GetByName("Ba Sing Se");
        entity!.IsImplemented.Should().BeTrue(
            "the [CardName] factory flips IsImplemented at load time");
    }

    [Fact]
    public void Factory_EarthbendAbility_AnimatesChosenLand()
    {
        var effects = new ContinuousEffectsService();
        var land = BaSingSeFactory.Create(_alice, effects);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var target = AddPlainLand(_alice, effects);

        var ability = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.TargetRequests.Count > 0);
        ability.IsSorcerySpeed.Should().BeTrue();
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        ability.Resolve();

        target.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        ((CreatureCharacteristics)effects.Compute(target)).Power.Should().Be(2);
    }
}
