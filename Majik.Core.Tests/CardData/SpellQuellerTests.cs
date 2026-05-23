using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Spell Queller (Eldritch Moon, {1}{W}{U}).
///
/// Covers:
///   - Identity (name, types, subtype, P/T, mana cost) + NamedCardFactory dispatch.
///   - Flash keyword presence.
///   - Two triggered abilities surfaced (ETB exile + LTB release).
///   - ETB exiles a mv ≤ 4 spell from the stack into its owner's exile zone.
///   - ETB target with mv = 5 is illegal — effect no-ops, spell stays on stack.
///   - LTB fires when Spell Queller leaves the battlefield and exposes the
///     exiled card to the host callback; the released card is castable via
///     <see cref="CastFromExileAlternativeCost"/> by its original owner.
/// </summary>
public class SpellQuellerTests
{
    private readonly EventBus _bus = new();
    private readonly Majik.Core.Stack.Stack _stack;
    private readonly ZoneService _zones;
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public SpellQuellerTests()
    {
        _stack = new Majik.Core.Stack.Stack(_bus);
        _zones = new ZoneService(_bus);
    }

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellQueller_Identity()
    {
        var queller = SpellQuellerFactory.Create(_alice);

        queller.Name.Should().Be("Spell Queller");
        queller.ManaCost.Should().Be("{1}{W}{U}");
        queller.HasType(CardType.Creature).Should().BeTrue();
        queller.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        queller.BasePower.Should().Be(2);
        queller.BaseToughness.Should().Be(3);
        queller.Owner.Should().BeSameAs(_alice);
        queller.Controller.Should().BeSameAs(_alice);

        // Flash + ETB + LTB.
        queller.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flash");
        queller.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile target spell + LTB release exiled card");
    }

