using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BedlamRevelerFactory"/>.
///
/// Bedlam Reveler (Eldritch Moon, {6}{R}{R}):
///   Creature — Horror 3/4. Trample. Prowess.
///   This spell costs {1} less to cast for each instant and sorcery card
///   in your graveyard.
///   When this creature enters, if you cast it from your hand, discard
///   your hand, then draw three cards.
///
/// Covers:
///   - Card identity (Horror 3/4, {6}{R}{R}, owner/controller, Trample,
///     Prowess keyword markers).
///   - <see cref="NamedCardFactory"/> dispatcher entry.
///   - Cost reduction at 0 / 5 / 8 instants+sorceries in graveyard (floor
///     at coloured {R}{R} pips per CR 117.7c).
///   - ETB trigger structure (single TriggeredAbility, Battlefield active
///     zone, intervening-if present).
///   - ETB resolution paths: cast-from-hand fires (discard hand + draw 3);
///     non-cast-from-hand path no-ops; intervening-if false short-circuits;
///     short library mid-draw flags the SBA loss.
/// </summary>
public class BedlamRevelerTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void BedlamReveler_Identity_Horror_3_4_At_6RR_WithTrampleAndProwess()
    {
        var reveler = BedlamRevelerFactory.Create(_alice);

        reveler.Name.Should().Be("Bedlam Reveler");
        reveler.ManaCost.Should().Be("{6}{R}{R}");
        reveler.HasType(CardType.Creature).Should().BeTrue();
        reveler.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        reveler.BasePower.Should().Be(3);
        reveler.BaseToughness.Should().Be(4);
        reveler.Owner.Should().BeSameAs(_alice);
        reveler.Controller.Should().BeSameAs(_alice);

        CombatAbilities.HasTrample(reveler).Should().BeTrue();
        reveler.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Trample");
        reveler.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Prowess");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_BedlamReveler()
    {
        var card = NamedCardFactory.Create("Bedlam Reveler", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Bedlam Reveler");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Horror).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(3);
        ((Creature)card).BaseToughness.Should().Be(4);

        // Keyword markers, cost-reducer, ETB trigger all attached.
        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain(new[] { "Trample", "Prowess" });
        card.Abilities.OfType<CostReductionAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void BedlamReveler_EmptyGraveyard_PaysFullCost()
    {
        // 0 instants / sorceries in graveyard → no reduction. Pays
        // {6}{R}{R}: generic = 6, red pips = 2.
        var reveler = BedlamRevelerFactory.Create(_alice);

        var effective = CostReduction.GetEffectiveCost(reveler, _alice);

        effective.Generic.Should().Be(6);
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void BedlamReveler_FiveInstantsOrSorceriesInGraveyard_ReducesTo_R_R()
    {
        // 5 instants/sorceries → reduction = 5 generic. Pays {1}{R}{R}:
        // generic = 1, red pips = 2.
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 3, sorceries: 2);

        var effective = CostReduction.GetEffectiveCost(reveler, _alice);

        effective.Generic.Should().Be(1);
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void BedlamReveler_EightInstantsOrSorceriesInGraveyard_FloorsAtColouredPips()
    {
        // 8 instants/sorceries → reduction = 8 generic. Printed generic
        // is 6, so reduction floors at 0 generic. Coloured pips
        // untouched (CR 117.7c) — still pays {R}{R}.
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeedGraveyardWithSpells(_alice, instants: 4, sorceries: 4);

        var effective = CostReduction.GetEffectiveCost(reveler, _alice);

        effective.Generic.Should().Be(0);
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void BedlamReveler_NonInstantSorceryGraveyardCards_DoNotReduce()
    {
        // Creatures + lands in graveyard — none should count.
        var reveler = BedlamRevelerFactory.Create(_alice);
        AddToGraveyard(_alice, new Creature("Bear A", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Creature("Bear B", "{1}{G}", 2, 2));
        AddToGraveyard(_alice, new Land("Plains",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Plains }));

        var effective = CostReduction.GetEffectiveCost(reveler, _alice);

        effective.Generic.Should().Be(6,
            "non-instant/sorcery cards don't trigger Bedlam Reveler's reduction");
        effective.Red.Should().Be(2);
    }

    [Fact]
    public void BedlamReveler_EtbTrigger_HasBattlefieldActiveZone_AndInterveningIf()
    {
        var reveler = BedlamRevelerFactory.Create(_alice);

        var triggers = reveler.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1);

        var t = triggers[0];
        t.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "the ETB trigger fires from the battlefield (CR 603.6a)");
        t.InterveningIf.Should().NotBeNull(
            "the printed 'if you cast it from your hand' clause is an intervening-if");
    }

    [Fact]
    public void BedlamReveler_InterveningIf_True_OnlyWhenCastFromHand()
    {
        var reveler = BedlamRevelerFactory.Create(_alice);
        var trigger = reveler.Abilities.OfType<TriggeredAbility>().Single();

        // No cast stamp → intervening-if is false (reanimation / blink /
        // token-copy paths).
        trigger.CanBePutOnStack().Should().BeFalse();

        // Stamp the cast-from-hand sentinel (mirrors SpellCastFlow's
        // Card.SetWasCastFromHand call at stack push time).
        reveler.SetWasCastFromHand(true);
        trigger.CanBePutOnStack().Should().BeTrue();
    }

    [Fact]
    public void EtbResolve_CastFromHand_DiscardsHand_AndDrawsThree()
    {
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeatOnBattlefield(reveler);

        // Hand: 3 cards. After ETB resolution all three should land in
        // graveyard (CR 701.16).
        var h1 = new Instant("Bolt", "{R}") { Owner = _alice };
        var h2 = new Sorcery("Looting", "{R}") { Owner = _alice };
        var h3 = new Creature("Bear", "{1}{G}", 2, 2) { Owner = _alice };
        AddToHand(_alice, h1, h2, h3);

        // Library: 4 cards → after 3 draws, 1 remains.
        var t1 = new Instant("Top1", "{R}") { Owner = _alice };
        var t2 = new Instant("Top2", "{R}") { Owner = _alice };
        var t3 = new Instant("Top3", "{R}") { Owner = _alice };
        var t4 = new Instant("Top4", "{R}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2, t3, t4);

        // Mark cast-from-hand so the intervening-if + resolve guard pass.
        reveler.SetWasCastFromHand(true);

        ExecuteEtb(reveler);

        // Hand: now contains the 3 drawn cards (and only those — discard
        // happened BEFORE the draws per the printed "then" order).
        _alice.Zones.Hand.GetCards().Should().HaveCount(3);
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { t1, t2, t3 });

        // Graveyard: 3 discarded cards.
        _alice.Zones.Graveyard.GetCards().Should().Contain(new ICard[] { h1, h2, h3 });

        // Library: t4 remains.
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(t4);

        // The Reveler itself stays on the battlefield — it's NOT in the
        // discarded hand (ETB resolves AFTER the permanent has entered).
        reveler.Zone.Should().Be(ZoneType.Battlefield);
    }

    [Fact]
    public void EtbResolve_NotCastFromHand_NoOp()
    {
        // Reanimation / blink / token-copy path: WasCastFromHand stays
        // false → resolve-time guard short-circuits (and the queue-time
        // intervening-if would have already skipped the trigger). The
        // resolve body is a clean no-op even if it does get invoked.
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeatOnBattlefield(reveler);

        var h1 = new Instant("Bolt", "{R}") { Owner = _alice };
        AddToHand(_alice, h1);
        var t1 = new Instant("Top", "{R}") { Owner = _alice };
        AddToLibrary(_alice, t1);

        // No SetWasCastFromHand call — simulating a non-cast battlefield
        // entry.
        ExecuteEtb(reveler);

        // Hand untouched: discard didn't fire.
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(h1);
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(t1);
    }

    [Fact]
    public void EtbResolve_CastFromHand_ShortLibrary_FlagsSbaLoss()
    {
        // Library has only 2 cards. After discarding the hand the draw-3
        // step exhausts the library mid-draw → SBA loss flag set
        // (CR 704.5b). Same handling as Faithless Looting / Wrenn's
        // Resolve when the draw underflows.
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeatOnBattlefield(reveler);

        var h1 = new Instant("Bolt", "{R}") { Owner = _alice };
        AddToHand(_alice, h1);

        var t1 = new Instant("Top1", "{R}") { Owner = _alice };
        var t2 = new Instant("Top2", "{R}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2);

        reveler.SetWasCastFromHand(true);
        ExecuteEtb(reveler);

        // Drew the 2 available cards.
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { t1, t2 });
        _alice.Zones.Library.GetCards().Should().BeEmpty();

        // SBA loss flag flipped on the underflow attempt.
        _alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "the 3rd draw with an empty library flags the CR 704.5b SBA loss");
    }

    [Fact]
    public void EtbResolve_CastFromHand_EmptyHand_OnlyDraws()
    {
        // No cards to discard → discard loop is a no-op; the draw 3 step
        // still fires. Defensive-edge case for the "discard your hand"
        // half (CR 701.16a — discard a nonexistent card is a no-op).
        var reveler = BedlamRevelerFactory.Create(_alice);
        SeatOnBattlefield(reveler);

        var t1 = new Instant("Top1", "{R}") { Owner = _alice };
        var t2 = new Instant("Top2", "{R}") { Owner = _alice };
        var t3 = new Instant("Top3", "{R}") { Owner = _alice };
        AddToLibrary(_alice, t1, t2, t3);

        reveler.SetWasCastFromHand(true);
        ExecuteEtb(reveler);

        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().Contain(new ICard[] { t1, t2, t3 });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static void AddToGraveyard(Player p, ICard card)
    {
        if (card is Card concrete)
        {
            concrete.SetOwner(p);
            concrete.SetZone(ZoneType.Graveyard);
        }
        p.Zones.Graveyard.AddCard(card);
    }

    private static void SeedGraveyardWithSpells(Player p, int instants, int sorceries)
    {
        for (var i = 0; i < instants; i++)
        {
            AddToGraveyard(p, new Instant($"Inst{i}", "{R}"));
        }
        for (var i = 0; i < sorceries; i++)
        {
            AddToGraveyard(p, new Sorcery($"Sorc{i}", "{R}"));
        }
    }

    private static void AddToHand(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Hand);
            }
            p.Zones.Hand.AddCard(c);
        }
    }

    private static void AddToLibrary(Player p, params ICard[] cards)
    {
        foreach (var c in cards)
        {
            if (c is Card concrete)
            {
                concrete.SetOwner(p);
                concrete.SetZone(ZoneType.Library);
            }
            p.Zones.Library.AddCard(c);
        }
    }

    private static void SeatOnBattlefield(Creature card)
    {
        card.SetZone(ZoneType.Battlefield);
        card.Owner!.Zones.Battlefield.AddCard(card);
    }

    private static void ExecuteEtb(Creature card)
    {
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();
    }
}
