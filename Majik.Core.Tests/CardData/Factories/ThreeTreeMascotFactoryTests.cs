using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="ThreeTreeMascotFactory"/>.
///
/// Three Tree Mascot (Bloomburrow, {2}). Artifact Creature — Shapeshifter
/// 2/1. Oracle text (verified against Scryfall):
///   "Changeling (This card is every creature type.)
///    {1}: Add one mana of any color. Activate only once each turn."
///
/// Covers the card's UNIQUE behaviour:
/// - Identity (Artifact Creature — Shapeshifter, {2}, 2/1).
/// - Changeling (CR 702.73) — stamped as every creature type.
/// - The "{1}: Add one mana of any color" any-colour fan-out (five no-tap
///   ManaAbility slots, one per WUBRG) and the {1} mana cost / no-tap shape.
/// - "Activate only once each turn" (CR 602.5e) — a SINGLE per-turn lock
///   shared across all five colour slots; resets at turn start (CR 500.1).
/// </summary>
[Trait("Color", "C")]
public class ThreeTreeMascotFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void ThreeTreeMascot_Identity()
    {
        var c = ThreeTreeMascotFactory.Create(_alice);

        c.Name.Should().Be("Three Tree Mascot");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue("it is an Artifact Creature");
        c.HasSubtype(CardSubtype.Shapeshifter).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void ThreeTreeMascot_HasChangeling_IsEveryCreatureType()
    {
        // CR 702.73a — Changeling: the card is every creature type. The
        // engine's currently-modelled subtype set is stamped on the body, so
        // tribal-lord predicates (HasSubtype(Goblin), HasSubtype(Elf), …) all
        // return true. Also carries a Changeling keyword marker.
        var c = ThreeTreeMascotFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Changeling");
        c.HasSubtype(CardSubtype.Goblin).Should().BeTrue("changeling is every creature type");
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue("changeling is every creature type");
    }

    [Fact]
    public void ThreeTreeMascot_HasFiveManaAbilities_OnePerColor()
    {
        // "{1}: Add one mana of any color." modeled as five ManaAbility
        // instances (one per WUBRG), same any-colour fan-out as Shimmering
        // Grotto / Mana Confluence.
        var c = ThreeTreeMascotFactory.Create(_alice);

        c.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void ThreeTreeMascot_ManaAbilitiesCoverEveryColor()
    {
        var c = ThreeTreeMascotFactory.Create(_alice);

        var manaStrings = c.Abilities.OfType<ManaAbility>()
            .Select(a => a.ManaGenerated?.ToString())
            .OrderBy(s => s)
            .ToList();

        manaStrings.Should().BeEquivalentTo(new[] { "B", "G", "R", "U", "W" },
            "Three Tree Mascot adds one mana of any color.");
    }

    [Fact]
    public void ThreeTreeMascot_ActivatingManaAbility_PaysOneGeneric_AddsColorMana_DoesNotTap()
    {
        // {1}: Add one mana of any color. The {1} is paid from the controller's
        // pool; the source does NOT tap (no {T} in the printed cost).
        var c = ThreeTreeMascotFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // Seed {1} so the activation can pay its cost.
        _alice.AddManaToPool(ManaCost.Parse("1"));

        var green = c.Abilities.OfType<ManaAbility>()
            .First(a => a.ManaGenerated?.ToString() == "G");

        green.CanActivate().Should().BeTrue("the {1} is available and the gate is open");

        var produced = green.Activate();

        produced.ToString().Should().Be("G", "the ability adds one mana of the chosen colour");
        c.IsTapped.Should().BeFalse(
            "the ability has no {T} component — Three Tree Mascot stays untapped");
        // ManaAbility.Activate() pays the {1} additional cost from the pool but
        // RETURNS the produced mana (the ManaAbilityActivator deposits it in the
        // pool in the live engine). So after the raw Activate() the pool reflects
        // only the spent {1}.
        _alice.ManaPool.Total.Should().Be(0,
            "the {1} cost was paid out of the seeded pool");
    }

    [Fact]
    public void ThreeTreeMascot_WithoutOneGeneric_CannotActivate()
    {
        // CR 602.5e activation legality also requires the {1} cost to be
        // payable. With an empty pool the ability can't be activated.
        var c = ThreeTreeMascotFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        var any = c.Abilities.OfType<ManaAbility>().First();

        any.CanActivate().Should().BeFalse("no {1} in the pool to pay the cost");
    }

    [Fact]
    public void ThreeTreeMascot_OncePerTurn_SecondActivationSameTurnIsBlocked_AcrossAllColorSlots()
    {
        // CR 602.5e — "Activate only once each turn." The single per-turn lock
        // is shared across all five colour slots: activating one colour blocks
        // EVERY colour for the rest of the turn (it is one printed ability with
        // five modes, not five independent abilities).
        var c = ThreeTreeMascotFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        // Seed enough generic to pay two activations if the gate let us.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var slots = c.Abilities.OfType<ManaAbility>().ToList();
        var white = slots.First(a => a.ManaGenerated?.ToString() == "W");
        var black = slots.First(a => a.ManaGenerated?.ToString() == "B");

        white.Activate();

        white.CanActivate().Should().BeFalse(
            "CR 602.5e — the activated colour mode is locked for the turn");
        black.CanActivate().Should().BeFalse(
            "the once-per-turn lock is SHARED — a different colour mode is " +
            "also blocked after any colour was used this turn");
    }

    [Fact]
    public void ThreeTreeMascot_OnNewTurn_ActivationGateResets()
    {
        // CR 500.1 — turn start. The bus-aware overload subscribes a
        // TurnStartedEvent handler that resets the shared per-turn lock.
        var bus = new EventBus();
        var c = ThreeTreeMascotFactory.Create(_alice, bus);
        c.SetZone(ZoneType.Battlefield);

        _alice.AddManaToPool(ManaCost.Parse("2"));

        var red = c.Abilities.OfType<ManaAbility>()
            .First(a => a.ManaGenerated?.ToString() == "R");

        red.Activate();
        red.CanActivate().Should().BeFalse("first use is locked this turn");

        bus.Publish(new TurnStartedEvent(_alice, turnNumber: 2));

        red.CanActivate().Should().BeTrue(
            "the TurnStartedEvent reset handler re-opens the gate at the " +
            "start of the next turn (the {1} from the leftover pool remains)");
    }
}
