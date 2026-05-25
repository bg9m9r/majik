using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SpectralFlightFactory"/>.
///
/// Card: Spectral Flight — Enchantment — Aura {1}{U} (Magic 2014).
///   "Enchant creature"
///   "Enchanted creature gets +2/+2 and has flying."
///
/// Covers:
///   - Identity / dispatch.
///   - Aura subtype.
///   - +2/+2 boost via AttachedBoostEffect (Layer 7c).
///   - Flying granted to the enchanted creature.
///   - Boost is inert when the aura is unattached.
///   - Build-spell-definition emits a creature-only target predicate.
/// </summary>
public class SpectralFlightTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SpectralFlight_Identity()
    {
        var c = SpectralFlightFactory.Create(_alice);

        c.Name.Should().Be("Spectral Flight");
        c.ManaCost.Should().Be("{1}{U}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.HasSubtype(CardSubtype.Aura).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SpectralFlight()
    {
        var card = NamedCardFactory.Create("Spectral Flight", _alice);

        card.Should().BeOfType<Enchantment>();
        card.Name.Should().Be("Spectral Flight");
        card.HasSubtype(CardSubtype.Aura).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Static boost — +2/+2 + Flying
    // -----------------------------------------------------------------------

    [Fact]
    public void Static_PlusTwoPlusTwo_AppliesToAttachedCreature()
    {
        var effects = new ContinuousEffectsService();
        var aura = SpectralFlightFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Power.Should().Be(4, "2 + 2 = 4");
        chars.Toughness.Should().Be(4, "2 + 2 = 4");
    }

    [Fact]
    public void Static_GrantsFlying()
    {
        var effects = new ContinuousEffectsService();
        var aura = SpectralFlightFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        aura.AttachTo(bear);

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Static_Inert_WhileUnattached()
    {
        var effects = new ContinuousEffectsService();
        var aura = SpectralFlightFactory.Create(_alice, effects);
        PlaceOnBattlefield(aura, _alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        // Don't attach.
        var chars = effects.Compute(bear);
        chars.Power.Should().Be(2);
        chars.Toughness.Should().Be(2);
        chars.Keywords.Should().NotContain("Flying");
    }

    // -----------------------------------------------------------------------
    // Spell definition — target predicate filters to creatures
    // -----------------------------------------------------------------------

    [Fact]
    public void BuildSpellDefinition_FiltersCreaturesOnly()
    {
        var aura = SpectralFlightFactory.Create(_alice);

        var bear = new Creature("Bear", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var land = new Land("Plains");
        land.SetOwner(_alice);
        land.SetController(_alice);

        var battlefield = new Permanent[] { bear, land };
        var def = SpectralFlightFactory.BuildSpellDefinition(aura, battlefield);

        def.TargetRequests.Should().HaveCount(1);
        var candidates = def.TargetRequests[0].LegalCandidates.Cast<Permanent>().ToList();

        candidates.Should().Contain(bear);
        candidates.Should().NotContain(land);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static void PlaceOnBattlefield(Enchantment aura, Player owner)
    {
        aura.SetOwner(owner);
        aura.SetController(owner);
        owner.Zones.Battlefield.AddCard(aura);
        aura.SetZone(ZoneType.Battlefield);
    }
}
