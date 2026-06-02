using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Intangible Virtue (Innistrad, {1}{W}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "Creature tokens you control get +1/+1 and have vigilance."
///
/// Covers:
///   - Card shape: name, Enchantment type, mana cost {1}{W}.
///   - Token anthem: +1/+1 to the controller's creature tokens.
///   - Granted vigilance keyword on the controller's tokens.
///   - Non-token creatures (controller's) are NOT buffed and gain nothing.
///   - Opponent's tokens are NOT buffed ("you control").
///   - LTB lifts the bonus + keyword (IsActive gate).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "W")]
public class IntangibleVirtueFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void IntangibleVirtue_IsEnchantment_AtCost1W()
    {
        var c = IntangibleVirtueFactory.Create(_alice);

        c.Name.Should().Be("Intangible Virtue");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IntangibleVirtue_BuffsControllersTokens()
    {
        var svc = new ContinuousEffectsService();

        var token = MakeCreature("Spirit", _alice, svc, 1, 1, isToken: true);

        var virtue = IntangibleVirtueFactory.Create(_alice, svc);
        virtue.SetZone(ZoneType.Battlefield);
        virtue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(virtue);

        token.GetPower().Should().Be(2,
            "Intangible Virtue grants +1/+1 to creature tokens the controller controls");
        token.GetToughness().Should().Be(2);
    }

    [Fact]
    public void IntangibleVirtue_GrantsVigilanceToControllersTokens()
    {
        var svc = new ContinuousEffectsService();

        var token = MakeCreature("Spirit", _alice, svc, 1, 1, isToken: true);

        var virtue = IntangibleVirtueFactory.Create(_alice, svc);
        virtue.SetZone(ZoneType.Battlefield);
        virtue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(virtue);

        CombatAbilities.HasVigilance(token).Should().BeTrue(
            "Intangible Virtue grants vigilance to creature tokens the controller controls");
    }

    [Fact]
    public void IntangibleVirtue_DoesNotBuffNonTokenCreatures()
    {
        var svc = new ContinuousEffectsService();

        var realCreature = MakeCreature("Bear", _alice, svc, 2, 2, isToken: false);

        var virtue = IntangibleVirtueFactory.Create(_alice, svc);
        virtue.SetZone(ZoneType.Battlefield);
        virtue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(virtue);

        realCreature.GetPower().Should().Be(2, "Intangible Virtue only buffs creature TOKENS");
        realCreature.GetToughness().Should().Be(2);
        CombatAbilities.HasVigilance(realCreature).Should().BeFalse(
            "Intangible Virtue grants vigilance only to creature tokens");
    }

    [Fact]
    public void IntangibleVirtue_DoesNotBuffOpponentTokens()
    {
        var svc = new ContinuousEffectsService();

        var bobToken = MakeCreature("Bob's Goblin", _bob, svc, 1, 1, isToken: true);

        var virtue = IntangibleVirtueFactory.Create(_alice, svc);
        virtue.SetZone(ZoneType.Battlefield);
        virtue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(virtue);

        bobToken.GetPower().Should().Be(1,
            "Intangible Virtue keys on 'you control' — opponent's tokens are unaffected");
        bobToken.GetToughness().Should().Be(1);
        CombatAbilities.HasVigilance(bobToken).Should().BeFalse();
    }

    [Fact]
    public void IntangibleVirtue_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var token = MakeCreature("Spirit", _alice, svc, 1, 1, isToken: true);

        var virtue = IntangibleVirtueFactory.Create(_alice, svc);
        virtue.SetZone(ZoneType.Battlefield);
        virtue.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(virtue);

        token.GetPower().Should().Be(2);
        CombatAbilities.HasVigilance(token).Should().BeTrue();

        // Intangible Virtue leaves the battlefield — IsActive gate flips false.
        virtue.SetZone(ZoneType.Graveyard);
        _alice.Zones.Battlefield.RemoveCard(virtue);
        _alice.Zones.Graveyard.AddCard(virtue);

        token.GetPower().Should().Be(1,
            "the anthem's IsActive gates on the source being on the battlefield");
        token.GetToughness().Should().Be(1);
        CombatAbilities.HasVigilance(token).Should().BeFalse(
            "the granted keyword lifts when the source leaves the battlefield");
    }
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, bool isToken)
    {
        var c = new Creature(name, "{W}", p, t);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        if (isToken) c.MarkAsToken();
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
