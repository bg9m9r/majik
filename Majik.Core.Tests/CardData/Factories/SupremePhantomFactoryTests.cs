using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Supreme Phantom (Core Set 2019, {1}{U}).
///
/// Covers:
///   - Card shape: name, type, Spirit subtype, P/T 1/3, mana cost.
///   - Flying keyword marker.
///   - LordStaticEffect: +1/+1 to OTHER Spirits controller controls.
///   - Self is not buffed (includeSelf: false).
///   - Non-Spirits are not buffed.
///   - Opponent's Spirits are not buffed (allPlayers: false).
///   - LTB lifts the bonus (IsActive gate).
///   - NamedCardFactory dispatch.
/// </summary>
[Trait("Color", "U")]
public class SupremePhantomFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SupremePhantom_IsCreature_Spirit_1_3_AtCost1U()
    {
        var c = SupremePhantomFactory.Create(_alice);

        c.Name.Should().Be("Supreme Phantom");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SupremePhantom_HasFlying()
    {
        var c = SupremePhantomFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void SupremePhantom_BuffsOtherSpiritsControllerControls()
    {
        var svc = new ContinuousEffectsService();

        var otherSpirit = MakeSpirit("Mausoleum Wanderer", _alice, svc, 1, 1);

        var phantom = SupremePhantomFactory.Create(_alice, svc);
        phantom.Zone = ZoneType.Battlefield;
        phantom.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(phantom);

        otherSpirit.GetPower().Should().Be(2,
            "Supreme Phantom grants +1/+1 to other Spirits controller controls");
        otherSpirit.GetToughness().Should().Be(2);
    }

    [Fact]
    public void SupremePhantom_DoesNotBuffSelf()
    {
        var svc = new ContinuousEffectsService();
        var phantom = SupremePhantomFactory.Create(_alice, svc);
        phantom.Zone = ZoneType.Battlefield;
        phantom.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(phantom);

        phantom.GetPower().Should().Be(1, "Other Spirits — Supreme Phantom does not buff itself");
        phantom.GetToughness().Should().Be(3);
    }

    [Fact]
    public void SupremePhantom_DoesNotBuffNonSpirits()
    {
        var svc = new ContinuousEffectsService();

        var human = MakeCreature("Doomed Traveler", _alice, svc, 1, 1, CardSubtype.Human);

        var phantom = SupremePhantomFactory.Create(_alice, svc);
        phantom.Zone = ZoneType.Battlefield;
        phantom.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(phantom);

        human.GetPower().Should().Be(1);
        human.GetToughness().Should().Be(1);
    }

    [Fact]
    public void SupremePhantom_DoesNotBuffOpponentSpirits()
    {
        var svc = new ContinuousEffectsService();

        var bobSpirit = MakeSpirit("Drogskol Captain (Bob's)", _bob, svc, 2, 2);

        var phantom = SupremePhantomFactory.Create(_alice, svc);
        phantom.Zone = ZoneType.Battlefield;
        phantom.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(phantom);

        bobSpirit.GetPower().Should().Be(2,
            "Supreme Phantom keys on 'you control' — opponent's Spirits are unaffected");
        bobSpirit.GetToughness().Should().Be(2);
    }

    [Fact]
    public void SupremePhantom_LeavingBattlefield_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();
        var otherSpirit = MakeSpirit("Mausoleum Wanderer", _alice, svc, 1, 1);

        var phantom = SupremePhantomFactory.Create(_alice, svc);
        phantom.Zone = ZoneType.Battlefield;
        phantom.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(phantom);

        otherSpirit.GetPower().Should().Be(2);

        // Phantom dies — IsActive gate flips false.
        phantom.Zone = ZoneType.Graveyard;
        _alice.Zones.Battlefield.RemoveCard(phantom);
        _alice.Zones.Graveyard.AddCard(phantom);

        otherSpirit.GetPower().Should().Be(1, "LordStaticEffect.IsActive gates on source being on battlefield");
        otherSpirit.GetToughness().Should().Be(1);
    }
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static Creature MakeSpirit(string name, Player owner,
        ContinuousEffectsService svc, int p, int t)
        => MakeCreature(name, owner, svc, p, t, CardSubtype.Spirit);

    private static Creature MakeCreature(string name, Player owner,
        ContinuousEffectsService svc, int p, int t, CardSubtype subtype)
    {
        var c = new Creature(name, "{1}", p, t, subtypes: new[] { subtype });
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        c.ActiveEffects = svc;
        return c;
    }
}
