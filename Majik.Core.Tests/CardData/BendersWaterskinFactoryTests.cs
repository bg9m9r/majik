using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="BendersWaterskinFactory"/> (Avatar: The Last
/// Airbender, {3}).
///
/// Oracle text (Scryfall, verified 2026):
///   "Untap this artifact during each other player's untap step.
///    {T}: Add one mana of any color."
///
/// Covers the card's UNIQUE behaviour:
///   - Identity ({3} Artifact — non-vanilla mana cost).
///   - Five any-colour mana abilities ({T}: Add one mana of any color).
///   - The "untap during each other player's untap step" static registers an
///     extra-untap rider while on the battlefield and exposes the Waterskin to
///     a non-controller's untap step (CR 502.1 + the printed static).
///   - The rider lifts when the Waterskin leaves the battlefield (LTB).
///
/// (NamedCardFactory dispatch + well-formedness are asserted for every
/// implemented card by CardFactoryContractTests — no dispatch test here.)
/// </summary>
[Trait("Color", "C")]
public class BendersWaterskinFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BendersWaterskin_Identity()
    {
        var skin = BendersWaterskinFactory.Create(_alice);

        skin.Name.Should().Be("Bender's Waterskin");
        skin.ManaCost.Should().Be("{3}");
        skin.HasType(CardType.Artifact).Should().BeTrue();
        skin.Owner.Should().BeSameAs(_alice);
        skin.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BendersWaterskin_HasFiveAnyColorManaAbilities()
    {
        var skin = BendersWaterskinFactory.Create(_alice);

        // "{T}: Add one mana of any color" — modeled as five WUBRG mana
        // abilities (CR 605.1, CR 106.6), the same any-colour shape as
        // Arcane Signet.
        skin.Abilities.OfType<ManaAbility>().Should().HaveCount(5,
            "{T}: Add one mana of any color = one ManaAbility per WUBRG colour");
    }

    [Fact]
    public void BendersWaterskin_UntapStatic_ExposesToOtherPlayersUntapStep()
    {
        // Per-flow isolated store so the ambient extra-untap registry doesn't
        // leak across tests.
        using var _ = UntapStepRestrictions.PushScope();
        var bus = new EventBus();

        var skin = BendersWaterskinFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(skin);
        skin.SetZone(ZoneType.Battlefield);
        // The lifecycle binder re-syncs on CardMovedEvent; emit one so the
        // rider registers now that the Waterskin is on the battlefield.
        bus.Publish(new CardMovedEvent(skin, ZoneType.Stack, ZoneType.Battlefield));

        skin.Tap();
        skin.IsTapped.Should().BeTrue();

        // CR 502.1 + the printed static: during Bob's (the OTHER player's)
        // untap step, the Waterskin is an extra untap...
        UntapStepRestrictions.ExtraUntapsDuring(_bob).Should().Contain(skin,
            "the Waterskin untaps during each other player's untap step");

        // ...but NOT during its own controller's untap step (that is the
        // normal pass, already handled by the standard untap rule).
        UntapStepRestrictions.ExtraUntapsDuring(_alice).Should().NotContain(skin,
            "the controller's own untap step is the normal pass, not an extra untap");
    }

    [Fact]
    public void BendersWaterskin_UntapStatic_LiftsWhenLeavingBattlefield()
    {
        using var _ = UntapStepRestrictions.PushScope();
        var bus = new EventBus();

        var skin = BendersWaterskinFactory.Create(_alice, bus);
        _alice.Zones.Battlefield.AddCard(skin);
        skin.SetZone(ZoneType.Battlefield);
        bus.Publish(new CardMovedEvent(skin, ZoneType.Stack, ZoneType.Battlefield));
        skin.Tap();

        UntapStepRestrictions.ExtraUntapsDuring(_bob).Should().Contain(skin);

        // Leave the battlefield — the rider must lift (CR 603.6e analogue for
        // statics: the effect only applies while the source is on the field).
        _alice.Zones.Battlefield.RemoveCard(skin);
        skin.SetZone(ZoneType.Graveyard);
        bus.Publish(new CardMovedEvent(skin, ZoneType.Battlefield, ZoneType.Graveyard));

        UntapStepRestrictions.ExtraUntapsDuring(_bob).Should().NotContain(skin,
            "the extra-untap rider lifts once the Waterskin leaves the battlefield");
    }
}
