using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Enchantment = Majik.Core.Cards.Enchantment;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Honor of the Pure (Magic 2010 / Eldritch Moon, {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "White creatures you control get +1/+1."
///
/// Covers:
///   - Card shape: name, Enchantment type, mana cost {1}{W}.
///   - Color-filtered anthem: +1/+1 to WHITE creatures the controller controls.
///   - Non-white creatures (controller's) are NOT buffed.
///   - Opponent's white creatures are NOT buffed ("you control").
///   - LTB lifts the bonus (IsActive gate).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class HonorOfThePureFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void HonorOfThePure_IsEnchantment_AtCost1W()
    {
        var c = HonorOfThePureFactory.Create(_alice);

        c.Name.Should().Be("Honor of the Pure");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HonorOfThePure_BuffsControllersWhiteCreatures()
    {
        var svc = new ContinuousEffectsService();

        var whiteKnight = MakeCreature("Soldier", _alice, svc, 1, 1, "W");

        var honor = HonorOfThePureFactory.Create(_alice, svc);
        honor.SetZone(ZoneType.Battlefield);
        honor.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(honor);

        whiteKnight.GetPower().Should().Be(2,
            "Honor of the Pure grants +1/+1 to white creatures the controller controls");
        whiteKnight.GetToughness().Should().Be(2);
    }

    [Fact]
    public void HonorOfThePure_DoesNotBuffNonWhiteCreatures()
    {
        var svc = new ContinuousEffectsService();

        var greenBear = MakeCreature("Bear", _alice, svc, 2, 2, "G");

        var honor = HonorOfThePureFactory.Create(_alice, svc);
        honor.SetZone(ZoneType.Battlefield);
        honor.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(honor);

        greenBear.GetPower().Should().Be(2, "Honor of the Pure only buffs WHITE creatures");
        greenBear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void HonorOfThePure_DoesNotBuffOpponentWhiteCreatures()
    {
        var svc = new ContinuousEffectsService();

        var bobWhite = MakeCreature("Bob's Soldier", _bob, svc, 1, 1, "W");

        var honor = HonorOfThePureFactory.Create(_alice, svc);
        honor.SetZone(ZoneType.Battlefield);
        honor.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(honor);

        bobWhite.GetPower().Should().Be(1,
            "Honor of the Pure keys on 'you control' — opponent's white creatures are unaffected");
        bobWhite.GetToughness().Should().Be(1);
    }

    [Fact]
    public void HonorOfThePure_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var whiteKnight = MakeCreature("Soldier", _alice, svc, 1, 1, "W");

        var honor = HonorOfThePureFactory.Create(_alice, svc);
        honor.SetZone(ZoneType.Battlefield);
        honor.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(honor);

        whiteKnight.GetPower().Should().Be(2);

        // Honor of the Pure leaves the battlefield — IsActive gate flips false.
        honor.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(honor);
        _alice.Zones.Graveyard.AddCard(honor);

        whiteKnight.GetPower().Should().Be(1,
            "the anthem's IsActive gates on the source being on the battlefield");
        whiteKnight.GetToughness().Should().Be(1);
    }
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, string manaColorPip)
    {
        // Mana cost pip drives the creature's printed color (CR 105.2a) so
        // GetEffectiveColors() reports it. e.g. "{W}" → white, "{G}" → green.
        var c = new Creature(name, $"{{{manaColorPip}}}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
