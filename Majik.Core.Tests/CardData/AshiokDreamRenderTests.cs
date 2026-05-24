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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Ashiok, Dream Render (War of the Spark, {1}{U/B}{U/B}).
///
/// Covers:
///   - Identity (Legendary Planeswalker, Ashiok subtype, loyalty 5, mana cost).
///   - Single loyalty ability shape (-1 only).
///   - -1 decrements loyalty by 1 + opponent mills 4 (CR 701.13b).
///   - After -1 resolves: ZoneMoveIntent targeting Graveyard gets rewritten
///     to Exile regardless of source / controller (CR 614).
///   - EOT cleanup drops the graveyard→exile replacement (CR 514.2).
///   - Static search-restriction registered while Ashiok on battlefield;
///     detaches cleanly. Distinct from Leonin Arbiter — unconditional, no
///     pay-to-bypass.
///   - NamedCardFactory dispatcher entry.
/// </summary>
public class AshiokDreamRenderTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -------------------------------------------------------------------
    // Identity + dispatch
    // -------------------------------------------------------------------

    [Fact]
    public void Ashiok_IsLegendaryPlaneswalker_Ashiok_5Loyalty_AtCost1UBUB()
    {
        var ashiok = AshiokDreamRenderFactory.Create(_alice);

        ashiok.Name.Should().Be("Ashiok, Dream Render");
        ashiok.ManaCost.Should().Be("{1}{U/B}{U/B}");
        ashiok.HasType(CardType.Planeswalker).Should().BeTrue();
        ashiok.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        ashiok.HasSubtype(CardSubtype.Ashiok).Should().BeTrue();
        ashiok.Loyalty.Should().Be(5);
        ashiok.StartingLoyalty.Should().Be(5);
        ashiok.Owner.Should().BeSameAs(_alice);
        ashiok.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Ashiok_HasSingleLoyaltyAbility_Minus1()
    {
        var ashiok = AshiokDreamRenderFactory.Create(_alice);
        var loyaltyAbilities = ashiok.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(1);
        loyaltyAbilities[0].LoyaltyChange.Should().Be(-1);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AshiokDreamRender()
    {
        var card = NamedCardFactory.Create("Ashiok, Dream Render", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Ashiok, Dream Render");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Ashiok).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(5);
        card.ManaCost.Should().Be("{1}{U/B}{U/B}");
        card.Owner.Should().BeSameAs(_alice);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(1);
    }

    // -------------------------------------------------------------------
    // -1 mill mechanic
    // -------------------------------------------------------------------

    [Fact]
    public void Minus1_DecrementsLoyaltyByOne_AndOpponentMillsFour()
    {
        // Bob's library: 6 cards so we can verify exactly 4 are milled.
        var bobLibraryCards = Enumerable.Range(0, 6)
            .Select(i =>
            {
                var c = new Card($"BobCard{i}", "U") { Owner = _bob };
                _bob.Zones.Library.AddCard(c);
                c.SetZone(ZoneType.Library);
                return c;
            }).ToList();

        var players = new[] { _alice, _bob };
        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, () => players, replacements: null, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        var minus1 = ashiok.Abilities.OfType<LoyaltyAbility>().Single();
        minus1.LoyaltyChange.Should().Be(-1);
        minus1.Activate();

        ashiok.Loyalty.Should().Be(4, "5 - 1 = 4");

        // The first 4 library cards moved to Bob's graveyard.
        _bob.Zones.Graveyard.GetCards().Should().HaveCount(4);
        _bob.Zones.Library.GetCards().Should().HaveCount(2);
        for (int i = 0; i < 4; i++)
        {
            _bob.Zones.Graveyard.GetCards().Should().Contain(bobLibraryCards[i]);
        }

        // Alice (controller) was not milled — "target opponent" semantics.
        _alice.Zones.Graveyard.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void Minus1_NoResolverWired_LoyaltyStillTicksDown()
    {
        // Single-arg dispatcher path: no allPlayersResolver — the mill body
        // no-ops but the loyalty change still applies (CR 606.3).
        var bobCard = new Card("BobCard", "U") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobCard);
        bobCard.SetZone(ZoneType.Library);

        var ashiok = AshiokDreamRenderFactory.Create(_alice);

        var minus1 = ashiok.Abilities.OfType<LoyaltyAbility>().Single();
        minus1.Activate();

        ashiok.Loyalty.Should().Be(4);
        _bob.Zones.Library.GetCards().Should().Contain(bobCard,
            "no resolver wired → mill is a silent no-op");
    }

    // -------------------------------------------------------------------
    // -1 exile rider — replacement effect on the ReplacementBus
    // -------------------------------------------------------------------

    [Fact]
    public void Minus1_RegistersReplacement_RewritingAnyGraveyardMoveToExile()
    {
        var bus = new ReplacementBus();
        var players = new[] { _alice, _bob };

        // Bob has one library card so the mill body has something to do.
        var bobLibCard = new Card("BobLibCard", "U") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobLibCard);
        bobLibCard.SetZone(ZoneType.Library);

        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, () => players, replacements: bus, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        ashiok.Abilities.OfType<LoyaltyAbility>().Single().Activate();

        // (1) Battlefield → Graveyard (creature death) — rewritten.
        var dyingCreature = new Creature("Bear", "{1}{G}", 2, 2);
        dyingCreature.SetOwner(_alice);
        dyingCreature.SetController(_alice);
        bus.Apply(new ZoneMoveIntent(
                dyingCreature, ZoneType.Battlefield, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Exile,
                "battlefield→graveyard rewrites to exile");

        // (2) Hand → Graveyard (discard) — rewritten.
        var discardedCard = new Card("Discarded", "U") { Owner = _alice };
        bus.Apply(new ZoneMoveIntent(
                discardedCard, ZoneType.Hand, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Exile,
                "hand→graveyard (discard) also rewrites — 'from anywhere'");

        // (3) Library → Graveyard (mill) — rewritten.
        var milledCard = new Card("Milled", "B") { Owner = _bob };
        bus.Apply(new ZoneMoveIntent(
                milledCard, ZoneType.Library, ZoneType.Graveyard, _bob))!
            .ToZone.Should().Be(ZoneType.Exile,
                "library→graveyard (mill) also rewrites — 'from anywhere'");
    }

    [Fact]
    public void Replacement_DoesNotRewrite_NonGraveyardDestinations()
    {
        var bus = new ReplacementBus();
        var players = new[] { _alice, _bob };

        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, () => players, replacements: bus, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        ashiok.Abilities.OfType<LoyaltyAbility>().Single().Activate();

        // Battlefield → Hand (bounce): unaffected.
        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        bus.Apply(new ZoneMoveIntent(
                bear, ZoneType.Battlefield, ZoneType.Hand, _alice))!
            .ToZone.Should().Be(ZoneType.Hand,
                "rider only catches moves whose destination is graveyard");

        // Library → Hand (draw): unaffected.
        var drawn = new Card("Drawn", "U") { Owner = _alice };
        bus.Apply(new ZoneMoveIntent(
                drawn, ZoneType.Library, ZoneType.Hand, _alice))!
            .ToZone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Minus1_WithoutBus_RiderIsSkipped_ButMillStillApplies()
    {
        var players = new[] { _alice, _bob };

        var bobLibCard = new Card("BobLibCard", "U") { Owner = _bob };
        _bob.Zones.Library.AddCard(bobLibCard);
        bobLibCard.SetZone(ZoneType.Library);

        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, () => players, replacements: null, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        var act = () => ashiok.Abilities.OfType<LoyaltyAbility>().Single().Activate();
        act.Should().NotThrow();

        _bob.Zones.Graveyard.GetCards().Should().Contain(bobLibCard,
            "mill body runs even without a ReplacementBus");
    }

    // -------------------------------------------------------------------
    // EOT cleanup
    // -------------------------------------------------------------------

    [Fact]
    public void EndOfTurn_Cleanup_RemovesGraveyardToExileReplacement()
    {
        var bus = new ReplacementBus();
        var players = new[] { _alice, _bob };

        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, () => players, replacements: bus, continuousEffects: null);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        ashiok.Abilities.OfType<LoyaltyAbility>().Single().Activate();

        var dyingBear = new Creature("Bear", "{1}{G}", 2, 2);
        dyingBear.SetOwner(_alice);
        dyingBear.SetController(_alice);

        // Before cleanup: rewritten to exile.
        bus.Apply(new ZoneMoveIntent(
                dyingBear, ZoneType.Battlefield, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Exile);

        // Cleanup sweep — CR 514.2.
        bus.ExpireEndOfTurn();

        // After cleanup: same intent now passes through to graveyard.
        bus.Apply(new ZoneMoveIntent(
                dyingBear, ZoneType.Battlefield, ZoneType.Graveyard, _alice))!
            .ToZone.Should().Be(ZoneType.Graveyard,
                "the EOT sweep dropped the IEndOfTurnExpirable replacement");
    }

    // -------------------------------------------------------------------
    // Static search-restriction effect
    // -------------------------------------------------------------------

    [Fact]
    public void SearchRestriction_NotActive_BeforeAshiokOnBattlefield()
    {
        var effects = new ContinuousEffectsService();

        // Factory wires the static effect's lifecycle binder, but Sync gates
        // on Zone == Battlefield. The factory call by itself does not yet
        // mark the restriction active — Ashiok is unzoned.
        var ashiok = AshiokDreamRenderFactory.Create(
            _alice, allPlayersResolver: null, replacements: null,
            continuousEffects: effects);

        // Sanity — re-attach a fresh effect (mirrors what would happen if
        // we built one and called Attach() with Ashiok off the battlefield).
        var probe = new AshiokSearchRestrictionEffect(ashiok, effects);
        probe.Attach();
        probe.IsRestrictionActive.Should().BeFalse(
            "Ashiok not on battlefield → restriction not registered");
    }

    [Fact]
    public void SearchRestriction_ActiveWhileAshiokOnBattlefield_DropsWhenLeaves()
    {
        var effects = new ContinuousEffectsService();

        var ashiok = AshiokDreamRenderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        var restriction = new AshiokSearchRestrictionEffect(ashiok, effects);
        restriction.Attach();
        restriction.IsRestrictionActive.Should().BeTrue(
            "Ashiok on battlefield at attach time → restriction registered");

        // Ashiok leaves the battlefield → Sync drops the restriction.
        _alice.Zones.Battlefield.RemoveCard(ashiok);
        _alice.Zones.Graveyard.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Graveyard);
        restriction.Sync();

        restriction.IsRestrictionActive.Should().BeFalse(
            "Ashiok left the battlefield → restriction unregisters");
    }

    [Fact]
    public void SearchRestriction_Detach_Unregisters()
    {
        var effects = new ContinuousEffectsService();

        var ashiok = AshiokDreamRenderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        var restriction = new AshiokSearchRestrictionEffect(ashiok, effects);
        restriction.Attach();
        restriction.IsRestrictionActive.Should().BeTrue();

        restriction.Detach();
        restriction.IsRestrictionActive.Should().BeFalse();
    }

    [Fact]
    public void SingleArgPath_NoContinuousEffectsService_NoStaticEffectAttached()
    {
        // Smoke test — single-arg dispatcher path shouldn't throw and the
        // static effect simply isn't registered (no service supplied).
        var ashiok = AshiokDreamRenderFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(ashiok);
        ashiok.SetZone(ZoneType.Battlefield);

        // Card shape intact.
        ashiok.HasSubtype(CardSubtype.Ashiok).Should().BeTrue();
        ashiok.Loyalty.Should().Be(5);
    }
}
