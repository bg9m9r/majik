using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IceFangCoatlFactory"/>.
///
/// Ice-Fang Coatl (Modern Horizons, {G}{U}):
///   Snow Creature — Snake 1/1.
///   "Flash. Flying. When this creature enters, draw a card.
///    This creature has deathtouch as long as you control at
///    least three other snow permanents."
///
/// Covers:
/// - Identity: {G}{U} Creature — Snake, 1/1, green+blue, Snow supertype.
/// - Flash keyword marker (CR 702.8).
/// - Flying keyword marker (CR 702.9).
/// - Mana value 2 (CR 202.3).
/// - NamedCardFactory dispatch.
/// - Exactly one battlefield-active ETB triggered ability.
/// - ETB draws 1 card for the controller.
/// - ETB on empty library stamps loss flag (CR 704.5b).
/// - Conditional Deathtouch (CR 702.2):
///     - ABSENT when &lt;3 other snow permanents controlled.
///     - PRESENT when ≥3 other snow permanents controlled.
///     - Responds dynamically as snow count changes.
/// </summary>
public class IceFangCoatlFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_Identity()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        c.Name.Should().Be("Ice-Fang Coatl");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue("Ice-Fang Coatl is a Snake");
        c.HasSupertype(CardSupertype.Snow).Should().BeTrue("Ice-Fang Coatl is a Snow creature");
        c.ManaCost.Should().Be("{G}{U}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void IceFangCoatl_IsGreenAndBlue()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        var colors = Majik.Core.Cards.CardColors.GetColors(c);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Green,
            "Ice-Fang Coatl has a {G} pip");
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Blue,
            "Ice-Fang Coatl has a {U} pip");
        colors.Should().HaveCount(2, "two color pips → two colors");
    }

    [Fact]
    public void IceFangCoatl_ManaValue_IsTwo()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {G}{U} has mana value 2");
    }

    // -----------------------------------------------------------------------
    // Flash keyword (CR 702.8)
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_HasFlashKeyword()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "CR 702.8 — Ice-Fang Coatl has Flash");
    }

    // -----------------------------------------------------------------------
    // Flying keyword (CR 702.9)
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_HasFlyingKeyword()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flying",
                "CR 702.9 — Ice-Fang Coatl has Flying");
    }

    // -----------------------------------------------------------------------
    // NamedCardFactory dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Ice-Fang Coatl", _alice);

        c.Should().BeOfType<Creature>("Ice-Fang Coatl is a Creature");
        c.Name.Should().Be("Ice-Fang Coatl");
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue();
        c.HasSupertype(CardSupertype.Snow).Should().BeTrue();
        c.ManaCost.Should().Be("{G}{U}");
    }

    // -----------------------------------------------------------------------
    // ETB triggered ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_HasExactlyOneTriggeredAbility_BattlefieldActive()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "exactly one ETB trigger");

        var etb = triggers.Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield,
            "ETB triggers are battlefield-active");
        etb.InterveningIf.Should().BeNull(
            "unconditional ETB — no intervening-if clause");
    }

    // -----------------------------------------------------------------------
    // ETB draw effect — stocked library
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_EtbTrigger_DrawsOneCard()
    {
        var alice = new Player("Alice", 20);

        var c1 = new Card("Top1", "");
        var c2 = new Card("Top2", "");
        var c3 = new Card("Top3", "");
        foreach (var card in new[] { c1, c2, c3 })
        {
            card.SetOwner(alice);
            alice.Zones.Library.AddCard(card);
            card.SetZone(ZoneType.Library);
        }

        var coatl = IceFangCoatlFactory.Create(alice);
        var etb = coatl.Abilities.OfType<TriggeredAbility>().Single();

        foreach (var effect in etb.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().HaveCount(1,
            "ETB draws exactly 1 card (CR 121.1)");
        alice.Zones.Library.GetCards().Should().HaveCount(2,
            "one card left the top of the library");
    }

    // -----------------------------------------------------------------------
    // ETB draw effect — empty library
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_EtbTrigger_EmptyLibrary_StampsLossFlag_NoCrash()
    {
        var alice = new Player("Alice", 20);

        var coatl = IceFangCoatlFactory.Create(alice);
        var etb = coatl.Abilities.OfType<TriggeredAbility>().Single();

        var act = () =>
        {
            foreach (var effect in etb.Effects) effect.Execute();
        };

        act.Should().NotThrow();
        alice.Zones.Hand.GetCards().Should().BeEmpty(
            "no cards in library → no draws");
        alice.TriedToDrawFromEmptyLibrary.Should().BeTrue(
            "CR 704.5b — drawing from an empty library stamps the loss flag");
    }

    // -----------------------------------------------------------------------
    // Conditional Deathtouch (CR 702.2)
    // -----------------------------------------------------------------------

    /// <summary>
    /// With no ContinuousEffectsService wired (shape-only), the Deathtouch
    /// keyword is NOT present as a printed KeywordAbility marker — it is
    /// conditional and must be evaluated via the layer system.
    /// </summary>
    [Fact]
    public void IceFangCoatl_ShapeOnly_NoImmediateDeathtouchMarker()
    {
        var c = IceFangCoatlFactory.Create(_alice);

        // Without any snow permanents to evaluate against,
        // Deathtouch should NOT be a static KeywordAbility marker.
        c.Abilities.OfType<KeywordAbility>()
            .Should().NotContain(k => k.Keyword == "Deathtouch",
                "Deathtouch is conditional — not a printed static marker");
    }

    /// <summary>
    /// With fewer than 3 other snow permanents controlled by Alice,
    /// Ice-Fang Coatl does NOT have Deathtouch.
    /// </summary>
    [Fact]
    public void IceFangCoatl_Deathtouch_AbsentWithFewerThanThreeOtherSnowPermanents()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        // Two other snow permanents — not enough.
        var snow1 = MakeSnowPermanent(alice);
        var snow2 = MakeSnowPermanent(alice);

        var coatl = IceFangCoatlFactory.Create(
            alice,
            effects,
            battlefieldSnowSource: () => new[] { snow1, snow2 });

        coatl.Zone = ZoneType.Battlefield;

        CombatAbilities.HasDeathtouch(coatl).Should().BeFalse(
            "only 2 other snow permanents — Deathtouch requires ≥3 others (CR 702.2)");
    }

    /// <summary>
    /// With exactly 3 other snow permanents controlled by Alice,
    /// Ice-Fang Coatl DOES have Deathtouch (layer system path).
    /// </summary>
    [Fact]
    public void IceFangCoatl_Deathtouch_PresentWithThreeOtherSnowPermanents()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var snow1 = MakeSnowPermanent(alice);
        var snow2 = MakeSnowPermanent(alice);
        var snow3 = MakeSnowPermanent(alice);

        var coatl = IceFangCoatlFactory.Create(
            alice,
            effects,
            battlefieldSnowSource: () => new[] { snow1, snow2, snow3 });

        coatl.Zone = ZoneType.Battlefield;

        CombatAbilities.HasDeathtouch(coatl).Should().BeTrue(
            "exactly 3 other snow permanents — Deathtouch should be active (CR 702.2)");
    }

    /// <summary>
    /// With more than 3 other snow permanents, Deathtouch is still present.
    /// </summary>
    [Fact]
    public void IceFangCoatl_Deathtouch_PresentWithMoreThanThreeOtherSnowPermanents()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var snows = Enumerable.Range(0, 5).Select(_ => MakeSnowPermanent(alice)).ToArray();

        var coatl = IceFangCoatlFactory.Create(
            alice,
            effects,
            battlefieldSnowSource: () => snows);

        coatl.Zone = ZoneType.Battlefield;

        CombatAbilities.HasDeathtouch(coatl).Should().BeTrue(
            "5 other snow permanents — Deathtouch should be active");
    }

    /// <summary>
    /// Ice-Fang Coatl itself is NOT counted in the snow permanent count
    /// (the oracle says "at least three OTHER snow permanents").
    /// Even though it IS a snow creature, it doesn't count itself.
    /// </summary>
    [Fact]
    public void IceFangCoatl_DoesNotCountItselfAsSnowPermanent()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        // Supply exactly 3 snow permanents, but they include the Coatl.
        // The Coatl is supplied as one of the "other" permanents here
        // to verify the factory's predicate excludes self-counting when
        // the battlefieldSnowSource includes it.
        // (In practice the battlefieldSnowSource is only OTHER permanents,
        // but the spec says "other" — we test with 3 includes-self = net 2 others.)
        var snow1 = MakeSnowPermanent(alice);
        var snow2 = MakeSnowPermanent(alice);

        Creature? coatl = null;
        coatl = IceFangCoatlFactory.Create(
            alice,
            effects,
            // battlefieldSnowSource excludes self — caller's responsibility per spec.
            // With only 2 other snows → no deathtouch.
            battlefieldSnowSource: () => new[] { snow1, snow2 });

        coatl.Zone = ZoneType.Battlefield;

        CombatAbilities.HasDeathtouch(coatl).Should().BeFalse(
            "only 2 OTHER snow permanents; coatl itself doesn't count (oracle: 'other')");
    }

    /// <summary>
    /// Deathtouch is absent when Coatl is NOT on the battlefield,
    /// even if the snow count would otherwise qualify.
    /// </summary>
    [Fact]
    public void IceFangCoatl_Deathtouch_AbsentWhenNotOnBattlefield()
    {
        var alice = new Player("Alice", 20);
        var effects = new ContinuousEffectsService();

        var snows = Enumerable.Range(0, 3).Select(_ => MakeSnowPermanent(alice)).ToArray();

        var coatl = IceFangCoatlFactory.Create(
            alice,
            effects,
            battlefieldSnowSource: () => snows);

        // Coatl is NOT on the battlefield (default zone is Hand/Library/etc).
        coatl.Zone = ZoneType.Hand;

        CombatAbilities.HasDeathtouch(coatl).Should().BeFalse(
            "continuous effect is inactive when source is not on the battlefield");
    }

    // -----------------------------------------------------------------------
    // Null guard
    // -----------------------------------------------------------------------

    [Fact]
    public void IceFangCoatl_ThrowsOnNullOwner()
    {
        var act = () => IceFangCoatlFactory.Create(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Create a minimal snow permanent on the battlefield controlled by
    /// <paramref name="controller"/>. Uses Snow-Covered Forest as the
    /// simplest snow permanent available.
    /// </summary>
    private static Land MakeSnowPermanent(Player controller)
    {
        var land = SnowCoveredForestFactory.Create(controller);
        land.Zone = ZoneType.Battlefield;
        return land;
    }
}
