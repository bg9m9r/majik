using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Cards;

/// <summary>
/// Tests for <see cref="LordOfAtlantisFactory"/> and
/// <see cref="MasterOfThePearlTridentFactory"/>.
///
/// Covers:
/// - Identity (name, type, mana cost, Merfolk subtype, 2/2, owner/controller).
/// - NamedCardFactory dispatch for both cards.
/// - LordStaticEffect: Lord of Atlantis buffs own Merfolk AND opponent Merfolk
///   (symmetric, allPlayers: true).
/// - LordStaticEffect: Master of the Pearl Trident buffs only controller's
///   Merfolk (not opponent's, allPlayers: false).
/// - Non-Merfolk creatures are unaffected by both lords.
/// - Neither lord self-buffs (includeSelf: false — "Other Merfolk").
/// - Islandwalk keyword is granted to affected creatures.
/// - LTB lifts the bonus (IsActive gate).
/// </summary>
public class LordOfAtlantisTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ─── Lord of Atlantis identity ───────────────────────────────────────────

    [Fact]
    public void LordOfAtlantis_Identity()
    {
        var lord = LordOfAtlantisFactory.Create(_alice);

        lord.Name.Should().Be("Lord of Atlantis");
        lord.ManaCost.Should().Be("{U}{U}");
        lord.HasType(CardType.Creature).Should().BeTrue();
        lord.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        lord.BasePower.Should().Be(2);
        lord.BaseToughness.Should().Be(2);
        lord.Owner.Should().BeSameAs(_alice);
        lord.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LordOfAtlantis_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Lord of Atlantis", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Lord of Atlantis");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
    }

    // ─── Lord of Atlantis static effect ─────────────────────────────────────

    [Fact]
    public void LordOfAtlantis_BuffsOwnMerfolk_Plus1Plus1AndIslandwalk()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk("Silvergill Adept", _alice, svc);
        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2,
            "Alice's Merfolk gets +1/+1 from Lord of Atlantis (1 base + 1).");
        aliceMerfolk.GetToughness().Should().Be(2);
        HasIslandwalk(aliceMerfolk).Should().BeTrue(
            "Lord of Atlantis grants Islandwalk to other Merfolk.");
    }

    [Fact]
    public void LordOfAtlantis_IsSymmetric_BuffsOpponentMerfolk()
    {
        // Lord of Atlantis says "Other Merfolk" — no "you control" qualifier,
        // so it's symmetric (allPlayers: true). Opponents' Merfolk also benefit.
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk("Merrow Reejerey", _bob, svc);
        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        bobMerfolk.GetPower().Should().Be(2,
            "Lord of Atlantis is symmetric — Bob's Merfolk also gets +1/+1.");
        bobMerfolk.GetToughness().Should().Be(2);
        HasIslandwalk(bobMerfolk).Should().BeTrue(
            "Lord of Atlantis grants Islandwalk even to opponent's Merfolk.");
    }

    [Fact]
    public void LordOfAtlantis_DoesNotSelfBuff()
    {
        var svc = new ContinuousEffectsService();

        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        lord.GetPower().Should().Be(2, "Lord of Atlantis says 'Other' — no self-buff.");
        lord.GetToughness().Should().Be(2);
    }

    [Fact]
    public void LordOfAtlantis_DoesNotBuff_NonMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Grizzly Bears", _alice, power: 2, toughness: 2, CardSubtype.Bear, svc);
        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Lord of Atlantis only buffs Merfolk.");
        bear.GetToughness().Should().Be(2);
        HasIslandwalk(bear).Should().BeFalse("non-Merfolk don't get Islandwalk.");
    }

    [Fact]
    public void LordOfAtlantis_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk("Silvergill Adept", _alice, svc);
        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2);

        lord.SetZone(ZoneType.Graveyard);

        aliceMerfolk.GetPower().Should().Be(1, "bonus lifts when Lord leaves the battlefield.");
        aliceMerfolk.GetToughness().Should().Be(1);
        HasIslandwalk(aliceMerfolk).Should().BeFalse(
            "Islandwalk grant lifts when Lord leaves the battlefield.");
    }

    // ─── Master of the Pearl Trident identity ────────────────────────────────

    [Fact]
    public void MasterOfThePearlTrident_Identity()
    {
        var master = MasterOfThePearlTridentFactory.Create(_alice);

        master.Name.Should().Be("Master of the Pearl Trident");
        master.ManaCost.Should().Be("{U}{U}");
        master.HasType(CardType.Creature).Should().BeTrue();
        master.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
        master.BasePower.Should().Be(2);
        master.BaseToughness.Should().Be(2);
        master.Owner.Should().BeSameAs(_alice);
        master.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MasterOfThePearlTrident_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Master of the Pearl Trident", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Master of the Pearl Trident");
        card.HasSubtype(CardSubtype.Merfolk).Should().BeTrue();
    }

    // ─── Master of the Pearl Trident static effect ───────────────────────────

    [Fact]
    public void MasterOfThePearlTrident_BuffsOwnMerfolk_Plus1Plus1AndIslandwalk()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk("Silvergill Adept", _alice, svc);
        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2,
            "Master of the Pearl Trident gives +1/+1 to controller's other Merfolk.");
        aliceMerfolk.GetToughness().Should().Be(2);
        HasIslandwalk(aliceMerfolk).Should().BeTrue(
            "Master of the Pearl Trident grants Islandwalk to controller's other Merfolk.");
    }

    [Fact]
    public void MasterOfThePearlTrident_IsNotSymmetric_DoesNotBuffOpponentMerfolk()
    {
        // Master says "Other Merfolk you control" — scoped to controller only.
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk("Merrow Reejerey", _bob, svc);
        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        bobMerfolk.GetPower().Should().Be(1,
            "Master of the Pearl Trident does not buff opponent's Merfolk ('you control').");
        bobMerfolk.GetToughness().Should().Be(1);
        HasIslandwalk(bobMerfolk).Should().BeFalse(
            "Opponent's Merfolk don't get Islandwalk from Master.");
    }

    [Fact]
    public void MasterOfThePearlTrident_DoesNotSelfBuff()
    {
        var svc = new ContinuousEffectsService();

        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        master.GetPower().Should().Be(2, "Master says 'Other' — no self-buff.");
        master.GetToughness().Should().Be(2);
    }

    [Fact]
    public void MasterOfThePearlTrident_DoesNotBuff_NonMerfolk()
    {
        var svc = new ContinuousEffectsService();

        var bear = MakeCreature("Grizzly Bears", _alice, power: 2, toughness: 2, CardSubtype.Bear, svc);
        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        bear.GetPower().Should().Be(2, "Master only buffs Merfolk.");
        bear.GetToughness().Should().Be(2);
        HasIslandwalk(bear).Should().BeFalse("non-Merfolk don't get Islandwalk.");
    }

    [Fact]
    public void MasterOfThePearlTrident_LTB_LiftsBonus()
    {
        var svc = new ContinuousEffectsService();

        var aliceMerfolk = MakeMerfolk("Silvergill Adept", _alice, svc);
        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        aliceMerfolk.GetPower().Should().Be(2);

        master.SetZone(ZoneType.Graveyard);

        aliceMerfolk.GetPower().Should().Be(1, "bonus lifts when Master leaves the battlefield.");
        aliceMerfolk.GetToughness().Should().Be(1);
        HasIslandwalk(aliceMerfolk).Should().BeFalse(
            "Islandwalk grant lifts when Master leaves the battlefield.");
    }

    // ─── Symmetry comparison test ─────────────────────────────────────────────

    [Fact]
    public void LordAndMaster_Compared_LordBuffsOpponent_MasterDoesNot()
    {
        // Put both lords on Alice's side and a Merfolk on Bob's side.
        // Only Lord of Atlantis should buff Bob's Merfolk.
        var svc = new ContinuousEffectsService();

        var bobMerfolk = MakeMerfolk("Merrow Reejerey", _bob, svc);

        var lord = LordOfAtlantisFactory.Create(_alice, svc);
        lord.Zone = ZoneType.Battlefield;
        lord.ActiveEffects = svc;

        var master = MasterOfThePearlTridentFactory.Create(_alice, svc);
        master.Zone = ZoneType.Battlefield;
        master.ActiveEffects = svc;

        // Bob's Merfolk should get +1/+1 from Lord (1 base → 2),
        // but NOT from Master (still 2, not 3).
        bobMerfolk.GetPower().Should().Be(2,
            "Only Lord of Atlantis buffs Bob's Merfolk — Master of the Pearl Trident does not.");
        HasIslandwalk(bobMerfolk).Should().BeTrue(
            "Lord of Atlantis grants Islandwalk to opponent's Merfolk.");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Creature MakeMerfolk(string name, Player controller, ContinuousEffectsService svc)
        => MakeCreature(name, controller, power: 1, toughness: 1, CardSubtype.Merfolk, svc);

    private static Creature MakeCreature(
        string name,
        Player controller,
        int power,
        int toughness,
        CardSubtype subtype,
        ContinuousEffectsService svc)
    {
        var c = new Creature(name, "U", power, toughness, subtypes: new[] { subtype })
        {
            Owner = controller,
            Controller = controller,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };
        return c;
    }

    /// <summary>
    /// Read Islandwalk from the creature's effective characteristics
    /// (applying all continuous effects on the service it's registered with).
    /// </summary>
    private static bool HasIslandwalk(Creature c)
    {
        var chars = c.ActiveEffects?.Compute(c);
        if (chars is null)
        {
            // No effects service — read from raw keyword abilities
            return c.Abilities.OfType<Majik.Core.Abilities.KeywordAbility>()
                .Any(k => k.Keyword == "Islandwalk");
        }
        return chars.Keywords.Contains("Islandwalk");
    }
}
