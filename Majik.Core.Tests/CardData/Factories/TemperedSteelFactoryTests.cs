using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Tempered Steel (Scars of Mirrodin, {1}{W}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Artifact creatures you control get +2/+2."
///
/// Covers:
///   - Card identity: name, Enchantment type, mana cost {1}{W}{W}.
///   - Artifact-creature anthem: +2/+2 to ARTIFACT creatures the controller controls.
///   - Non-artifact creatures (controller's) are NOT buffed.
///   - Opponent's artifact creatures are NOT buffed ("you control").
///   - LTB lifts the bonus (IsActive gate).
/// (Dispatch + well-formedness are covered automatically by CardFactoryContractTests.)
/// </summary>
[Trait("Color", "W")]
public class TemperedSteelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TemperedSteel_IsEnchantment_AtCost1WW()
    {
        var c = TemperedSteelFactory.Create(_alice);

        c.Name.Should().Be("Tempered Steel");
        c.ManaCost.Should().Be("{1}{W}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void TemperedSteel_BuffsControllersArtifactCreatures()
    {
        var svc = new ContinuousEffectsService();

        var golem = MakeArtifactCreature("Golem", _alice, svc, 2, 2);

        var steel = TemperedSteelFactory.Create(_alice, svc);
        steel.SetZone(ZoneType.Battlefield);
        steel.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(steel);

        golem.GetPower().Should().Be(4,
            "Tempered Steel grants +2/+2 to artifact creatures the controller controls");
        golem.GetToughness().Should().Be(4);
    }

    [Fact]
    public void TemperedSteel_DoesNotBuffNonArtifactCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Bear", _alice, svc, 2, 2);

        var steel = TemperedSteelFactory.Create(_alice, svc);
        steel.SetZone(ZoneType.Battlefield);
        steel.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(steel);

        bear.GetPower().Should().Be(2, "Tempered Steel only buffs ARTIFACT creatures");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void TemperedSteel_DoesNotBuffOpponentArtifactCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobGolem = MakeArtifactCreature("Bob's Golem", _bob, svc, 2, 2);

        var steel = TemperedSteelFactory.Create(_alice, svc);
        steel.SetZone(ZoneType.Battlefield);
        steel.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(steel);

        bobGolem.GetPower().Should().Be(2,
            "Tempered Steel keys on 'you control' — opponent's artifact creatures are unaffected");
        bobGolem.GetToughness().Should().Be(2);
    }

    [Fact]
    public void TemperedSteel_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var golem = MakeArtifactCreature("Golem", _alice, svc, 2, 2);

        var steel = TemperedSteelFactory.Create(_alice, svc);
        steel.SetZone(ZoneType.Battlefield);
        steel.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(steel);

        golem.GetPower().Should().Be(4);

        // Tempered Steel leaves the battlefield — IsActive gate flips false.
        steel.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(steel);
        _alice.Zones.Graveyard.AddCard(steel);

        golem.GetPower().Should().Be(2,
            "the anthem's IsActive gates on the source being on the battlefield");
        golem.GetToughness().Should().Be(2);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t)
    {
        var c = new Creature(name, "{2}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }

    private static Creature MakeArtifactCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t)
    {
        var c = MakeCreature(name, owner, svc, p, t);
        // CR 301 — additively stamp the Artifact type on the Creature shell
        // (mirrors Master of Etherium / Steel Overseer).
        c.AddCardType(CardType.Artifact);
        return c;
    }
}