    [Fact]
    public void SpellQueller_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Spell Queller", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Spell Queller");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(2);
        ((Creature)card).BaseToughness.Should().Be(3);
        card.Owner.Should().Be(_alice);

        card.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).Should().Contain("Flash");
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2);
    }

    // -----------------------------------------------------------------------
    // ETB exile — CR 603.6a / CR 701.21
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellQueller_Etb_ExilesManaValue4Spell()
    {
        var queller = SpellQuellerFactory.Create(
            _alice, stack: _stack, triggers: null, onExiledCardReleased: null);
        _alice.Zones.Battlefield.AddCard(queller);
        queller.SetZone(ZoneType.Battlefield);

        // Bob casts a mv-4 sorcery onto the stack.
        var cruise = new Sorcery("Cruel Ultimatum", "{1}{U}{B}{R}");
        cruise.SetOwner(_bob);
        cruise.SetController(_bob);
        cruise.SetZone(ZoneType.Stack);
        var cruiseSpell = new Majik.Core.Spells.Spell(cruise, _bob);
        _stack.Push(cruiseSpell);

        // Use a mv-4 spell — CardData/Factories uses mv = printed cost
        // total. {1}{U}{B}{R} totals 4 — within Queller's MaxTargetManaValue.
        cruise.ManaCostValue.TotalValue.Should().Be(4);

        // Drive the ETB target manually — Snapcaster-pattern.
        var etb = queller.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.TargetRequests[0].Description.Should().Contain("mana value 4 or less");
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { cruiseSpell },
        });

        foreach (var e in etb.Effects) e.Execute();

        cruise.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted spell's card (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(cruise);
        _stack.GetAll().Should().NotContain(cruiseSpell,
            "the targeted spell is removed from the stack");
    }

    [Fact]
    public void SpellQueller_Etb_IllegalTarget_ManaValue5_NoOp()
    {
        // CR 608.2b — at resolution time, a target with mv > 4 is illegal.
        // The effect does nothing; the spell stays on the stack.
        var queller = SpellQuellerFactory.Create(
            _alice, stack: _stack, triggers: null, onExiledCardReleased: null);
        _alice.Zones.Battlefield.AddCard(queller);
        queller.SetZone(ZoneType.Battlefield);

        var fiveDrop = new Sorcery("Mind's Desire", "{4}{U}");
        fiveDrop.SetOwner(_bob);
        fiveDrop.SetController(_bob);
        fiveDrop.SetZone(ZoneType.Stack);
        var fiveSpell = new Majik.Core.Spells.Spell(fiveDrop, _bob);
        _stack.Push(fiveSpell);

        fiveDrop.ManaCostValue.TotalValue.Should().Be(5);

        var etb = queller.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { fiveSpell },
        });

        foreach (var e in etb.Effects) e.Execute();

        fiveDrop.Zone.Should().Be(ZoneType.Stack,
            "mv-5 target is illegal — effect no-ops, spell stays on stack");
        _stack.GetAll().Should().Contain(fiveSpell);
        _bob.Zones.Exile.GetCards().Should().NotContain(fiveDrop);
    }

    [Fact]
    public void SpellQueller_Etb_TargetRequest_Shape()
    {
        var queller = SpellQuellerFactory.Create(_alice);

        var etb = queller.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);

        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers only fire while on the battlefield (CR 603.6a)");

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("spell");
        req.Description.Should().Contain("mana value 4 or less");
    }

    // -----------------------------------------------------------------------
    // LTB release — CR 603.6c / CR 702.85a-style free cast
    // -----------------------------------------------------------------------

    [Fact]
    public void SpellQueller_Ltb_ReleasesExiledCard_OwnerCanCastForFree()
    {
        // Setup: Queller resolves, ETB exiles Bob's mv-3 spell. Then the
        // Queller leaves the battlefield → LTB callback fires with the
        // exiled card; we then confirm a CastFromExileAlternativeCost is
        // legal for Bob (the exiled card's owner) to use.
        ICard? released = null;
        var queller = SpellQuellerFactory.Create(
            _alice, stack: _stack, triggers: null,
            onExiledCardReleased: c => released = c);
        _alice.Zones.Battlefield.AddCard(queller);
        queller.SetZone(ZoneType.Battlefield);

        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_bob);
        bolt.SetController(_bob);
        bolt.SetZone(ZoneType.Stack);
        var boltSpell = new Majik.Core.Spells.Spell(bolt, _bob);
        _stack.Push(boltSpell);

        // Run ETB.
        var etb = queller.Abilities.OfType<TriggeredAbility>()
            .First(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { boltSpell },
        });
        foreach (var e in etb.Effects) e.Execute();
        bolt.Zone.Should().Be(ZoneType.Exile);

        // Spell Queller leaves the battlefield (e.g. dies to removal). The
        // LTB effect reads the captured exiled card and invokes the
        // callback; this is the host's cue to drive Bob's free cast.
        var ltb = queller.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        released.Should().BeSameAs(bolt,
            "LTB releases the exiled card so its owner may cast it for free");

        // CR 702.85a-style free cast — the cast cost gates on
        //   (1) the card being in exile, AND
        //   (2) caster being the card's owner.
        // Bob (Bolt's owner) qualifies; Alice (Queller's controller) does not.
        var freeCost = SpellQuellerFactory.BuildFreeCastCost();
        freeCost.CanCastFor(bolt, _bob).Should().BeTrue(
            "the exiled card's owner can cast it without paying its mana cost (CR 702.85a)");
        freeCost.CanCastFor(bolt, _alice).Should().BeFalse(
            "only the original owner may cast — not Queller's controller");
    }

    [Fact]
    public void SpellQueller_Ltb_NoExiledCard_CallbackNotInvoked()
    {
        // If the ETB never landed an exile (e.g. illegal target, no spell
        // on stack), the LTB has nothing to release — the callback is not
        // invoked.
        var called = false;
        var queller = SpellQuellerFactory.Create(
            _alice, stack: _stack, triggers: null,
            onExiledCardReleased: _ => called = true);
        _alice.Zones.Battlefield.AddCard(queller);
        queller.SetZone(ZoneType.Battlefield);

        var ltb = queller.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        called.Should().BeFalse(
            "no exile recorded by ETB → LTB no-ops, original-owner free-cast does not fire");
    }
}
