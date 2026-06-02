using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkithiryxTheBlightDragonFactory"/>
/// (Mirrodin Besieged, {3}{B}{B}).
///
/// Legendary Creature — Skeleton Dragon 4/4. Oracle text:
///   "Flying.
///    Haste.
///    Infect
///    {B}: Regenerate Skithiryx, the Blight Dragon."
///
/// Covers:
///   - Identity (name, cost, P/T, Legendary, subtypes
///     Skeleton / Dragon, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flying + Haste + Infect keyword markers.
///   - {B}: Regenerate activated ability shape + resolve adds a
///     <see cref="Permanent.AddRegenerationShield"/> shield.
/// </summary>
[Trait("Color", "B")]
public class SkithiryxTheBlightDragonFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Skithiryx_Identity()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);

        c.Name.Should().Be("Skithiryx, the Blight Dragon");
        c.ManaCost.Should().Be("{3}{B}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue(
            "Skithiryx is Legendary");
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Skithiryx_HasFlyingHasteInfectKeywordMarkers()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain(k =>
            string.Equals(k, "Flying", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.9 — Flying keyword marker is wired");
        keywords.Should().Contain(k =>
            string.Equals(k, "Haste", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.10 — Haste keyword marker is wired");
        keywords.Should().Contain(k =>
            string.Equals(k, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(3,
            "Flying + Haste + Infect — three keyword markers");
    }

    [Fact]
    public void Skithiryx_HasExactlyOneRegenerateActivatedAbility()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "Skithiryx prints one activated ability: {B}: regenerate self");
    }

    [Fact]
    public void Skithiryx_RegenerateActivatedAbility_CostIsSingleBlack()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        var regen = c.Abilities.OfType<ActivatedAbility>().Single();

        regen.Costs.Should().HaveCount(1);
        var manaCost = regen.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Black.Should().Be(1);
        manaCost.Cost.Generic.Should().Be(0);
    }

    [Fact]
    public void Skithiryx_RegenerateAbility_Resolve_AddsRegenerationShield()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        c.HasRegenerationShield.Should().BeFalse();
        c.RegenerationShieldCount.Should().Be(0);

        var regen = c.Abilities.OfType<ActivatedAbility>().Single();
        regen.Resolve();

        c.HasRegenerationShield.Should().BeTrue();
        c.RegenerationShieldCount.Should().Be(1);
    }
}
