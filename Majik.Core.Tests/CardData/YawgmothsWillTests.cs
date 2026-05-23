using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="YawgmothsWillFactory"/>.
///
/// Card: Yawgmoth's Will — Sorcery {2}{B} (Urza's Saga).
///   "Until end of turn, you may play cards from your graveyard.
///    If a card would be put into your graveyard from anywhere this turn,
///    exile it instead."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - NamedCardFactory dispatch.
///   - Resolve effect stamps Card.RuntimeGraveyardCastCost on every card in
///     the controller's graveyard with its printed mana cost.
///   - Resolve effect registers an EOT-expirable replacement that rewrites
///     hand→graveyard (and other zone→graveyard) moves to exile for cards
///     owned by Yawgmoth's-Will's controller.
///   - Opponent discards / dies are NOT rewritten (controller-scoped).
///   - EOT cleanup sweep drops the replacement.
/// </summary>
public class YawgmothsWillTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void YawgmothsWill_Identity()
    {
        var c = YawgmothsWillFactory.Create(_alice);

        c.Name.Should().Be("Yawgmoth's Will");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_YawgmothsWill()
    {
        var card = NamedCardFactory.Create("Yawgmoth's Will", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Yawgmoth's Will");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{B}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Resolve: stamp grave-cast on every card in controller's graveyard
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_StampsGraveCastGrant_OnEveryControllerGraveyardCard()
    {
        // Three different cards in Alice's graveyard, with different
        // printed mana costs.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        bolt.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bolt);

        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2) { Owner = _alice };
        bears.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bears);

        var bauble = new Artifact("Mishra's Bauble", "{0}") { Owner = _alice };
        bauble.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(bauble);

        // Sanity — nothing stamped before resolve.
        bolt.RuntimeGraveyardCastCost.Should().BeNull();
        bears.RuntimeGraveyardCastCost.Should().BeNull();
        bauble.RuntimeGraveyardCastCost.Should().BeNull();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: null);
        foreach (var e in effects) e.Execute();

        bolt.RuntimeGraveyardCastCost.Should().NotBeNull(
            "Yawgmoth's Will lets the controller play any card from their graveyard");
        bolt.RuntimeGraveyardCastCost!.TotalValue.Should().Be(1, "Bolt is {R} — mv 1");

        bears.RuntimeGraveyardCastCost.Should().NotBeNull();
        bears.RuntimeGraveyardCastCost!.TotalValue.Should().Be(2, "Bears is {1}{G} — mv 2");

        bauble.RuntimeGraveyardCastCost.Should().NotBeNull();
        bauble.RuntimeGraveyardCastCost!.TotalValue.Should().Be(0, "Bauble is {0} — mv 0");

        // The granted cost can be composed with the generic graveyard-cast
        // alt-cost (mirrors the Lurrus plumbing).
        var altCost = new GraveyardCastAlternativeCost(
            description: "Yawgmoth's Will — cast Lightning Bolt from graveyard",
            cost: bolt.RuntimeGraveyardCastCost);
        altCost.CanCastFor(bolt, _alice).Should().BeTrue(
            "Bolt is owned by Alice and in her graveyard — the alt cost is legal.");
    }

    [Fact]
    public void Resolve_DoesNotStamp_OpponentGraveyardCards()
    {
        var aliceCard = new Instant("Bolt", "{R}") { Owner = _alice };
        aliceCard.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(aliceCard);

        var bobCard = new Instant("Bob's Bolt", "{R}") { Owner = _bob };
        bobCard.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(bobCard);

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: null);
        foreach (var e in effects) e.Execute();

        aliceCard.RuntimeGraveyardCastCost.Should().NotBeNull(
            "Alice's graveyard card is granted the cast");
        bobCard.RuntimeGraveyardCastCost.Should().BeNull(
            "Bob's graveyard card is untouched — Yawgmoth's Will only grants 'your graveyard'");
    }

    // -----------------------------------------------------------------------
    // Grave→exile replacement registered on resolve
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_RegistersReplacement_ThatRewritesControllersGraveyardMovesToExile()
    {
        var bus = new ReplacementBus();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        // A subsequent discard of Alice's card hand→graveyard should be
        // rewritten to hand→exile.
        var discarded = new Card("Discarded", "");
        discarded.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: discarded,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Exile,
            "Yawgmoth's Will replaces 'into your graveyard' with 'exile' for this turn");
        result.FromZone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Replacement_AppliesFromAnyZone_NotJustHand()
    {
        var bus = new ReplacementBus();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        // Battlefield→graveyard (creature dying) — should be rewritten too.
        var creature = new Creature("Goyf", "{1}{G}", 4, 5) { Owner = _alice };

        var dyingIntent = new ZoneMoveIntent(
            Card: creature,
            FromZone: ZoneType.Battlefield,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var dyingResult = bus.Apply(dyingIntent);
        dyingResult!.ToZone.Should().Be(ZoneType.Exile,
            "the oracle says 'from anywhere' — battlefield→graveyard is exiled too");

        // Stack→graveyard (resolved instant/sorcery) — should be rewritten.
        var spell = new Instant("Bolt", "{R}") { Owner = _alice };
        var stackIntent = new ZoneMoveIntent(
            Card: spell,
            FromZone: ZoneType.Stack,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var stackResult = bus.Apply(stackIntent);
        stackResult!.ToZone.Should().Be(ZoneType.Exile,
            "Yawgmoth's Will's own post-resolution trip to the graveyard is exiled too");

        // Library→graveyard (mill) — should be rewritten.
        var milled = new Card("Milled", "");
        milled.SetOwner(_alice);
        var millIntent = new ZoneMoveIntent(
            Card: milled,
            FromZone: ZoneType.Library,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var millResult = bus.Apply(millIntent);
        millResult!.ToZone.Should().Be(ZoneType.Exile,
            "milling Alice's library also routes through exile");
    }

    [Fact]
    public void Replacement_DoesNotRewrite_OpponentCardsHittingGraveyard()
    {
        var bus = new ReplacementBus();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        var bobsCard = new Card("Bob's card", "");
        bobsCard.SetOwner(_bob);

        var intent = new ZoneMoveIntent(
            Card: bobsCard,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _bob);

        var result = bus.Apply(intent);

        result.Should().NotBeNull();
        result!.ToZone.Should().Be(ZoneType.Graveyard,
            "Yawgmoth's Will is scoped to 'your graveyard' — Bob's graveyard moves are unaffected");
    }

    [Fact]
    public void Replacement_OnlyApplies_WhenDestinationIsGraveyard()
    {
        var bus = new ReplacementBus();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        // A library→hand draw should NOT be rewritten — only graveyard
        // moves are intercepted.
        var card = new Card("Drawn", "");
        card.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: card,
            FromZone: ZoneType.Library,
            ToZone: ZoneType.Hand,
            Controller: _alice);

        var result = bus.Apply(intent);
        result!.ToZone.Should().Be(ZoneType.Hand,
            "Yawgmoth's Will only intercepts moves whose destination is the graveyard");
    }

    // -----------------------------------------------------------------------
    // EOT cleanup — replacement is EOT-expirable
    // -----------------------------------------------------------------------

    [Fact]
    public void EndOfTurn_Cleanup_RemovesReplacement()
    {
        var bus = new ReplacementBus();

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        // Before EOT sweep — replacement is active.
        var discarded = new Card("Discarded", "");
        discarded.SetOwner(_alice);
        var intentBefore = new ZoneMoveIntent(
            Card: discarded,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);
        bus.Apply(intentBefore)!.ToZone.Should().Be(ZoneType.Exile,
            "Yawgmoth's Will replacement is in effect this turn");

        // EOT cleanup sweep — same sweep TurnDriver runs at cleanup
        // (CR 514.2). Mirrors the per-turn shield-drop path.
        bus.ExpireEndOfTurn();

        // After EOT — the replacement is gone; subsequent grave moves go
        // through unchanged.
        var discarded2 = new Card("Discarded2", "");
        discarded2.SetOwner(_alice);
        var intentAfter = new ZoneMoveIntent(
            Card: discarded2,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);
        bus.Apply(intentAfter)!.ToZone.Should().Be(ZoneType.Graveyard,
            "the EOT sweep dropped the IEndOfTurnExpirable replacement");
    }

    // -----------------------------------------------------------------------
    // Combined: stamped grave-cast survives the same-turn cycle
    // -----------------------------------------------------------------------

    [Fact]
    public void Resolve_StampsGrant_AndReplacementExilesNewGraveyardMoves()
    {
        var bus = new ReplacementBus();

        var existing = new Instant("Bolt", "{R}") { Owner = _alice };
        existing.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(existing);

        var effects = YawgmothsWillFactory.BuildResolveEffect(_alice, replacements: bus);
        foreach (var e in effects) e.Execute();

        existing.RuntimeGraveyardCastCost.Should().NotBeNull(
            "the pre-existing graveyard card got stamped at resolve");

        // A new card that would hit Alice's graveyard mid-turn never settles
        // there — the replacement rewrites the destination to exile, so
        // there's nothing to re-stamp. The replacement covers the "from
        // anywhere this turn" half of the oracle.
        var fresh = new Card("Fresh", "");
        fresh.SetOwner(_alice);

        var intent = new ZoneMoveIntent(
            Card: fresh,
            FromZone: ZoneType.Hand,
            ToZone: ZoneType.Graveyard,
            Controller: _alice);

        var result = bus.Apply(intent);
        result!.ToZone.Should().Be(ZoneType.Exile,
            "any new card heading to Alice's graveyard is exiled instead");
    }
}
