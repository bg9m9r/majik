using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Master of Waves (Theros, {3}{U}).
///
/// Creature — Merfolk Wizard 2/1. Oracle text (verified against Scryfall
/// 2026-06-02):
///   "Protection from red
///    Elemental creatures you control get +1/+1.
///    When this creature enters, create a number of 1/0 blue Elemental
///    creature tokens equal to your devotion to blue. (Each {U} in the mana
///    costs of permanents you control counts toward your devotion to blue.)"
///
/// Covers:
///   - Card shape: name, Creature, Merfolk + Wizard subtypes, {3}{U}, 2/1.
///   - NamedCardFactory dispatch.
///   - Protection from red (CR 702.16).
///   - Elemental +1/+1 anthem (CR 613.7c): buffs own Elementals, scoped to
///     controller ("you control"), not opponents'; non-Elementals unaffected;
///     LTB lifts.
///   - ETB self-trigger (CR 603.1): fires for this card, not another.
///   - ETB devotion-to-blue token mint (CR 700.5 / 111): N = devotion to blue
///     (including Master's own {U}); tokens are 1/0 blue Elementals; the anthem
///     pulls them to 2/1; devotion-0 makes no tokens.
/// </summary>
[Trait("Color", "U")]
public class MasterOfWavesFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private TriggeredAbility EtbOf(Creature c)
        => c.Abilities.OfType<TriggeredAbility>().Single();

    /// <summary>
    /// Place a Master of Waves on Alice's battlefield (so its own {U} counts
    /// toward her devotion to blue, as it does when the trigger resolves).
    /// </summary>
    private Creature PlaceMaster(ContinuousEffectsService? svc, ZoneService? zones = null)
    {
        var master = MasterOfWavesFactory.Create(
            _alice, continuousEffects: svc, triggers: null, zoneService: zones);
        master.SetZone(ZoneType.Battlefield);
        master.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(master);
        return master;
    }

    private void AddBluePermanent(string name, string manaCost)
    {
        var perm = new Creature(name, manaCost, power: 1, toughness: 1)
            { Owner = _alice, Controller = _alice };
        perm.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(perm);
    }

    // ─── Card shape + dispatch ──────────────────────────────────────────────

    [Fact]
    public void MasterOfWaves_IsMerfolkWizard_2_1_AtCost3U()
    {
        var c = MasterOfWavesFactory.Create(_alice);

        c.Name.Should().Be("Master of Waves");
        c.ManaCost.Should().Be("{3}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_MasterOfWaves()
    {
        var card = NamedCardFactory.Create("Master of Waves", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Master of Waves");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    [Fact]
    public void ShapeOnly_HasSingleEtbTrigger_ActiveOnBattlefield()
    {
        var c = MasterOfWavesFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the single ETB token-mint trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ─── Protection from red (CR 702.16) ────────────────────────────────────

    [Fact]
    public void MasterOfWaves_HasProtectionFromRed_NotOtherColors()
    {
        var c = MasterOfWavesFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue(
            "Master of Waves has protection from red");
        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeFalse();
    }

    // ─── Elemental +1/+1 anthem (CR 613.7c) ─────────────────────────────────

    [Fact]
    public void Anthem_BuffsOwnElementals_Plus1Plus1()
    {
        var svc = new ContinuousEffectsService();
        var elemental = MakeElemental("Mutavault Elemental", _alice, svc);
        PlaceMaster(svc);

        elemental.GetPower().Should().Be(2, "1 base +1 from the Elemental anthem");
        elemental.GetToughness().Should().Be(2, "1 base +1 from the Elemental anthem");
    }

    [Fact]
    public void Anthem_DoesNotBuffOpponentElementals()
    {
        // "Elemental creatures YOU CONTROL get +1/+1" — scoped to controller.
        var svc = new ContinuousEffectsService();
        var bobElemental = MakeElemental("Air Elemental", _bob, svc);
        PlaceMaster(svc);

        bobElemental.GetPower().Should().Be(1, "anthem is 'you control', not symmetric");
        bobElemental.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Anthem_DoesNotBuffNonElementals()
    {
        var svc = new ContinuousEffectsService();
        var bear = MakeCreature("Grizzly Bears", _alice, 2, 2, CardSubtype.Bear, svc);
        PlaceMaster(svc);

        bear.GetPower().Should().Be(2, "non-Elementals are unaffected");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void Anthem_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();
        var elemental = MakeElemental("Mutavault Elemental", _alice, svc);
        var master = PlaceMaster(svc);

        elemental.GetPower().Should().Be(2);

        master.SetZone(ZoneType.Graveyard);

        elemental.GetPower().Should().Be(1, "anthem lifts when Master leaves the battlefield");
        elemental.GetToughness().Should().Be(1);
    }

    // ─── ETB self-trigger (CR 603.1) ────────────────────────────────────────

    [Fact]
    public void Etb_FiresForSelfEntering_NotOtherCard()
    {
        var c = MasterOfWavesFactory.Create(_alice);
        var etb = EtbOf(c);

        var selfEvt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(selfEvt, etb).Should().BeTrue(
            "the ETB trigger fires when Master of Waves itself enters");

        var other = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var otherEvt = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(otherEvt, etb).Should().BeFalse(
            "the ETB trigger fires only for this specific card");
    }

    // ─── ETB devotion-to-blue token mint (CR 700.5 / 111) ───────────────────

    [Fact]
    public void Etb_OnlyOwnBluePip_CreatesOneElementalToken()
    {
        // Just Master of Waves on the battlefield: devotion to blue = its own
        // {U} = 1 (CR 700.5 — the source counts itself when its trigger
        // resolves), so exactly one 1/0 blue Elemental token is created.
        var svc = new ContinuousEffectsService();
        var master = PlaceMaster(svc);

        foreach (var e in EtbOf(master).Effects) e.Execute();

        var tokens = ElementalTokensOnAliceBattlefield();
        tokens.Should().HaveCount(1, "devotion to blue = 1 (Master's own {U})");

        var token = tokens[0];
        token.IsToken.Should().BeTrue();
        token.BasePower.Should().Be(1, "printed 1/0 blue Elemental token");
        token.BaseToughness.Should().Be(0);
        token.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        CardColors.GetColors(token).Should().Contain(ManaColor.Blue);

        // The +1/+1 Elemental anthem pulls the 1/0 token up to an effective
        // 2/1 (so it survives the 0-toughness SBA 704.5f).
        token.GetPower().Should().Be(2, "anthem makes the 1/0 token a 2/1");
        token.GetToughness().Should().Be(1);
    }

    [Fact]
    public void Etb_CountsOtherBluePermanents_TowardTokenCount()
    {
        var svc = new ContinuousEffectsService();
        var master = PlaceMaster(svc);
        AddBluePermanent("Snapcaster Mage", "{1}{U}");           // +1 blue
        AddBluePermanent("Cryptic Command", "{1}{U}{U}{U}");     // +3 blue
        AddBluePermanent("Grizzly Stand-in", "{1}{G}");          // +0 blue

        // Devotion to blue = 1 (Master) + 1 + 3 = 5.
        foreach (var e in EtbOf(master).Effects) e.Execute();

        ElementalTokensOnAliceBattlefield().Should().HaveCount(5, "devotion to blue = 5");
    }

    [Fact]
    public void Etb_DevotionZero_CreatesNoTokens()
    {
        // Master of Waves NOT on the battlefield and no blue permanents → the
        // controller's devotion to blue is 0, so no tokens are created.
        var svc = new ContinuousEffectsService();
        var master = MasterOfWavesFactory.Create(
            _alice, continuousEffects: svc, triggers: null);

        foreach (var e in EtbOf(master).Effects) e.Execute();

        ElementalTokensOnAliceBattlefield().Should().BeEmpty(
            "devotion to blue = 0 ⇒ no tokens");
    }

    [Fact]
    public void Etb_DevotionHelper_CountsBluePips()
    {
        PlaceMaster(svc: null);
        AddBluePermanent("Snapcaster Mage", "{1}{U}");

        NykthosShrineToNyxFactory
            .ComputeDevotionToColor(_alice, ManaColor.Blue)
            .Should().Be(2, "Master of Waves's {U} + Snapcaster Mage's {U}");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private IReadOnlyList<Creature> ElementalTokensOnAliceBattlefield()
        => _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Elemental")
            .ToList();

    private static Creature MakeElemental(string name, Player controller, ContinuousEffectsService svc)
        => MakeCreature(name, controller, power: 1, toughness: 1, CardSubtype.Elemental, svc);

    private static Creature MakeCreature(
        string name,
        Player controller,
        int power,
        int toughness,
        CardSubtype subtype,
        ContinuousEffectsService svc)
        => new Creature(name, "U", power, toughness, subtypes: new[] { subtype })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
}
