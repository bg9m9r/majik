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
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Karn, the Great Creator (War of the Spark, {4}).
///
/// Covers:
///   - Card identity (Legendary Planeswalker, Karn subtype, loyalty 5,
///     mana cost {4}).
///   - Loyalty ability shape: three abilities at +1 / -2 (plus the printed
///     static, which is NOT a loyalty ability).
///   - Printed static: opponent's artifact activated ability blocked;
///     opponent's mana ability still activatable (CR 605).
///   - +1: target noncreature artifact gets a Layer 4 type-add and a Layer
///     7b BecomesPTEffect / shim at P/T = mv.
///   - -2: selector returns a Wurmcoil from "outside" → goes to hand.
///   - LTB: static suppression lifts.
///   - NamedCardFactory dispatch.
///
/// Shares the <see cref="ActivatedAbilityRestrictionsCollection"/> non-
/// parallel xUnit collection with <see cref="PithingNeedleTests"/> and the
/// rule-engine tests that consult the registry — the registry is
/// process-global, and predicate restrictions (Karn-style) can otherwise
/// leak into concurrently-running tests that touch
/// <see cref="ActionValidator.ValidateActivateAbility"/>.
/// </summary>
[Collection(nameof(ActivatedAbilityRestrictionsCollection))]
public class KarnTheGreatCreatorTests : IDisposable
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);
    private readonly EventBus _bus = new();

    public KarnTheGreatCreatorTests()
    {
        // Defensive — ensure no other test left predicates behind. The
        // Collection attribute already serialises us against the other
        // registry-touching suites, but a constructor-side clear costs
        // nothing and tightens the invariant.
        ActivatedAbilityRestrictions.Clear();
    }

    public void Dispose()
    {
        // Registry is process-global; clear between tests.
        ActivatedAbilityRestrictions.Clear();
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Karn_IsLegendaryPlaneswalker_Karn_5Loyalty_AtCost4()
    {
        var karn = KarnTheGreatCreatorFactory.Create(_alice);

        karn.Name.Should().Be("Karn, the Great Creator");
        karn.ManaCost.Should().Be("{4}");
        karn.HasType(CardType.Planeswalker).Should().BeTrue();
        karn.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        karn.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        karn.Loyalty.Should().Be(5);
        karn.StartingLoyalty.Should().Be(5);
        karn.Owner.Should().BeSameAs(_alice);
        karn.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Karn_HasTwoLoyaltyAbilities_Plus1_Minus2()
    {
        var karn = KarnTheGreatCreatorFactory.Create(_alice);
        var loyaltyAbilities = karn.Abilities.OfType<LoyaltyAbility>().ToList();

        loyaltyAbilities.Should().HaveCount(2);
        loyaltyAbilities.Select(a => a.LoyaltyChange)
            .Should().BeEquivalentTo(new[] { +1, -2 });
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Karn()
    {
        var card = NamedCardFactory.Create("Karn, the Great Creator", _alice);

        card.Should().BeOfType<Planeswalker>();
        card.Name.Should().Be("Karn, the Great Creator");
        card.HasType(CardType.Planeswalker).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        card.HasSubtype(CardSubtype.Karn).Should().BeTrue();
        ((Planeswalker)card).Loyalty.Should().Be(5);
        card.Abilities.OfType<LoyaltyAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Printed static — opponent-artifact activated suppression
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_BlocksOpponentsArtifactActivatedAbility_OnceOnBattlefield()
    {
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: null);

        // Karn enters the battlefield.
        karn.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Hand, ZoneType.Battlefield));

        // Opponent controls Walking Ballista (artifact creature) — try to
        // activate its {X} ping.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var pingAbility = new ActivatedAbility(ballista, _bob);
        var action = new ActivateAbilityAction(pingAbility, _bob);
        var validator = new ActionValidator();

        var result = validator.ValidateAction(action);

        result.IsValid.Should().BeFalse(
            "Karn's static blocks activated abilities of opponent-controlled artifacts");
        result.Violation?.RuleNumber.Should().Be("602.5c");
    }

    [Fact]
    public void Static_DoesNotBlockOpponentManaAbility_Cr605Exemption()
    {
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: null);
        karn.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Hand, ZoneType.Battlefield));

        // Opponent's Sol Ring — a mana-ability artifact. CR 605: mana
        // abilities are NOT "activated abilities" for Karn-style gates.
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_bob);
        solRing.SetController(_bob);
        solRing.SetZone(ZoneType.Battlefield);
        var mana = new ManaAbility(solRing, _bob, ManaCost.Parse("CC"));

        // Mana abilities take a separate activator path (not
        // ActionValidator). The registry's defensive guard is the right
        // thing to assert at the unit level.
        ActivatedAbilityRestrictions.IsActivatedAbilityRestricted(
            new ActivatedAbilityManaShim(solRing, _bob, mana))
            .Should().BeFalse(
                "CR 605 — mana abilities are exempt from Karn's suppression");
    }

    [Fact]
    public void Static_DoesNotBlockOwnArtifactActivatedAbility()
    {
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: null);
        karn.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Hand, ZoneType.Battlefield));

        // Karn's controller (Alice) controls their own Walking Ballista —
        // their own artifact's activated ability is NOT suppressed.
        var ballista = NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ((Card)ballista).SetZone(ZoneType.Battlefield);

        var pingAbility = new ActivatedAbility(ballista, _alice);
        var action = new ActivateAbilityAction(pingAbility, _alice);

        new ActionValidator().ValidateAction(action).IsValid.Should().BeTrue(
            "Karn's static targets opponents' artifacts only");
    }

    [Fact]
    public void Static_LiftsWhenKarnLeavesBattlefield()
    {
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: null);
        karn.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Hand, ZoneType.Battlefield));

        // Sanity — static is in effect.
        var ballista = NamedCardFactory.Create("Walking Ballista", _bob);
        ballista.SetController(_bob);
        ((Card)ballista).SetZone(ZoneType.Battlefield);
        var pingAbility = new ActivatedAbility(ballista, _bob);
        new ActionValidator().ValidateAction(new ActivateAbilityAction(pingAbility, _bob))
            .IsValid.Should().BeFalse();

        // Karn leaves the battlefield.
        karn.SetZone(ZoneType.Graveyard);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Battlefield, ZoneType.Graveyard));

        // Activation legal again.
        new ActionValidator().ValidateAction(new ActivateAbilityAction(pingAbility, _bob))
            .IsValid.Should().BeTrue("LTB removes the predicate registration");
    }

    // -----------------------------------------------------------------------
    // +1 — animate target noncreature artifact
    // -----------------------------------------------------------------------

    [Fact]
    public void Plus1_RegistersTypeAdd_AndPTAtManaValue_ForNoncreatureArtifact()
    {
        // Sol Ring on Alice's battlefield (mana value 1) — noncreature
        // artifact, valid +1 target.
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        solRing.SetController(_alice);
        solRing.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: effects,
            eventBus: _bus,
            battlefieldResolver: () => new[] { (Permanent)solRing },
            wishSelector: null);
        karn.SetZone(ZoneType.Battlefield);
        _bus.Publish(new CardMovedEvent(karn, ZoneType.Hand, ZoneType.Battlefield));

        var plus1 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        karn.Loyalty.Should().Be(6);

        // Layer 4 type-add registered.
        var animate = GetRegisteredEffects(effects)
            .OfType<KarnAnimateArtifactEffect>()
            .SingleOrDefault();
        animate.Should().NotBeNull("the +1 registers the Layer 4 type-add");
        animate!.Target.Should().BeSameAs(solRing);
        animate.Layer.Should().Be(Layer.Type);
        animate.ExpiresAtEndOfTurn.Should().BeTrue();

        // Compute(Sol Ring) — chars.Types now contains Creature via Layer 4.
        var chars = effects.Compute(solRing);
        chars.Types.Should().Contain(CardType.Creature);
        chars.Types.Should().Contain(CardType.Artifact, "Layer 4 ADDS — printed Artifact stays");

        // Layer 7b shim recorded P/T at mana value (Sol Ring's MV = 1).
        var pt = GetRegisteredEffectsByLayer(effects, Layer.PT_SetBase).ToList();
        pt.Should().HaveCount(1);
        // KarnAnimatedShimPTEffect is internal; inspect via reflection
        // since we only need to confirm the recorded values.
        var shim = pt[0];
        var newPower = (int)shim.GetType().GetProperty("NewPower")!.GetValue(shim)!;
        var newToughness = (int)shim.GetType().GetProperty("NewToughness")!.GetValue(shim)!;
        newPower.Should().Be(1);
        newToughness.Should().Be(1);
    }

    [Fact]
    public void Plus1_EndOfTurnExpiration_RemovesAnimateEffects()
    {
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        solRing.SetController(_alice);
        solRing.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: effects,
            eventBus: _bus,
            battlefieldResolver: () => new[] { (Permanent)solRing },
            wishSelector: null);

        var plus1 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        GetRegisteredEffects(effects).OfType<KarnAnimateArtifactEffect>().Should().HaveCount(1);

        // EOT — the +1 effects all carry ExpiresAtEndOfTurn=true; the
        // service drops them. "Until your next turn" is approximated as
        // EOT in v1 (documented in the factory).
        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<KarnAnimateArtifactEffect>().Should().BeEmpty();
        effects.Compute(solRing).Types.Should().NotContain(CardType.Creature);
    }

    [Fact]
    public void Plus1_SkipsCreatureArtifacts_AndArtifactlessPermanents()
    {
        // Walking Ballista is an artifact creature — must NOT be picked.
        var ballista = (Creature)NamedCardFactory.Create("Walking Ballista", _alice);
        ballista.SetController(_alice);
        ballista.SetZone(ZoneType.Battlefield);

        // A non-artifact (Forest land) — must NOT be picked either.
        var forest = (Permanent)NamedCardFactory.Create("Forest", _alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: effects,
            eventBus: _bus,
            battlefieldResolver: () => new Permanent[] { ballista, forest },
            wishSelector: null);

        var plus1 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == +1);
        plus1.Activate();

        karn.Loyalty.Should().Be(6, "loyalty change applies even with no legal target");
        GetRegisteredEffects(effects).OfType<KarnAnimateArtifactEffect>()
            .Should().BeEmpty("+1 is 'up to one' — no legal target = no effects");
    }

    // -----------------------------------------------------------------------
    // -2 — wishboard fetch
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_SelectorReturnsArtifactFromOutsideTheGame_GoesToHand()
    {
        // Wurmcoil Engine sitting "outside the game" — represented as a
        // raw card not in any tracked zone.
        var wurmcoil = (Card)NamedCardFactory.Create("Wurmcoil Engine", _alice);
        // Note: zone is whatever the factory left it as; the -2 path
        // treats anything not in Exile as "outside the game" and just
        // routes it to hand.

        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: _ => wurmcoil);
        karn.AddLoyalty(0); // 5 — enough for -2.

        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.CanActivate().Should().BeTrue();
        minus2.Activate();

        karn.Loyalty.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().Contain(wurmcoil);
        wurmcoil.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Minus2_SelectorReturnsFaceUpExileArtifact_GoesToHand()
    {
        // Sol Ring face-up in Alice's exile.
        var solRing = new Artifact("Sol Ring", "{1}");
        solRing.SetOwner(_alice);
        _alice.Zones.Exile.AddCard(solRing);
        solRing.SetZone(ZoneType.Exile);

        var karn = KarnTheGreatCreatorFactory.Create(
            _alice,
            effects: null,
            eventBus: _bus,
            battlefieldResolver: null,
            wishSelector: _ => solRing);

        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        _alice.Zones.Exile.GetCards().Should().NotContain(solRing);
        _alice.Zones.Hand.GetCards().Should().Contain(solRing);
        solRing.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Minus2_NoSelector_LoyaltyStillDecrements()
    {
        var karn = KarnTheGreatCreatorFactory.Create(_alice);

        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        karn.Loyalty.Should().Be(3, "CR 606.3 — loyalty cost is paid even if body no-ops");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // -2 — wishboard retrofit (WishTutorEffect, no explicit selector)
    // -----------------------------------------------------------------------

    [Fact]
    public void Minus2_NoSelector_PicksArtifactFromWishboard_ToHand()
    {
        // Alice's sideboard / wishboard contains an artifact and a
        // non-artifact. With no explicit selector, the -2 falls through
        // to WishTutorEffect filtered by ArtifactCard → only the artifact
        // is eligible, deterministic first-pick picks it.
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        var solRing = new Artifact("Sol Ring", "{1}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);
        _alice.Wishboard.AddCard(solRing);

        var karn = KarnTheGreatCreatorFactory.Create(_alice);

        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        karn.Loyalty.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().Contain(solRing,
            "-2 wishboard auto-fetches an artifact card from sideboard");
        _alice.Zones.Hand.GetCards().Should().NotContain(bolt,
            "the artifact-only predicate filters Bolt out");
        _alice.Wishboard.GetCards().Should().Contain(bolt);
        _alice.Wishboard.GetCards().Should().NotContain(solRing);
        solRing.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void Minus2_NoSelector_NoArtifactInWishboard_NoOp()
    {
        // Wishboard has only a non-artifact — predicate filters everything
        // out → no-op, but loyalty still decrements (CR 606.3).
        var bolt = new Instant("Lightning Bolt", "{R}") { Owner = _alice };
        _alice.Wishboard.AddCard(bolt);

        var karn = KarnTheGreatCreatorFactory.Create(_alice);

        var minus2 = karn.Abilities.OfType<LoyaltyAbility>()
            .Single(a => a.LoyaltyChange == -2);
        minus2.Activate();

        karn.Loyalty.Should().Be(3);
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Wishboard.GetCards().Should().Contain(bolt);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(ContinuousEffectsService svc)
    {
        // ContinuousEffectsService keeps its effects list private; we read
        // via reflection to keep tests close to the public surface.
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffectsByLayer(
        ContinuousEffectsService svc, Layer layer)
        => GetRegisteredEffects(svc).Where(e => e.Layer == layer);

    /// <summary>
    /// Test-only shim — exposes a mana ability through the
    /// <see cref="IActivatedAbility"/> shape so the registry's defensive
    /// IManaAbility check can be exercised end-to-end.
    /// </summary>
    private sealed class ActivatedAbilityManaShim : ActivatedAbility, IManaAbility
    {
        private readonly IManaAbility _inner;

        public ActivatedAbilityManaShim(ICard source, Player controller, IManaAbility inner)
            : base(source, controller)
        {
            _inner = inner;
        }

        object IManaAbility.Source => _inner.Source;
        Player IManaAbility.Controller => _inner.Controller;
        ManaCost IManaAbility.ManaGenerated => _inner.ManaGenerated;
        bool IManaAbility.CanActivate() => _inner.CanActivate();
        ManaCost IManaAbility.Activate() => _inner.Activate();
    }
}
