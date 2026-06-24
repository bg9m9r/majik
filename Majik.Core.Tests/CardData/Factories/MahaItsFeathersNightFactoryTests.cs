using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MahaItsFeathersNightFactory"/>
/// (Bloomburrow, {3}{B}{B}). Legendary Creature — Elemental Bird 6/5.
/// Oracle text (verified against Scryfall):
///   "Flying, trample
///    Ward—Discard a card.
///    Creatures your opponents control have base toughness 1."
///
/// Coverage (UNIQUE behaviour only — CardFactoryContractTests already
/// asserts NamedCardFactory dispatch + well-formedness):
/// - Identity (cost / P-T / Legendary / Elemental Bird) — single assert.
/// - Flying + Trample keyword markers (CR 702.9 / 702.19).
/// - Ward—Discard a card (CR 702.21): the bound WardEffect charges a real
///   discard, and the prod-dispatch shape carries a resident ward trigger.
/// - Opponents'-base-toughness static (CR 613.7b): opponent creatures are set
///   to base toughness 1 (power untouched); the controller's own creatures and
///   Maha itself are unaffected; a +1/+1 counter still stacks on top (Layer 7c
///   over the 7b set — CR 613.7); the debuff lifts when Maha is not on the
///   battlefield.
/// </summary>
[Trait("Color", "B")]
public class MahaItsFeathersNightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeBear(Player owner, int power = 2, int toughness = 2)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power, toughness);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    // ── Identity ────────────────────────────────────────────────────────

    [Fact]
    public void Maha_Identity()
    {
        var maha = MahaItsFeathersNightFactory.Create(_alice);

        maha.Name.Should().Be("Maha, Its Feathers Night");
        maha.ManaCost.Should().Be("{3}{B}{B}");
        maha.ManaCostValue.TotalValue.Should().Be(5);
        maha.HasType(CardType.Creature).Should().BeTrue();
        maha.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        maha.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        maha.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        maha.BasePower.Should().Be(6);
        maha.BaseToughness.Should().Be(5);
        CardColors.GetColors(maha).Should().Contain(ManaColor.Black);
    }

    // ── Keywords ────────────────────────────────────────────────────────

    [Fact]
    public void Maha_HasFlyingAndTrample()
    {
        var maha = MahaItsFeathersNightFactory.Create(_alice);

        CombatAbilities.HasFlying(maha).Should().BeTrue("Maha prints Flying (CR 702.9).");
        CombatAbilities.HasTrample(maha).Should().BeTrue("Maha prints trample (CR 702.19).");
    }

    // ── Ward—Discard a card (CR 702.21) ─────────────────────────────────

    [Fact]
    public void Maha_BuildWardEffect_ChargesDiscard()
    {
        var maha = MahaItsFeathersNightFactory.Create(_alice);
        maha.SetController(_alice);
        var ward = MahaItsFeathersNightFactory.BuildWardEffect(maha);

        ward.Source.Should().BeSameAs(maha);
        // Printed cost is non-mana ("discard a card") — mana portion is zero.
        ward.Cost.TotalValue.Should().Be(0);

        // CR 702.21f — opponent with a card discards it → not countered.
        var spare = new Creature("Spare", "{1}", 1, 1) { Owner = _bob, Controller = _bob };
        _bob.Zones.Hand.AddCard(spare);
        ward.Resolve(_bob).Should().BeFalse("Bob discards a card to satisfy the ward.");
        _bob.Zones.Graveyard.GetCards().Should().Contain(spare);

        // Empty hand → cannot pay → the spell/ability is countered.
        ward.Resolve(_bob).Should().BeTrue("Bob's hand is now empty — the ward bites.");
    }

    [Fact]
    public void Maha_ProdDispatch_CarriesWardTrigger()
    {
        // The prod build path (NamedCardFactory.Create → Create(owner)) must
        // produce a card with a real ITriggeredAbility — Ward is a triggered
        // ability (CR 702.21e). Maha's reads "a spell or ability".
        var card = NamedCardFactory.Create("Maha, Its Feathers Night", _alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "Ward—Discard a card is a triggered ability (CR 702.21e).");
    }

    // ── Opponents'-base-toughness static (CR 613.7b) ────────────────────

    [Fact]
    public void Maha_SetsOpponentCreatureBaseToughnessTo1_PowerUntouched()
    {
        var svc = new ContinuousEffectsService();

        var bobBear = MakeBear(_bob, power: 4, toughness: 5);
        bobBear.ActiveEffects = svc;

        var maha = MahaItsFeathersNightFactory.Create(_alice, svc, triggers: null);
        maha.SetZone(ZoneType.Battlefield);
        maha.ActiveEffects = svc;

        bobBear.GetToughness().Should().Be(1,
            "Maha sets opponents' creatures to base toughness 1 (CR 613.7b).");
        bobBear.GetPower().Should().Be(4,
            "only base TOUGHNESS is set — base power is left as printed.");
    }

    [Fact]
    public void Maha_DoesNotAffectControllersOwnCreatures()
    {
        var svc = new ContinuousEffectsService();

        var aliceBear = MakeBear(_alice, power: 2, toughness: 2);
        aliceBear.ActiveEffects = svc;

        var maha = MahaItsFeathersNightFactory.Create(_alice, svc, triggers: null);
        maha.SetZone(ZoneType.Battlefield);
        maha.ActiveEffects = svc;

        aliceBear.GetToughness().Should().Be(2,
            "'your opponents control' — Alice's own creatures are unaffected (CR 109.5).");
    }

    [Fact]
    public void Maha_DoesNotAffectItself()
    {
        var svc = new ContinuousEffectsService();

        var maha = MahaItsFeathersNightFactory.Create(_alice, svc, triggers: null);
        maha.SetZone(ZoneType.Battlefield);
        maha.ActiveEffects = svc;

        maha.GetToughness().Should().Be(5,
            "Maha controls itself — it is not one of its controller's opponents' creatures.");
    }

    [Fact]
    public void Maha_PlusOneCounterStacksOnTopOfBaseSet()
    {
        var svc = new ContinuousEffectsService();

        var bobBear = MakeBear(_bob, power: 2, toughness: 2);
        bobBear.ActiveEffects = svc;

        var maha = MahaItsFeathersNightFactory.Create(_alice, svc, triggers: null);
        maha.SetZone(ZoneType.Battlefield);
        maha.ActiveEffects = svc;

        // CR 613.7 — a +1/+1 counter (Layer 7c) applies AFTER the base set
        // (Layer 7b): base toughness 1 + 1 = 2.
        bobBear.Counters.Add(CounterType.PlusOnePlusOne);

        bobBear.GetToughness().Should().Be(2,
            "a +1/+1 counter raises the set base toughness 1 → 2 (Layer 7c over 7b).");
    }

    [Fact]
    public void Maha_DebuffLiftsWhenNotOnBattlefield()
    {
        var svc = new ContinuousEffectsService();

        var bobBear = MakeBear(_bob, power: 4, toughness: 5);
        bobBear.ActiveEffects = svc;

        var maha = MahaItsFeathersNightFactory.Create(_alice, svc, triggers: null);
        maha.SetZone(ZoneType.Battlefield);
        maha.ActiveEffects = svc;

        bobBear.GetToughness().Should().Be(1, "static is active while Maha is on the battlefield.");

        // Maha leaves play — the static stops applying (CR 613.7b only while
        // the source is on the battlefield).
        maha.SetZone(ZoneType.Graveyard);

        bobBear.GetToughness().Should().Be(5,
            "with Maha gone, the opponent creature reverts to its printed base toughness.");
    }
}
