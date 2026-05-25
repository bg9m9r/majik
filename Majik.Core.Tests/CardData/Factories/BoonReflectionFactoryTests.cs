using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BoonReflectionFactory"/>.
///
/// Card: Boon Reflection — Enchantment {4}{W} (Tenth Edition).
///   "If you would gain life, you gain twice that much life instead."
///
/// Covers:
///   - Identity (name, type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Single-arg shape path: no replacement registered.
///   - Asymmetric life-gain doubling: controller's gains double; opponent's
///     gains unaffected.
///   - Battlefield gate: doubling only fires while the card is on the
///     battlefield.
///   - Stacking: two copies of Boon Reflection quadruple a single gain.
/// </summary>
public class BoonReflectionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity + dispatch ─────────────────────────────────────────────

    [Fact]
    public void BoonReflection_Identity()
    {
        var c = BoonReflectionFactory.Create(_alice);

        c.Name.Should().Be("Boon Reflection");
        c.ManaCost.Should().Be("{4}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BoonReflection_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Boon Reflection", _alice);

        c.Should().BeOfType<Enchantment>();
        c.Name.Should().Be("Boon Reflection");
        c.HasType(CardType.Enchantment).Should().BeTrue();
    }

    [Fact]
    public void BoonReflection_SingleArgPath_DoesNotRegisterReplacement()
    {
        var bus = new ReplacementBus();
        _ = BoonReflectionFactory.Create(_alice);

        var intent = new LifeGainIntent(_alice, 3);
        bus.Apply(intent)!.Amount.Should().Be(3,
            "single-arg dispatcher path never registers on the bus");
    }

    // ── Life-gain doubling ──────────────────────────────────────────────

    [Fact]
    public void BoonReflection_DoublesControllersLifeGain_OnBattlefield()
    {
        var bus = new ReplacementBus();
        var card = BoonReflectionFactory.Create(_alice, bus);
        card.SetZone(ZoneType.Battlefield);

        var intent = new LifeGainIntent(_alice, 3);
        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(6, "Boon Reflection doubles Alice's life gain (3 → 6)");
    }

    [Fact]
    public void BoonReflection_DoesNotDoubleOpponentsLifeGain()
    {
        var bus = new ReplacementBus();
        var card = BoonReflectionFactory.Create(_alice, bus);
        card.SetZone(ZoneType.Battlefield);

        var intent = new LifeGainIntent(_bob, 3);
        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(3,
            "Boon Reflection is asymmetric — only the controller's gains double");
    }

    [Fact]
    public void BoonReflection_OffBattlefield_DoesNotDouble()
    {
        // Card defaults to ZoneType.Library (or whatever the Card ctor sets).
        // It's not on the battlefield, so the predicate refuses.
        var bus = new ReplacementBus();
        var card = BoonReflectionFactory.Create(_alice, bus);
        card.SetZone(ZoneType.Graveyard);

        var intent = new LifeGainIntent(_alice, 3);
        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(3,
            "the doubling clause is gated on Boon Reflection being on the battlefield");
    }

    [Fact]
    public void BoonReflection_ZeroAmountGain_ShortCircuited()
    {
        // Defensive — zero / negative intents are filtered by the applies
        // predicate so the rewrite never inflates 0 → 0 redundantly. Real-
        // world relevance: Roiling Vortex stacked above the doubling
        // returns Amount=0 first; downstream doublers must not turn that
        // back into a nonzero gain.
        var bus = new ReplacementBus();
        var card = BoonReflectionFactory.Create(_alice, bus);
        card.SetZone(ZoneType.Battlefield);

        var intent = new LifeGainIntent(_alice, 0);
        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(0);
    }

    [Fact]
    public void BoonReflection_StacksWithSecondCopy_Quadruples()
    {
        // Two Boon Reflections on the battlefield → 3 → 6 → 12 (per-effect
        // dedup tag is the card instance; each card fires once per intent
        // via the standard apply-each-eligible-replacement loop).
        var bus = new ReplacementBus();
        var first = BoonReflectionFactory.Create(_alice, bus);
        first.SetZone(ZoneType.Battlefield);
        var second = BoonReflectionFactory.Create(_alice, bus);
        second.SetZone(ZoneType.Battlefield);

        var intent = new LifeGainIntent(_alice, 3);
        var replaced = bus.Apply(intent);

        replaced!.Amount.Should().Be(12, "two Boon Reflections quadruple the gain");
    }

    [Fact]
    public void BoonReflection_RoutesThroughPlayerGainLife_WhenBusAttached()
    {
        // Integration: Player.GainLife → ReplacementBus → doubled amount
        // committed to the life total. Confirms the wiring at the Player
        // boundary (Player.AttachReplacementBus + Replacements!.Apply call
        // in Player.GainLife) actually picks up the registered effect.
        var bus = new ReplacementBus();
        _alice.AttachReplacementBus(bus);
        var card = BoonReflectionFactory.Create(_alice, bus);
        card.SetZone(ZoneType.Battlefield);

        var before = _alice.LifeTotal;
        _alice.GainLife(3);
        var after = _alice.LifeTotal;

        (after - before).Should().Be(6, "Boon Reflection doubled the 3 → 6 gain at the player boundary");
    }
}
