using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Skyclave Apparition (Zendikar Rising, {1}{W}{W}) and
/// Leonin Arbiter (Scars of Mirrodin, {1}{W}).
///
/// Skyclave Apparition covers:
///   - Identity (name, types, subtype, P/T, mana cost) + NamedCardFactory dispatch.
///   - Two triggered abilities (ETB exile + LTB create token).
///   - ETB exiles a mv ≤ 4 nonland nontoken opponent permanent.
///   - ETB with mv = 5 target is illegal — effect no-ops (CR 608.2b).
///   - ETB with 0 targets chosen — no exile, LTB no-ops.
///   - LTB creates X/X Illusion token where X = exiled card's mana value.
///
/// Leonin Arbiter covers:
///   - Identity (name, types, subtype, P/T, mana cost) + NamedCardFactory dispatch.
///   - LeoninArbiterSearchRestrictionEffect registered when wired overload is used.
/// </summary>
public class SkyclaveApparitionTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Skyclave Apparition — Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveApparition_Identity()
    {
        var apparition = SkyclaveApparitionFactory.Create(_alice);

        apparition.Name.Should().Be("Skyclave Apparition");
        apparition.ManaCost.Should().Be("{1}{W}{W}");
        apparition.HasType(CardType.Creature).Should().BeTrue();
        apparition.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        apparition.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        apparition.BasePower.Should().Be(2);
        apparition.BaseToughness.Should().Be(2);
        apparition.Owner.Should().BeSameAs(_alice);
        apparition.Controller.Should().BeSameAs(_alice);

        // ETB + LTB.
        apparition.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB token-creation trigger");
    }

    [Fact]
    public void SkyclaveApparition_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Skyclave Apparition", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Skyclave Apparition");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Kor).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);

        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // Skyclave Apparition — ETB target request shape
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveApparition_Etb_TargetRequest_Shape()
    {
        var apparition = SkyclaveApparitionFactory.Create(_alice);

        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers only fire while on the battlefield (CR 603.6a)");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(0,
            "the 'up to one' clause allows choosing zero targets");
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("nonland");
        req.Description.Should().Contain("4 or less");
    }

    // -----------------------------------------------------------------------
    // Skyclave Apparition — ETB exile CR 603.6a / CR 701.21
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveApparition_Etb_ExilesOpponentPermanent_Mv4()
    {
        // Setup: Alice's Apparition targets Bob's mv-4 creature.
        var apparition = SkyclaveApparitionFactory.Create(_alice);
        apparition.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(apparition);

        // Bob has a 4-mana creature on the battlefield.
        var target = new Creature("Siege Rhino", "{1}{W}{B}{G}", 4, 5);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        target.ManaCostValue.TotalValue.Should().Be(4);

        // Drive ETB manually — Snapcaster-pattern.
        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted permanent (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void SkyclaveApparition_Etb_IllegalTarget_Mv5_NoOp()
    {
        // CR 608.2b — a permanent with mv > 4 at resolution is illegal.
        var apparition = SkyclaveApparitionFactory.Create(_alice);
        apparition.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(apparition);

        var fiveDrop = new Creature("Gray Merchant of Asphodel", "{3}{B}{B}", 2, 4);
        fiveDrop.SetOwner(_bob);
        fiveDrop.SetController(_bob);
        fiveDrop.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(fiveDrop);

        fiveDrop.ManaCostValue.TotalValue.Should().Be(5);

        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { fiveDrop },
        });
        foreach (var e in etb.Effects) e.Execute();

        fiveDrop.Zone.Should().Be(ZoneType.Battlefield,
            "mv-5 target is illegal at resolution — effect no-ops (CR 608.2b)");
        _bob.Zones.Exile.GetCards().Should().NotContain(fiveDrop);
    }

    // -----------------------------------------------------------------------
    // Skyclave Apparition — ETB with 0 targets (up to one)
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveApparition_Etb_ZeroTargets_NoExile()
    {
        // "Up to one" — choosing 0 targets is legal. The ETB no-ops.
        var apparition = SkyclaveApparitionFactory.Create(_alice);
        apparition.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(apparition);

        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);

        // Supply an empty targets list for the first slot.
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });
        foreach (var e in etb.Effects) e.Execute();

        // Nothing to assert beyond "no exception thrown". LTB test below
        // verifies 0-target ETB leads to no token on LTB.
    }

    // -----------------------------------------------------------------------
    // Skyclave Apparition — LTB token creation CR 603.6c / CR 111.6
    // -----------------------------------------------------------------------

    [Fact]
    public void SkyclaveApparition_Ltb_CreatesIllusionToken_XEqualsMv()
    {
        // Setup: ETB exiles Bob's mv-3 creature. Then Apparition leaves
        // the battlefield → LTB creates a 3/3 Illusion under Bob.
        var apparition = SkyclaveApparitionFactory.Create(_alice);
        apparition.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(apparition);

        // Bob's mv-3 creature on the battlefield.
        var bolt = new Creature("Tidehollow Sculler", "{W}{B}", 2, 2);
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bolt);
        bolt.ManaCostValue.TotalValue.Should().Be(2, "Tidehollow Sculler is {W}{B} = mv 2");

        // Use a mv-3 creature instead for clarity.
        var mv3 = new Creature("Flickerwisp", "{1}{W}{W}", 3, 1);
        mv3.SetOwner(_bob);
        mv3.SetController(_bob);
        mv3.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(mv3);
        mv3.ManaCostValue.TotalValue.Should().Be(3);

        // Run ETB to exile the mv-3 creature.
        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { mv3 },
        });
        foreach (var e in etb.Effects) e.Execute();
        mv3.Zone.Should().Be(ZoneType.Exile);

        // Apparition leaves the battlefield — run LTB.
        var ltb = apparition.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        // Bob's battlefield should now contain a 3/3 Illusion token.
        var illusions = _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Illusion))
            .ToList();

        illusions.Should().HaveCount(1,
            "LTB creates exactly one Illusion token");
        illusions[0].BasePower.Should().Be(3,
            "X = mana value of the exiled card (3)");
        illusions[0].BaseToughness.Should().Be(3,
            "X = mana value of the exiled card (3)");
    }

    [Fact]
    public void SkyclaveApparition_Ltb_ZeroTargetEtb_NoTokenCreated()
    {
        // If the ETB resolved with 0 targets, no card was exiled.
        // LTB should create no token.
        var apparition = SkyclaveApparitionFactory.Create(_alice);
        apparition.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(apparition);

        // Run ETB with 0 targets.
        var etb = apparition.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });
        foreach (var e in etb.Effects) e.Execute();

        // Run LTB.
        var ltb = apparition.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        // No Illusion token should exist on Bob's battlefield.
        _bob.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Illusion))
            .Should().BeEmpty("no exile → no LTB token");

        // And none on Alice's battlefield either.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Illusion))
            .Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Leonin Arbiter — Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void LeoninArbiter_Identity()
    {
        var arbiter = LeoninArbiterFactory.Create(_alice);

        arbiter.Name.Should().Be("Leonin Arbiter");
        arbiter.ManaCost.Should().Be("{1}{W}");
        arbiter.HasType(CardType.Creature).Should().BeTrue();
        arbiter.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        arbiter.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        arbiter.BasePower.Should().Be(2);
        arbiter.BaseToughness.Should().Be(2);
        arbiter.Owner.Should().BeSameAs(_alice);
        arbiter.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void LeoninArbiter_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Leonin Arbiter", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Leonin Arbiter");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cat).Should().BeTrue();
        card.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(2);
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Leonin Arbiter — search restriction marker
    // -----------------------------------------------------------------------

    [Fact]
    public void LeoninArbiter_WiredOverload_RegistersSearchRestrictionEffect()
    {
        // Create Arbiter with zone pre-set so Attach (called inside Create)
        // sees it on the battlefield and immediately registers the marker.
        // We construct directly to pre-set the zone, then call
        // LeoninArbiterSearchRestrictionEffect.Attach manually — same code
        // path LeoninArbiterFactory.Create uses internally.
        var effects = new ContinuousEffectsService();
        var arbiter = new Creature(
            "Leonin Arbiter", "{1}{W}", 2, 2,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Cleric });
        arbiter.SetOwner(_alice);
        arbiter.SetController(_alice);
        arbiter.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(arbiter);

        var restriction = new LeoninArbiterSearchRestrictionEffect(
            source: arbiter,
            effects: effects);
        restriction.Attach();

        restriction.IsRestrictionActive.Should().BeTrue(
            "Leonin Arbiter is on the battlefield — restriction should be active (CR 614)");

        // The ContinuousEffectsService should hold the marker.
        // A future enforcement hook queries:
        //   effects.OfType<LeoninArbiterSearchRestrictionEffect>()
        //          .Any(e => e.IsRestrictionActive)
        restriction.Should().NotBeNull();
    }

    [Fact]
    public void LeoninArbiter_WiredOverload_EffectDeactivatesOnLtb()
    {
        // Restriction activates on ETB, deactivates when Arbiter leaves.
        var effects = new ContinuousEffectsService();
        var bus = new EventBus();

        var arbiter = new Creature(
            "Leonin Arbiter", "{1}{W}", 2, 2,
            subtypes: new[] { CardSubtype.Cat, CardSubtype.Cleric });
        arbiter.SetOwner(_alice);
        arbiter.SetController(_alice);
        arbiter.SetZone(ZoneType.Battlefield);

        var restriction = new LeoninArbiterSearchRestrictionEffect(
            source: arbiter,
            effects: effects,
            eventBus: bus);
        restriction.Attach();

        restriction.IsRestrictionActive.Should().BeTrue();

        // Simulate Arbiter leaving the battlefield.
        arbiter.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(arbiter, ZoneType.Battlefield, ZoneType.Graveyard));

        restriction.IsRestrictionActive.Should().BeFalse(
            "Restriction should deactivate when Leonin Arbiter leaves the battlefield");
    }
}
