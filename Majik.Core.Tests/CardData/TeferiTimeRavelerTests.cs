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
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Teferi, Time Raveler — Legendary Planeswalker {1}{W}{U},
/// loyalty 4 (CR 117.1a printed static + CR 606 loyalty abilities).
///
/// Covers:
/// - Card identity / subtype / loyalty / dispatcher routing.
/// - The printed-static sorcery-speed restriction wired via
///   <see cref="SorcerySpeedRestrictionEffect"/> +
///   <see cref="CastingRestrictions"/> and observed through
///   <see cref="ActionValidator"/>.
/// - The -3 bounce + draw loyalty ability.
/// - <see cref="Emblem"/>-with-static infrastructure (the same lifecycle
///   binder works when attached to an emblem so future PW-ultimate emblems
///   can adopt this pattern).
///
/// Tests dispose-clean the static <see cref="CastingRestrictions"/>
/// registry to prevent cross-test leakage.
/// </summary>
public class TeferiTimeRavelerTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();
    private readonly ContinuousEffectsService _effects = new();
    private readonly ZoneService _zones;

    public TeferiTimeRavelerTests()
    {
        _zones = new ZoneService(_bus);
        CastingRestrictions.Clear();
    }

    public void Dispose()
    {
        CastingRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Card identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Teferi_HasCorrectIdentity_AndLoyalty4_AndTeferiSubtype()
    {
        var teferi = TeferiTimeRavelerFactory.Create(_alice);

        teferi.Name.Should().Be("Teferi, Time Raveler");
        teferi.ManaCost.Should().Be("{1}{W}{U}");
        teferi.HasType(CardType.Planeswalker).Should().BeTrue();
        teferi.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        teferi.HasSubtype(CardSubtype.Teferi).Should().BeTrue();
        teferi.StartingLoyalty.Should().Be(4);
        teferi.Loyalty.Should().Be(4);
        teferi.Owner.Should().BeSameAs(_alice);
        teferi.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_RoutesTeferiTimeRaveler_ToFactory()
    {
        var card = NamedCardFactory.Create("Teferi, Time Raveler", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Teferi, Time Raveler");
        ((Planeswalker)card).StartingLoyalty.Should().Be(4);
        card.HasSubtype(CardSubtype.Teferi).Should().BeTrue();
    }

    [Fact]
    public void Teferi_HasPlus1AndMinus3_LoyaltyAbilities()
    {
        var teferi = TeferiTimeRavelerFactory.Create(_alice);

        var loyaltyAbilities = teferi.Abilities.OfType<LoyaltyAbility>().ToList();
        loyaltyAbilities.Should().HaveCount(2);
        loyaltyAbilities.Should().Contain(la => la.LoyaltyChange == +1);
        loyaltyAbilities.Should().Contain(la => la.LoyaltyChange == -3);
    }

    // -----------------------------------------------------------------------
    // Printed static — CR 117.1a sorcery-speed restriction
    // -----------------------------------------------------------------------

    [Fact]
    public void TeferiOnBattlefield_RestrictsOpponentToSorcerySpeed_BlocksOpponentInstantOutsideMainPhase()
    {
        // Wire Teferi with the runtime-effects path so the printed static
        // attaches. Opponents = { Bob }.
        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            targetResolver: null,
            effects: _effects,
            eventBus: _bus);

        // Move Teferi onto the battlefield so the lifecycle picks it up.
        _alice.Zones.Library.AddCard(teferi);
        teferi.SetZone(ZoneType.Library);
        _zones.MoveCard(teferi, ZoneType.Library, ZoneType.Battlefield);

        // Bob tries to cast Lightning Bolt (Instant) at instant speed on
        // Alice's turn — normally legal (instants ignore SorcerySpeedAvailable),
        // but Teferi's restriction forces sorcery speed.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: false);
        var result = new ActionValidator().ValidateAction(action);

        result.IsValid.Should().BeFalse();
        result.ErrorMessage.Should().Contain("sorcery speed");
        result.Violation!.RuleNumber.Should().Be("117.1a");
    }

    [Fact]
    public void TeferiOnBattlefield_AllowsOpponent_WhenSorcerySpeedIsAvailable()
    {
        // Same setup, but action is taken during a window where sorcery
        // speed IS legal for Bob (his own main phase, empty stack). Even
        // a Sorcery is fine, and so is the restriction-target's own
        // sorcery-speed cast.
        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            targetResolver: null,
            effects: _effects,
            eventBus: _bus);
        _alice.Zones.Library.AddCard(teferi);
        teferi.SetZone(ZoneType.Library);
        _zones.MoveCard(teferi, ZoneType.Library, ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var sorcerySpeedAction = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: true);
        new ActionValidator().ValidateAction(sorcerySpeedAction).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TeferiOnBattlefield_DoesNotRestrictTeferiController()
    {
        // CR 117.1a — restriction targets each *opponent*, not Alice.
        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            targetResolver: null,
            effects: _effects,
            eventBus: _bus);
        _alice.Zones.Library.AddCard(teferi);
        teferi.SetZone(ZoneType.Library);
        _zones.MoveCard(teferi, ZoneType.Library, ZoneType.Battlefield);

        // Alice casts an instant at instant speed on Bob's turn — fine.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _alice };
        var action = new CastSpellAction(bolt, _alice, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    [Fact]
    public void TeferiLeavingBattlefield_ReleasesRestriction()
    {
        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: () => new[] { _bob },
            targetResolver: null,
            effects: _effects,
            eventBus: _bus);
        _alice.Zones.Library.AddCard(teferi);
        teferi.SetZone(ZoneType.Library);
        _zones.MoveCard(teferi, ZoneType.Library, ZoneType.Battlefield);

        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeTrue();

        // Teferi dies → restriction lifts.
        _zones.MoveCard(teferi, ZoneType.Battlefield, ZoneType.Graveyard);

        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeFalse();

        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // -3: bounce target artifact/creature/enchantment + draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void TeferiMinus3_ReturnsTargetCreatureToOwnersHand_AndControllerDrawsACard()
    {
        // Bob has Grizzly Bears on the battlefield; Alice has a card in
        // her library to draw.
        var bears = new Creature("Grizzly Bears", "1G", 2, 2);
        bears.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(bears);
        bears.SetZone(ZoneType.Battlefield);
        bears.SetController(_bob);

        var deckTop = new Creature("Top of Deck", "G", 1, 1);
        deckTop.SetOwner(_alice);
        _alice.Zones.Library.AddCard(deckTop);
        deckTop.SetZone(ZoneType.Library);

        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: null,
            targetResolver: () => new[] { (Permanent)bears },
            effects: null,
            eventBus: null);

        // Activate the -3 directly.
        var minus3 = teferi.Abilities.OfType<LoyaltyAbility>()
            .Single(la => la.LoyaltyChange == -3);
        minus3.Activate();

        // Bears bounced to Bob's hand.
        _bob.Zones.Hand.GetCards().Should().Contain(bears);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bears);
        bears.Zone.Should().Be(ZoneType.Hand);

        // Alice drew a card.
        _alice.Zones.Hand.GetCards().Should().Contain(deckTop);
        _alice.Zones.Library.GetCards().Should().NotContain(deckTop);

        // Loyalty change applied.
        teferi.Loyalty.Should().Be(1); // 4 - 3
    }

    [Fact]
    public void TeferiMinus3_OnlyBouncesArtifactsCreaturesOrEnchantments()
    {
        // Resolver returns a Land first — should be skipped — then a
        // Creature. Verifies the IsBounceTarget gate.
        var land = new Land("Forest", new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        land.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
        land.SetController(_bob);

        var creature = new Creature("Bear", "1G", 2, 2);
        creature.SetOwner(_bob);
        _bob.Zones.Battlefield.AddCard(creature);
        creature.SetZone(ZoneType.Battlefield);
        creature.SetController(_bob);

        var teferi = TeferiTimeRavelerFactory.Create(
            _alice,
            opponentResolver: null,
            targetResolver: () => new Permanent[] { land, creature },
            effects: null,
            eventBus: null);

        teferi.Abilities.OfType<LoyaltyAbility>().Single(la => la.LoyaltyChange == -3).Activate();

        // Land stays put.
        _bob.Zones.Battlefield.GetCards().Should().Contain(land);
        // Creature bounced.
        _bob.Zones.Hand.GetCards().Should().Contain(creature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(creature);
    }

    // -----------------------------------------------------------------------
    // Emblem-with-static infrastructure: same lifecycle wired onto an emblem
    // -----------------------------------------------------------------------

    [Fact]
    public void Emblem_CanCarry_SorcerySpeedRestrictionEffect_ForRestOfGame()
    {
        // Build an emblem with the sorcery-speed-restriction lifecycle.
        // Source is null (emblems aren't permanents) — activeWhile = always.
        var emblem = new Emblem(_alice, "Test Emblem (sorcery-only)", Array.Empty<IAbility>());

        var lifecycle = new SorcerySpeedRestrictionEffect(
            source: null,
            eventBus: _bus,
            affectedPlayersResolver: () => new[] { _bob },
            activeWhile: () => true);
        emblem.AddEffect(lifecycle);
        lifecycle.Attach();
        _alice.AddEmblem(emblem);

        // Restriction is live.
        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeTrue();
        emblem.Effects.Should().HaveCount(1);

        // Validator agrees: Bob's instant-speed cast is rejected.
        var bolt = new Instant("Lightning Bolt", "R") { Owner = _bob };
        var action = new CastSpellAction(bolt, _bob, sorcerySpeedAvailable: false);
        new ActionValidator().ValidateAction(action).IsValid.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // CastingRestrictions registry — direct unit-level coverage
    // -----------------------------------------------------------------------

    [Fact]
    public void CastingRestrictions_AddAndRemove_TogglesRestriction()
    {
        var token = new object();
        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeFalse();

        CastingRestrictions.AddSorcerySpeedRestriction(token, _bob);
        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeTrue();

        // Idempotent add for same (token, player).
        CastingRestrictions.AddSorcerySpeedRestriction(token, _bob);
        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeTrue();

        CastingRestrictions.RemoveSorcerySpeedRestriction(token);
        CastingRestrictions.MustCastAtSorcerySpeed(_bob).Should().BeFalse();
    }
}
