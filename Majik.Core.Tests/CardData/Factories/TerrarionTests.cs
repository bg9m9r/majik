using FluentAssertions;
using Majik.Core.Abilities;
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
/// Unit tests for <see cref="TerrarionFactory"/>.
///
/// Terrarion — Artifact {1} (Time Spiral).
///   "This artifact enters tapped.
///    {2}, {T}, Sacrifice this artifact: Add two mana in any combination of colors.
///    When this artifact is put into a graveyard from the battlefield, draw a card."
///
/// Same family as Chromatic Star (sac-for-mana ability + LTB draw trigger);
/// differs by the enters-tapped replacement, the {2} activation cost, and the
/// two-mana-of-any-colours output.
///
/// Covers the card's UNIQUE behaviour:
/// - Identity ({1}, Artifact) — single _Identity assert.
/// - Fifteen mana abilities (one per two-pip WUBRG multiset), each producing
///   two pips.
/// - Activation gate requires {2} in the pool.
/// - Activating one slot: pays {2}, taps + sacrifices Terrarion, credits two
///   mana of the chosen combination; siblings then un-activatable.
/// - LTB trigger fires on Battlefield → Graveyard and draws on resolve.
/// - Enters-tapped replacement registered on the ReplacementBus.
/// </summary>
[Trait("Color", "C")]
public class TerrarionTests
{
    private readonly Player _alice = new("Alice", 20);

    // --------------------------------------------------------------
    // Card identity
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_Identity_IsArtifact_OneCost()
    {
        var card = TerrarionFactory.Create(_alice);

        card.Name.Should().Be("Terrarion");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{1}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // --------------------------------------------------------------
    // Ability shape — 15 mana abilities + 1 LTB trigger
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_HasFifteenManaAbilities_OnePerTwoColorCombo()
    {
        var card = TerrarionFactory.Create(_alice);
        var mas = card.Abilities.OfType<ManaAbility>().ToList();

        mas.Should().HaveCount(15, "one ManaAbility per two-pip WUBRG multiset");

        // Mono-colour doubles.
        mas.Should().ContainSingle(m => m.ManaGenerated.White == 2 && m.ManaGenerated.TotalValue == 2);
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 2 && m.ManaGenerated.TotalValue == 2);
        mas.Should().ContainSingle(m => m.ManaGenerated.Black == 2 && m.ManaGenerated.TotalValue == 2);
        mas.Should().ContainSingle(m => m.ManaGenerated.Red == 2 && m.ManaGenerated.TotalValue == 2);
        mas.Should().ContainSingle(m => m.ManaGenerated.Green == 2 && m.ManaGenerated.TotalValue == 2);

        // A representative split (Blue + Red), confirming two distinct pips.
        mas.Should().ContainSingle(m => m.ManaGenerated.Blue == 1
                                     && m.ManaGenerated.Red == 1
                                     && m.ManaGenerated.TotalValue == 2);

        // Every slot produces exactly two coloured pips.
        mas.Should().OnlyContain(m => m.ManaGenerated.TotalValue == 2);
    }

    [Fact]
    public void Terrarion_HasOneTriggeredAbility_ForLTB()
    {
        var card = TerrarionFactory.Create(_alice);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // --------------------------------------------------------------
    // Activation gate — requires {2} in the pool
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_CannotActivate_WithoutTwoGenericInPool()
    {
        var card = TerrarionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        foreach (var ma in card.Abilities.OfType<ManaAbility>())
        {
            ma.CanActivate().Should().BeFalse(
                "the {2} activation cost can't be paid from an empty pool");
        }
    }

    // --------------------------------------------------------------
    // Mana ability activation — pay {2}, tap, produce 2, sacrifice
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_Activate_PaysTwo_ProducesTwoMana_AndSacrifices()
    {
        var card = TerrarionFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Pay-for: {2} available in the pool.
        _alice.AddManaToPool(ManaCost.Parse("2"));

        var mas = card.Abilities.OfType<ManaAbility>().ToList();
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeTrue(
                "Terrarion is untapped, on the battlefield, with {2} in the pool");
        }

        // Activate the Blue+Red split.
        var ur = mas.Single(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Red == 1);
        var produced = ur.Activate();

        produced.Blue.Should().Be(1);
        produced.Red.Should().Be(1);
        produced.TotalValue.Should().Be(2);

        // {2} was consumed paying the activation cost.
        _alice.ManaPool.Generic.Should().Be(0,
            "the {2} activation cost is deducted from the pool");

        card.IsTapped.Should().BeFalse(
            "CR 400.7 — the sacrificed artifact is a new object in the graveyard and no longer tapped");
        card.Zone.Should().Be(ZoneType.Graveyard,
            "CR 701.16 — sacrifice moves Terrarion from battlefield to owner's graveyard");
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);

        // Sibling slots are now un-activatable.
        foreach (var ma in mas)
        {
            ma.CanActivate().Should().BeFalse(
                "Terrarion has been sacrificed — no further activations possible");
        }
    }

    // --------------------------------------------------------------
    // LTB trigger — Battlefield → Graveyard for the source
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_DiesTrigger_ConditionMatchesBattlefieldToGraveyard()
    {
        var card = TerrarionFactory.Create(_alice);
        card.SetZone(ZoneType.Battlefield);

        var ltb = card.Abilities.OfType<TriggeredAbility>().Single();

        var dies = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Graveyard);
        ltb.IsTriggered(dies).Should().BeTrue(
            "Battlefield → Graveyard for the source matches the LTB condition");

        var bounce = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Hand);
        ltb.IsTriggered(bounce).Should().BeFalse(
            "Battlefield → Hand is a bounce, not LTB-to-graveyard");

        var exile = new CardMovedEvent(card, ZoneType.Battlefield, ZoneType.Exile);
        ltb.IsTriggered(exile).Should().BeFalse(
            "Battlefield → Exile bypasses the graveyard step entirely");
    }

    [Fact]
    public void Terrarion_LTB_Resolve_DrawsACard()
    {
        var top = new Card("Top of library", "");
        top.SetOwner(_alice);
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var card = TerrarionFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);

        var ltb = card.Abilities.OfType<TriggeredAbility>().Single();
        ltb.Resolve();

        _alice.Zones.Hand.GetCards().Should().Contain(top, "LTB cantrip drew one card");
        _alice.Zones.Library.GetCards().Should().NotContain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }

    // --------------------------------------------------------------
    // Enters-tapped replacement — CR 614.1c
    // --------------------------------------------------------------

    [Fact]
    public void Terrarion_RegistersEntersTappedReplacement_WhenBusSupplied()
    {
        var replacements = new ReplacementBus();

        var card = TerrarionFactory.Create(_alice, triggers: null, replacements: replacements);

        // The ETB ZoneMoveIntent for Terrarion should be replaced to enter tapped.
        var intent = new ZoneMoveIntent(card, ZoneType.Hand, ZoneType.Battlefield);
        var result = replacements.Apply(intent);

        result.Should().NotBeNull("the enters-tapped replacement transforms, never cancels, the ETB intent");
        result!.EntersTapped.Should().BeTrue(
            "CR 614.1c — \"This artifact enters tapped.\"");
    }
}
