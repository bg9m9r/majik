using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Abilities;

/// <summary>
/// CR 302.6 / 605.3a — a creature's activated ability whose cost includes the
/// tap ({T}) or untap ({Q}) symbol can't be activated unless its controller
/// has controlled it continuously since their most recent turn began (i.e. no
/// summoning sickness), OR the creature has haste. Mana abilities are NOT
/// exempt (CR 605.3a — they're activated abilities for this purpose).
///
/// These tests cover BOTH activation paths:
///   - the mana-ability path (<see cref="ManaAbility.CanActivate"/>), and
///   - the regular activated-ability path (<see cref="AdditionalCost"/>'s
///     {T} tap cost, the choke point every {T} activated ability pays through).
///
/// Regression guards confirm the gate does NOT touch:
///   - no-tap mana abilities (Wall of Roots shape), and
///   - lands (CR 302.6 is creature-only).
/// </summary>
public class SummoningSicknessTapAbilityTests
{
    private readonly Player _alice = new("Alice", 20);

    private static void GiveHaste(Creature c)
        => c.AddAbility(new KeywordAbility("Haste", source: c, controller: c.Controller));

    // ------------------------------------------------------------------
    // Mana-ability path ({T}: Add {G})
    // ------------------------------------------------------------------

    [Fact]
    public void ManaAbility_SummoningSickCreature_CannotActivate()
    {
        var elves = LlanowarElvesFactory.Create(_alice);
        elves.SetZone(ZoneType.Battlefield);
        elves.HasSummoningSickness.Should().BeTrue("a creature enters with summoning sickness (CR 302.1).");

        var mana = elves.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeFalse(
            "CR 302.6 — a summoning-sick creature without haste can't pay {T}.");
    }

    [Fact]
    public void ManaAbility_SummoningSickCreatureWithHaste_CanActivate()
    {
        var elves = LlanowarElvesFactory.Create(_alice);
        elves.SetZone(ZoneType.Battlefield);
        GiveHaste(elves);

        var mana = elves.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue(
            "CR 302.6 / 702.10 — haste lets a summoning-sick creature pay {T}.");
    }

    [Fact]
    public void ManaAbility_AfterSummoningSicknessClears_CanActivate()
    {
        var elves = LlanowarElvesFactory.Create(_alice);
        elves.SetZone(ZoneType.Battlefield);
        elves.ClearSummoningSickness();

        var mana = elves.Abilities.OfType<ManaAbility>().Single();

        mana.CanActivate().Should().BeTrue(
            "once summoning sickness clears the {T} mana ability is legal.");
    }

    [Fact]
    public void ManaAbility_NoTapCost_SummoningSickCreature_StillLegal()
    {
        // Wall of Roots shape: "Put a -0/-1 counter on this: Add {G}." The
        // activation cost does NOT include {T}, so CR 302.6 never applies —
        // it can be used the turn it enters.
        var wall = new Creature("Wall of Roots", "{1}{G}", 0, 5);
        wall.SetController(_alice);
        wall.SetOwner(_alice);
        wall.SetZone(ZoneType.Battlefield);
        wall.HasSummoningSickness.Should().BeTrue();

        var noTap = new ManaAbility(
            source: wall,
            controller: _alice,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => true,
            additionalCostPayer: _ => { },
            tapsAsCost: false);

        noTap.CanActivate().Should().BeTrue(
            "no {T} in the cost — CR 302.6 does not gate this ability.");
    }

    [Fact]
    public void ManaAbility_LandTapForMana_Unaffected()
    {
        // CR 302.6 is creature-only. A summoning-sick land taps for mana
        // the turn it enters.
        var land = new Land("Forest");
        land.SetController(_alice);
        land.SetOwner(_alice);
        land.SetZone(ZoneType.Battlefield);
        land.HasSummoningSickness.Should().BeTrue();

        var mana = new ManaAbility(
            source: land,
            controller: _alice,
            manaGenerated: ManaCost.Parse("{G}"),
            canActivateCheck: () => !land.IsTapped);

        mana.CanActivate().Should().BeTrue(
            "CR 302.6 gates creatures only — lands tap for mana the turn they enter.");
    }

    // ------------------------------------------------------------------
    // Regular activated-ability path ({T} non-mana cost via AdditionalCost)
    // ------------------------------------------------------------------

    [Fact]
    public void TapCost_SummoningSickCreature_CannotPay()
    {
        var creature = new Creature("Steel Overseer", "{2}", 1, 1);
        creature.SetController(_alice);
        creature.SetOwner(_alice);
        creature.SetZone(ZoneType.Battlefield);
        creature.HasSummoningSickness.Should().BeTrue();

        var tap = AdditionalCost.Tap(creature);

        tap.CanPay(_alice).Should().BeFalse(
            "CR 302.6 — a {T} activated ability can't be activated while summoning sick.");
    }

    [Fact]
    public void TapCost_SummoningSickCreatureWithHaste_CanPay()
    {
        var creature = new Creature("Steel Overseer", "{2}", 1, 1);
        creature.SetController(_alice);
        creature.SetOwner(_alice);
        creature.SetZone(ZoneType.Battlefield);
        GiveHaste(creature);

        var tap = AdditionalCost.Tap(creature);

        tap.CanPay(_alice).Should().BeTrue(
            "CR 302.6 / 702.10 — haste lets the {T} cost be paid the turn it enters.");
    }

    [Fact]
    public void TapCost_AfterSummoningSicknessClears_CanPay()
    {
        var creature = new Creature("Steel Overseer", "{2}", 1, 1);
        creature.SetController(_alice);
        creature.SetOwner(_alice);
        creature.SetZone(ZoneType.Battlefield);
        creature.ClearSummoningSickness();

        var tap = AdditionalCost.Tap(creature);

        tap.CanPay(_alice).Should().BeTrue(
            "once summoning sickness clears the {T} cost is payable.");
    }

    [Fact]
    public void TapCost_LandTap_Unaffected()
    {
        // CR 302.6 is creature-only — a fetch land taps the turn it enters.
        var land = new Land("Wasteland");
        land.SetController(_alice);
        land.SetOwner(_alice);
        land.SetZone(ZoneType.Battlefield);
        land.HasSummoningSickness.Should().BeTrue();

        var tap = AdditionalCost.Tap(land);

        tap.CanPay(_alice).Should().BeTrue(
            "CR 302.6 gates creatures only — a {T} land ability works the turn it enters.");
    }
}
