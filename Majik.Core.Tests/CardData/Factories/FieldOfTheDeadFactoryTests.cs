using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="FieldOfTheDeadFactory"/>.
///
/// Covers:
///   - Identity (Land, no subtype, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Pure helper <see cref="FieldOfTheDeadFactory.CountDistinctlyNamedLands"/>
///     (counts duplicates once, ignores non-lands).
///   - ETB-tapped replacement when <see cref="ReplacementBus"/> wired.
///   - Land-ETB trigger fires with ≥7 distinctly-named lands controlled.
///   - Land-ETB trigger does NOT fire with &lt;7 distinctly-named lands.
///   - Trigger does NOT fire on opponent's land ETBs.
/// </summary>
[Trait("Color", "C")]
public class FieldOfTheDeadFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Field_Identity_LandWithTapMana_AndLandEtbTrigger()
    {
        var f = FieldOfTheDeadFactory.Create(_alice);

        f.Name.Should().Be("Field of the Dead");
        f.HasType(CardType.Land).Should().BeTrue();
        f.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        f.Owner.Should().BeSameAs(_alice);
        f.Controller.Should().BeSameAs(_alice);

        // {T}: Add {C} + the land-ETB intervening-if trigger.
        f.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        f.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    // -----------------------------------------------------------------------
    // Pure helper
    // -----------------------------------------------------------------------

    [Fact]
    public void CountDistinctlyNamedLands_CountsDistinctNamesOnly()
    {
        AddLand(_alice, "Mountain", basic: true, CardSubtype.Mountain);
        AddLand(_alice, "Mountain", basic: true, CardSubtype.Mountain); // duplicate
        AddLand(_alice, "Forest", basic: true, CardSubtype.Forest);
        AddLand(_alice, "Island", basic: true, CardSubtype.Island);

        FieldOfTheDeadFactory.CountDistinctlyNamedLands(_alice).Should().Be(3);
    }

    [Fact]
    public void CountDistinctlyNamedLands_IgnoresNonLandCardsOnBattlefield()
    {
        AddLand(_alice, "Plains", basic: true, CardSubtype.Plains);

        // Drop a creature on the battlefield to confirm it's filtered.
        var ogre = new Creature("Ogre", "{2}{R}", 3, 3);
        ogre.SetOwner(_alice);
        ogre.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(ogre);
        ogre.SetZone(ZoneType.Battlefield);

        FieldOfTheDeadFactory.CountDistinctlyNamedLands(_alice).Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // ETB-tapped replacement (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void Field_EtbTapped_WiredViaReplacementBus()
    {
        var (zones, _, _, replacements) = BuildEngine();

        var field = FieldOfTheDeadFactory.Create(_alice, replacements, triggers: null, zones: null);
        _alice.Zones.Hand.AddCard(field);
        field.SetZone(ZoneType.Hand);

        zones.MoveCardTo(field, ZoneType.Battlefield, controller: _alice);

        field.Zone.Should().Be(ZoneType.Battlefield);
        field.IsTapped.Should().BeTrue(
            "Field of the Dead enters tapped unconditionally (CR 614.1c)");
    }

    // -----------------------------------------------------------------------
    // Triggered ability (CR 603.1 / 603.4 — intervening-if)
    // -----------------------------------------------------------------------

    [Fact]
    public void LandEtbWithSevenDistinctlyNamedLands_TriggerFires()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Seed 6 distinct-name lands so the entering 7th tips the
        // intervening-if to true (≥7 distinctly-named lands).
        AddLand(_alice, "Mountain", basic: true, CardSubtype.Mountain);
        AddLand(_alice, "Forest", basic: true, CardSubtype.Forest);
        AddLand(_alice, "Island", basic: true, CardSubtype.Island);
        AddLand(_alice, "Plains", basic: true, CardSubtype.Plains);
        AddLand(_alice, "Swamp", basic: true, CardSubtype.Swamp);
        AddLand(_alice, "Wastes", basic: true, CardSubtype.Wastes);

        var field = FieldOfTheDeadFactory.Create(_alice, replacements, triggers, zones);
        _alice.Zones.Hand.AddCard(field);
        field.SetZone(ZoneType.Hand);

        // Play Field via ZoneService — its own ETB drives the trigger
        // ("this land or another land"). At event-publish time the
        // battlefield includes Field, taking distinct names to 7.
        zones.MoveCardTo(field, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "Field's own ETB with 7 distinct-name lands (including itself) satisfies the intervening-if");
    }

    [Fact]
    public void LandEtbWithFewerThanSevenDistinctlyNamedLands_TriggerDoesNotFire()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // 5 distinct names — Field will be the 6th, still below the
        // threshold.
        AddLand(_alice, "Mountain", basic: true, CardSubtype.Mountain);
        AddLand(_alice, "Forest", basic: true, CardSubtype.Forest);
        AddLand(_alice, "Island", basic: true, CardSubtype.Island);
        AddLand(_alice, "Plains", basic: true, CardSubtype.Plains);
        AddLand(_alice, "Swamp", basic: true, CardSubtype.Swamp);

        var field = FieldOfTheDeadFactory.Create(_alice, replacements, triggers, zones);
        _alice.Zones.Hand.AddCard(field);
        field.SetZone(ZoneType.Hand);

        zones.MoveCardTo(field, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "only 6 distinct names (5 + Field) — intervening-if fails");
    }

    [Fact]
    public void OpponentLandEtb_DoesNotFireFieldTrigger()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Alice has Field on the battlefield with 7+ distinct-name lands.
        for (int i = 0; i < 7; i++)
        {
            AddLand(_alice, $"Unique Land {i}", basic: false, CardSubtype.Forest);
        }

        var field = FieldOfTheDeadFactory.Create(_alice, replacements, triggers, zones);
        field.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(field);
        field.SetZone(ZoneType.Battlefield);

        // Bob plays his own Mountain — not under Alice's control, so
        // Field's trigger doesn't fire (the predicate is gated on
        // controller = owner).
        var bobMountain = new Land("Mountain",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        bobMountain.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobMountain);
        bobMountain.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobMountain, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "Field's trigger is scoped to lands entering under ITS controller");
    }

    [Fact]
    public void DuplicateNamesDontTipThreshold()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // 6 Mountains (all share a name) + Field → distinct-name count is
        // only 2 (Mountain + Field), threshold not met.
        for (int i = 0; i < 6; i++)
        {
            AddLand(_alice, "Mountain", basic: true, CardSubtype.Mountain);
        }

        var field = FieldOfTheDeadFactory.Create(_alice, replacements, triggers, zones);
        _alice.Zones.Hand.AddCard(field);
        field.SetZone(ZoneType.Hand);

        zones.MoveCardTo(field, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "all 6 Mountains share a name — distinct count is 2 (Mountain + Field), below threshold");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void AddLand(Player p, string name, bool basic, CardSubtype subtype)
    {
        var supertypes = basic ? new[] { CardSupertype.Basic } : Array.Empty<CardSupertype>();
        var land = new Land(name, supertypes, new[] { subtype });
        land.SetOwner(p);
        land.SetController(p);
        p.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
