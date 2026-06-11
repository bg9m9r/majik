using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Gray Merchant of Asphodel (Theros, {3}{B}{B}).
///
/// Creature — Zombie 2/4. Oracle text (verified against Scryfall 2026-06-02):
///   "When this creature enters, each opponent loses X life, where X is your
///    devotion to black. You gain life equal to the life lost this way.
///    (Each {B} in the mana costs of permanents you control counts toward your
///    devotion to black.)"
///
/// Covers:
///   - Card shape: name, Creature, Zombie subtype, {3}{B}{B}, 2/4.
///   - NamedCardFactory dispatch.
///   - ETB self-trigger (CR 603.1): fires for this card entering, not another.
///   - ETB devotion drain (CR 700.5 / 119.3): each opponent loses X =
///     devotion to black (including Gray Merchant's own {B}{B}); the controller
///     gains the total life lost; multi-opponent totalling; devotion-0 no-op;
///     no-resolver no-op.
/// </summary>
[Trait("Color", "B")]
public class GrayMerchantOfAsphodelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly Player _carol = new("Carol", 20);

    private TriggeredAbility EtbOf(Creature c)
        => c.Abilities.OfType<TriggeredAbility>().Single();

    /// <summary>
    /// Place a Gray Merchant on Alice's battlefield (so its own {B}{B} counts
    /// toward her devotion to black, as it does when the trigger resolves).
    /// </summary>
    private Creature PlaceMerchant()
    {
        var merchant = GrayMerchantOfAsphodelFactory.Create(_alice);
        merchant.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(merchant);
        return merchant;
    }

    /// <summary>
    /// Resolve the ETB trigger through the async path with a live
    /// <see cref="GameContext"/>, so "each opponent" reads off the live game
    /// exactly as it does in a real match (the resolver-null bug class fix).
    /// </summary>
    private static void ResolveEtb(Creature merchant, Player controller, params Player[] players)
    {
        var game = new GameContext(
            self: controller,
            allPlayers: players,
            activePlayer: controller,
            turnNumber: 1,
            currentPhase: null,
            stack: new Majik.Core.Stack.Stack(new EventBus()));

        var trigger = merchant.Abilities.OfType<TriggeredAbility>().Single();
        trigger.ResolveAsync(agent: null, game: game).AsTask().GetAwaiter().GetResult();
    }

    private void AddBlackPermanent(string name, string manaCost)
    {
        var perm = new Creature(name, manaCost, power: 1, toughness: 1)
            { Owner = _alice, Controller = _alice };
        perm.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(perm);
    }

    // ─── Card shape + dispatch ──────────────────────────────────────────────

    [Fact]
    public void GrayMerchant_IsZombie_2_4_AtCost3BB()
    {
        var c = GrayMerchantOfAsphodelFactory.Create(_alice);

        c.Name.Should().Be("Gray Merchant of Asphodel");
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_GrayMerchant()
    {
        var card = NamedCardFactory.Create("Gray Merchant of Asphodel", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Gray Merchant of Asphodel");
        card.HasSubtype(CardSubtype.Zombie).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(4);
    }

    [Fact]
    public void ShapeOnly_HasSingleEtbTrigger_ActiveOnBattlefield()
    {
        var c = GrayMerchantOfAsphodelFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the single ETB devotion-drain trigger");
        triggers[0].ActiveZones.Should().Contain(ZoneType.Battlefield);
    }

    // ─── ETB self-trigger (CR 603.1) ────────────────────────────────────────

    [Fact]
    public void Etb_FiresForSelfEntering_NotOtherCard()
    {
        var c = GrayMerchantOfAsphodelFactory.Create(_alice);
        var etb = EtbOf(c);

        var selfEvt = new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(selfEvt, etb).Should().BeTrue(
            "the ETB trigger fires when Gray Merchant itself enters");

        var other = new Creature("Grizzly Bears", "1G", 2, 2) { Owner = _alice };
        var otherEvt = new CardMovedEvent(other, ZoneType.Hand, ZoneType.Battlefield);
        etb.Condition.Matches(otherEvt, etb).Should().BeFalse(
            "the ETB trigger fires only for this specific card");
    }

    // ─── ETB devotion drain (CR 700.5 / 119.3) ──────────────────────────────

    [Fact]
    public void Etb_OnlyOwnTwoBlackPips_DrainsTwoGainsTwo()
    {
        // Just Gray Merchant on the battlefield: devotion to black = its own
        // {B}{B} = 2 (CR 700.5 — the source counts itself once resolving).
        var merchant = PlaceMerchant();

        ResolveEtb(merchant, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(18, "X = devotion to black = 2 (Gray Merchant's own {B}{B})");
        _alice.LifeTotal.Should().Be(22, "Alice gains life equal to the 2 life lost this way");
    }

    /// <summary>
    /// PROD-PATH guard (resolver-null bug class). Gray Merchant's ETB drain is a
    /// Modern staple — the routed <see cref="NamedCardFactory.Create(string, Player)"/>
    /// build must drain each opponent read off the live <see cref="GameContext"/>,
    /// not no-op on a captured-null resolver.
    /// </summary>
    [Fact]
    public void Etb_DrainsEachOpponent_OnProdBuild()
    {
        var merchant = (Creature)NamedCardFactory.Create("Gray Merchant of Asphodel", _alice);
        merchant.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(merchant);

        ResolveEtb(merchant, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(18,
            "the prod-built ETB devotion drain reads opponents from the live context (not inert)");
        _alice.LifeTotal.Should().Be(22, "Alice gains the 2 life lost this way");
    }

    [Fact]
    public void Etb_CountsOtherBlackPermanents_TowardDevotion()
    {
        var merchant = PlaceMerchant();
        AddBlackPermanent("Diregraf Ghoul", "{B}");        // +1 black
        AddBlackPermanent("Phyrexian Obliterator", "{B}{B}{B}{B}"); // +4 black
        AddBlackPermanent("Grizzly Bears Stand-in", "{1}{G}");      // +0 black

        // Devotion to black = 2 (merchant) + 1 + 4 = 7.
        ResolveEtb(merchant, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(13, "X = devotion to black = 7");
        _alice.LifeTotal.Should().Be(27, "Alice gains the 7 life lost this way");
    }

    [Fact]
    public void Etb_MultipleOpponents_EachLosesX_GainsTotal()
    {
        var merchant = PlaceMerchant();
        AddBlackPermanent("Diregraf Ghoul", "{B}"); // devotion = 2 + 1 = 3

        ResolveEtb(merchant, _alice, _alice, _bob, _carol);

        _bob.LifeTotal.Should().Be(17, "each opponent loses X = 3");
        _carol.LifeTotal.Should().Be(17, "each opponent loses X = 3");
        _alice.LifeTotal.Should().Be(26,
            "Alice gains the TOTAL life lost this way = 3 + 3 = 6");
    }

    [Fact]
    public void Etb_DevotionZero_NoLifeChange()
    {
        // Gray Merchant NOT on the battlefield and no black permanents → the
        // controller's devotion to black is 0, so X = 0 and nothing happens.
        var merchant = GrayMerchantOfAsphodelFactory.Create(_alice);

        ResolveEtb(merchant, _alice, _alice, _bob);

        _bob.LifeTotal.Should().Be(20, "devotion to black = 0 ⇒ each opponent loses 0");
        _alice.LifeTotal.Should().Be(20, "no life lost this way ⇒ no lifegain");
    }

    [Fact]
    public void Etb_NoLiveGame_NoOps()
    {
        var merchant = PlaceMerchant();

        // No live game context → no opponents → drain no-ops.
        foreach (var e in EtbOf(merchant).Effects) e.Execute();

        _bob.LifeTotal.Should().Be(20, "no live game ⇒ drain no-ops");
        _alice.LifeTotal.Should().Be(20, "no life lost this way ⇒ no lifegain");
    }

    [Fact]
    public void Etb_DevotionHelper_CountsBlackPips()
    {
        PlaceMerchant();
        AddBlackPermanent("Diregraf Ghoul", "{B}");

        NykthosShrineToNyxFactory
            .ComputeDevotionToColor(_alice, ManaColor.Black)
            .Should().Be(3, "Gray Merchant's {B}{B} + Diregraf Ghoul's {B}");
    }
}
