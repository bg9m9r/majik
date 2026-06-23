using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="QuicksandFactory"/> (Mirage utility land and reprints).
/// Land:
///   "{T}: Add {C}.
///    {T}, Sacrifice this land: Target attacking creature without flying gets
///    -1/-2 until end of turn."
///
/// Covers:
/// - Identity (Land, nonbasic, no subtype, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability.
/// - Pump ability cost shape ({T} tap + Sacrifice this land), no mana cost,
///   instant speed, single 1..1 target request.
/// - Candidate gatherer yields only attacking creatures WITHOUT flying
///   (CR 506.2; flyers excluded per the oracle's "without flying").
/// - Resolution registers a -1/-2 <see cref="PumpUntilEndOfTurnEffect"/> on the
///   chosen creature, expiring at end of turn (CR 514.2).
/// - Illegal-target / no-target / off-battlefield paths are no-ops (CR 608.2b).
/// </summary>
[Trait("Color", "C")]
public class QuicksandFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Quicksand_Identity()
    {
        var land = QuicksandFactory.Create(_alice);

        land.Name.Should().Be("Quicksand");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "Quicksand is a plain utility land, not a creature land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Quicksand is a nonbasic land");
        land.Subtypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Abilities — mana + pump shape
    // -----------------------------------------------------------------------

    [Fact]
    public void Quicksand_HasManaAndPumpAbilities()
    {
        var land = QuicksandFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "the {T}, Sacrifice this land pump ability is wired");
    }

    [Fact]
    public void PumpAbility_HasTapAndSacrificeCosts_NoMana_OneTarget()
    {
        var land = QuicksandFactory.Create(_alice);
        var pump = land.Abilities.OfType<ActivatedAbility>().Single();

        // No mana component — the cost is purely {T} + sacrifice.
        pump.Costs.OfType<ManaCostCost>().Should().BeEmpty(
            "Quicksand's pump ability has no mana cost — only {T} + sacrifice");

        // Two AdditionalCosts: Tap + Sacrifice.
        var additional = pump.Costs.OfType<AdditionalCost>().ToList();
        additional.Should().HaveCount(2, "the cost is {T} (tap) + Sacrifice this land");
        additional.Should().Contain(c => c.Description.StartsWith("Tap "),
            "one cost taps this land");
        additional.Should().Contain(c => c.Description.StartsWith("Sacrifice "),
            "one cost sacrifices this land");

        pump.IsSorcerySpeed.Should().BeFalse(
            "the pump ability is instant-speed per oracle");

        pump.TargetRequests.Should().ContainSingle();
        pump.TargetRequests[0].Description.Should().Be("target attacking creature without flying");
        pump.TargetRequests[0].MinTargets.Should().Be(1);
        pump.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Candidate gatherer — attackers only, flyers excluded
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_CandidateGatherer_ExcludesFlyers_KeepsGroundAttackers()
    {
        var ground = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        ground.SetOwner(_bob);
        ground.SetController(_bob);

        var flyer = new Creature("Wind Drake", "{2}{U}", 2, 2);
        flyer.SetOwner(_bob);
        flyer.SetController(_bob);
        flyer.AddAbility(new KeywordAbility("Flying", flyer, _bob));

        var land = QuicksandFactory.Create(
            _alice,
            attackerLookup: () => new[] { ground, flyer });

        var pump = land.Abilities.OfType<ActivatedAbility>().Single();
        var candidates = pump.TargetRequests[0].CandidateGatherer!(null!);

        candidates.Should().ContainSingle(
            "only the non-flying attacker is a legal target (oracle: \"without flying\")");
        candidates.Single().Should().BeSameAs(ground);
    }

    [Fact]
    public void PumpAbility_CandidateGatherer_NoLookup_ReturnsEmpty()
    {
        // Shape-only / dispatcher path — no live combat wired.
        var land = QuicksandFactory.Create(_alice);
        var pump = land.Abilities.OfType<ActivatedAbility>().Single();

        pump.TargetRequests[0].CandidateGatherer!(null!).Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Resolution — -1/-2 until end of turn (CR 514.2)
    // -----------------------------------------------------------------------

    [Fact]
    public void PumpAbility_OnResolution_RegistersMinus1Minus2_UntilEndOfTurn()
    {
        var effects = new ContinuousEffectsService();
        var land = QuicksandFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);

        var pump = land.Abilities.OfType<ActivatedAbility>().Single();
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });
        pump.Resolve();

        var chars = effects.Compute(target);
        chars.Power.Should().Be(1, "2 base power -1 → 1 (-1/-2)");
        chars.Toughness.Should().Be(0, "2 base toughness -2 → 0 (-1/-2)");

        // CR 514.2 — the effect expires at cleanup, reverting the target.
        effects.ExpireEndOfTurn();
        var after = effects.Compute(target);
        after.Power.Should().Be(2);
        after.Toughness.Should().Be(2);
    }

    [Fact]
    public void PumpAbility_NoTarget_NoOp_DoesNotThrow()
    {
        var land = QuicksandFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var pump = land.Abilities.OfType<ActivatedAbility>().Single();
        var resolve = () => pump.Resolve();

        resolve.Should().NotThrow("no chosen target → no-op (CR 608.2b)");
    }

    [Fact]
    public void PumpAbility_NonCreatureTarget_NoOp_DoesNotThrow()
    {
        var land = QuicksandFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var pump = land.Abilities.OfType<ActivatedAbility>().Single();
        // A non-Creature token chosen (illegal at resolution, CR 608.2b).
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { land } });

        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow("a non-Creature target makes the ability no-op");
    }

    [Fact]
    public void PumpAbility_TargetOffBattlefield_NoOp_DoesNotThrow()
    {
        var effects = new ContinuousEffectsService();
        var land = QuicksandFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);

        var target = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = effects,
        };
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Graveyard); // no longer on the battlefield

        var pump = land.Abilities.OfType<ActivatedAbility>().Single();
        pump.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { target } });

        var resolve = () => pump.Resolve();
        resolve.Should().NotThrow("an off-battlefield target makes the ability no-op (CR 608.2b)");

        var chars = effects.Compute(target);
        chars.Power.Should().Be(2, "no pump was registered on an illegal target");
        chars.Toughness.Should().Be(2);
    }
}
