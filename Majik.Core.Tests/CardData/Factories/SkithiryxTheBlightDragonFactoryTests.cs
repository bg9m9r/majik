using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SkithiryxTheBlightDragonFactory"/>
/// (Mirrodin Besieged, {3}{B}{B}).
///
/// Legendary Creature — Phyrexian Dragon Skeleton 4/4. Current oracle text
/// (verified against Scryfall):
///   "Flying.
///    Infect (This creature deals damage to creatures in the form of
///    -1/-1 counters and to players in the form of poison counters.)
///    {B}: Skithiryx, the Blight Dragon gains haste until end of turn.
///    {B}{B}: Regenerate Skithiryx, the Blight Dragon."
///
/// Covers:
///   - Identity (name, cost, P/T, Legendary, subtypes
///     Phyrexian / Skeleton / Dragon, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Flying + Infect keyword markers (Haste is NOT a static keyword on
///     the current printing — it is granted by an activated ability).
///   - {B}: gains haste until EOT activated ability shape + resolve
///     registers a <see cref="GrantKeywordUntilEndOfTurnEffect"/>.
///   - {B}{B}: Regenerate activated ability shape + resolve adds a
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
        c.HasSubtype(CardSubtype.Phyrexian).Should().BeTrue();
        c.HasSubtype(CardSubtype.Skeleton).Should().BeTrue();
        c.HasSubtype(CardSubtype.Dragon).Should().BeTrue();
        c.BasePower.Should().Be(4);
        c.BaseToughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Skithiryx_HasFlyingAndInfectKeywordMarkers_NoStaticHaste()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword)
            .ToList();

        keywords.Should().Contain(k =>
            string.Equals(k, "Flying", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.9 — Flying keyword marker is wired");
        keywords.Should().Contain(k =>
            string.Equals(k, "Infect", System.StringComparison.OrdinalIgnoreCase),
            "CR 702.90 — Infect keyword marker is wired (mechanic deferred)");

        keywords.Should().NotContain(k =>
            string.Equals(k, "Haste", System.StringComparison.OrdinalIgnoreCase),
            "current printing has NO static Haste — it is granted by {B} activated ability");

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(2,
            "Flying + Infect — two static keyword markers on the current printing");
    }

    [Fact]
    public void Skithiryx_HasExactlyTwoActivatedAbilities()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);

        c.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "current printing: {B}: gains haste until EOT + {B}{B}: regenerate self");
    }

    [Fact]
    public void Skithiryx_RegenerateActivatedAbility_CostIsDoubleBlack()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e => e.Description.Contains("regenerate",
                System.StringComparison.OrdinalIgnoreCase)));

        regen.Costs.Should().HaveCount(1);
        var manaCost = regen.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Black.Should().Be(2,
            "current printing regenerates for {B}{B}, not the old {B}");
        manaCost.Cost.Generic.Should().Be(0);
    }

    [Fact]
    public void Skithiryx_HasteActivatedAbility_CostIsSingleBlack()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        var haste = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e => e.Description.Contains("haste",
                System.StringComparison.OrdinalIgnoreCase)));

        haste.Costs.Should().HaveCount(1);
        var manaCost = haste.Costs[0].Should().BeOfType<ManaCostCost>().Subject;
        manaCost.Cost.Black.Should().Be(1, "{B}: gains haste until end of turn");
        manaCost.Cost.Generic.Should().Be(0);
    }

    [Fact]
    public void Skithiryx_RegenerateAbility_Resolve_AddsRegenerationShield()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);

        c.HasRegenerationShield.Should().BeFalse();
        c.RegenerationShieldCount.Should().Be(0);

        var regen = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e => e.Description.Contains("regenerate",
                System.StringComparison.OrdinalIgnoreCase)));
        regen.Resolve();

        c.HasRegenerationShield.Should().BeTrue();
        c.RegenerationShieldCount.Should().Be(1);
    }

    [Fact]
    public void Skithiryx_HasteAbility_Resolve_GrantsHasteUntilEndOfTurn()
    {
        var c = SkithiryxTheBlightDragonFactory.Create(_alice);
        c.SetZone(ZoneType.Battlefield);
        c.ActiveEffects = new ContinuousEffectsService();

        c.HasEffectiveKeyword("Haste").Should().BeFalse(
            "no static Haste on the current printing");

        var haste = c.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Effects.Any(e => e.Description.Contains("haste",
                System.StringComparison.OrdinalIgnoreCase)));
        haste.Resolve();

        c.HasEffectiveKeyword("Haste").Should().BeTrue(
            "CR 613.1f Layer 6 — {B} grants Haste until end of turn");
    }
}
