using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpiritMantleFactory"/>.
///
/// Card: Spirit Mantle — Enchantment — Aura {1}{W} (New Phyrexia).
///   "Enchant creature"
///   "Enchanted creature gets +1/+1 and has protection from creatures."
///
/// Covers:
///   - Identity / dispatch (Enchantment — Aura, {1}{W}).
///   - Fixed +1/+1 boost on the enchanted creature (CR 613 Layer 7c).
///   - Protection-from-creatures grant (CR 702.16) — marker on the card in
///     the shape-only path; re-projected onto the live enchanted creature
///     when a ContinuousEffectsService is wired.
///   - Boost is inert while unattached.
///   - "Enchant creature" cast-time target predicate filters non-creatures.
/// </summary>
public class SpiritMantleFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void SpiritMantle_Identity()
    {
        var c = SpiritMantleFactory.Create(_alice);

        c.Name.Should().Be("Spirit Mantle");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpiritMantle()
    {
        var card = NamedCardFactory.Create("Spirit Mantle", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Spirit Mantle");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static +1/+1 boost
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_Boost_GrantsPlusOnePlusOne_WhileAttached()
    {
        var effects = new ContinuousEffectsService();
        var mantle = SpiritMantleFactory.Create(_alice, effects);
        PlaceOnBattlefield(mantle, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        mantle.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2 + 1, "+1/+1 from Spirit Mantle");
        chars.Toughness.Should().Be(2 + 1);
    }

    [Fact]
    public void Static_Boost_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var mantle = SpiritMantleFactory.Create(_alice, effects);
        PlaceOnBattlefield(mantle, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        // Don't attach.

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Protection from creatures (CR 702.16)
    // -----------------------------------------------------------------------

    [Fact]
    public void ShapeOnly_CarriesProtectionFromCreaturesMarker()
    {
        var mantle = SpiritMantleFactory.Create(_alice);

        var qualities = mantle.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();

        qualities.Should().BeEquivalentTo(new[] { "creatures" },
            "Spirit Mantle carries a protection-from-creatures marker in the shape-only path");

        Protection.HasProtectionFromCardType(mantle, CardType.Creature).Should().BeTrue(
            "the 'creatures' marker is visible to Protection helpers");
        Protection.HasProtectionFromCardType(mantle, CardType.Artifact).Should().BeFalse(
            "no protection-from-artifacts marker is attached");
    }

    [Fact]
    public void Wired_GrantsProtectionFromCreatures_OntoEnchantedCreature()
    {
        var effects = new ContinuousEffectsService();
        var mantle = SpiritMantleFactory.Create(_alice, effects);
        PlaceOnBattlefield(mantle, _alice);

        var bear = NewCreatureOnBattlefield("Bear");
        bear.ActiveEffects = effects;
        mantle.AttachTo(bear);

        // Sync the layer system so the Layer-6 grant projects onto the bear.
        effects.Compute(bear);

        Protection.HasProtectionFromCardType(bear, CardType.Creature).Should().BeTrue(
            "the enchanted creature gains protection from creatures (CR 702.16)");
        // Marker now lives on the bearer, not the Mantle (CR 702.16e reads
        // the enchanted creature).
        mantle.Abilities.OfType<ProtectionAbility>().Should().BeEmpty(
            "the grant moves the marker onto the enchanted creature when a service is wired");
    }

    // -----------------------------------------------------------------------
    // "Enchant creature" target predicate
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersToCreatures()
    {
        var mantle = SpiritMantleFactory.Create(_alice);

        var bear = NewCreatureOnBattlefield("Bear");
        var land = new Land("Plains");
        var pacifism = new Enchantment("Pacifism", "{1}{W}",
            supertypes: null, subtypes: new[] { CardSubtype.Aura });

        var battlefield = new Permanent[] { bear, land, pacifism };
        var def = SpiritMantleFactory.BuildSpellDefinition(mantle, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
        candidates.Should().NotContain(pacifism);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Creature NewCreatureOnBattlefield(string name)
    {
        var c = new Creature(name, "{1}{G}", 2, 2);
        c.SetOwner(_alice);
        c.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static void PlaceOnBattlefield(Enchantment mantle, Player owner)
    {
        owner.Zones.Battlefield.AddCard(mantle);
        mantle.SetZone(ZoneType.Battlefield);
    }
}
