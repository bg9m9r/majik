using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AuriokChampionFactory"/>.
///
/// Auriok Champion (Fifth Dawn, {W}). Creature — Human Cleric 1/1. Oracle
/// text (verified against Scryfall):
///   "Protection from black and from red
///    Whenever another creature enters, you may gain 1 life."
///
/// Coverage:
/// - Identity (name, type, Human + Cleric subtypes, cost, colour, P/T,
///   owner/controller).
/// - NamedCardFactory dispatch.
/// - Protection from black and from red (CR 702.16) — both quality markers
///   present, surfaced via Rules.Protection.HasProtectionFromColor; no
///   protection from other colours.
/// - ETB-other-creature trigger (CR 603.6a): another creature entering
///   matches (any controller); non-creature + self do not; resolution gains
///   the controller 1 life.
/// </summary>
public class AuriokChampionFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void AuriokChampion_Identity()
    {
        var c = AuriokChampionFactory.Create(_alice);

        c.Name.Should().Be("Auriok Champion");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.ManaCost.Should().Be("{W}");
        c.ManaCostValue.TotalValue.Should().Be(1);
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        CardColors.GetColors(c).Should().Contain(ManaColor.White);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AuriokChampion_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Auriok Champion", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Auriok Champion");
        ((Creature)c).HasSubtype(CardSubtype.Human).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Cleric).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1,
            "one ETB-other-creature trigger is attached");
    }

    // ── Protection ──────────────────────────────────────────────────────

    [Fact]
    public void AuriokChampion_HasProtectionFromBlackAndRed()
    {
        var c = AuriokChampionFactory.Create(_alice);

        var qualities = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality)
            .ToList();
        qualities.Should().BeEquivalentTo(new[] { "black", "red" },
            "CR 702.16 — protection from black and from red are the printed riders.");
    }

    [Fact]
    public void AuriokChampion_ProtectionFromColor_SurfacesViaRulesProtection()
    {
        var c = AuriokChampionFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.Black).Should().BeTrue(
            "Auriok Champion has protection from black (CR 702.16).");
        Protection.HasProtectionFromColor(c, ManaColor.Red).Should().BeTrue(
            "Auriok Champion has protection from red (CR 702.16).");
    }

    [Fact]
    public void AuriokChampion_NoProtectionFromOtherColors()
    {
        var c = AuriokChampionFactory.Create(_alice);

        Protection.HasProtectionFromColor(c, ManaColor.White).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Blue).Should().BeFalse();
        Protection.HasProtectionFromColor(c, ManaColor.Green).Should().BeFalse();
    }

    // ── ETB-other-creature lifegain trigger ─────────────────────────────

    [Fact]
    public void AuriokChampion_AnotherCreatureEnters_TriggerMatches()
    {
        var champ = AuriokChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);

        var trigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(bear, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Auriok Champion fires when another creature enters");
    }

    [Fact]
    public void AuriokChampion_OpponentCreatureEnters_StillTriggers()
    {
        // Printed trigger cares about ANY creature entering, not just yours.
        var champ = AuriokChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Battlefield);

        var oppCreature = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        oppCreature.SetOwner(_bob);
        oppCreature.SetController(_bob);

        var trigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(oppCreature, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeTrue(
            "Auriok Champion's printed trigger has no controller restriction");
    }

    [Fact]
    public void AuriokChampion_NonCreatureEnters_DoesNotMatch()
    {
        var champ = AuriokChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Battlefield);

        var artifact = new Artifact("Mox Pearl", "{0}");
        artifact.SetOwner(_alice);
        artifact.SetController(_alice);

        var trigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(artifact, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Auriok Champion does not trigger on non-creature ETB");
    }

    [Fact]
    public void AuriokChampion_SelfEnters_DoesNotTrigger()
    {
        var champ = AuriokChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Hand);

        var trigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        var moveEvent = new CardMovedEvent(champ, ZoneType.Hand, ZoneType.Battlefield);

        trigger.Condition.Matches(moveEvent, trigger).Should().BeFalse(
            "Auriok Champion's trigger is 'another creature' — itself is excluded (CR 603.1)");
    }

    [Fact]
    public void AuriokChampion_OnResolve_ControllerGainsOneLife()
    {
        var champ = AuriokChampionFactory.Create(_alice);
        champ.SetZone(ZoneType.Battlefield);

        var trigger = champ.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var effect in trigger.Effects) effect.Execute();

        _alice.LifeTotal.Should().Be(21,
            "Auriok Champion gains its controller 1 life ('may' auto-accepts in v1)");
    }
}
