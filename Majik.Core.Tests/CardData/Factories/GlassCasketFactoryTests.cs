using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="GlassCasketFactory"/>.
///
/// Glass Casket (Throne of Eldraine, {1}{W}) — Artifact. Oracle text
/// (verified against Scryfall):
///   "When this artifact enters, exile target creature an opponent controls
///    with mana value 3 or less until this artifact leaves the battlefield."
///
/// Same exile-on-ETB / return-on-LTB closure shape as
/// <see cref="PortableHoleFactory"/>, narrowed to an opponent's *creature*
/// with mana value 3 or less, on a {1}{W} Artifact.
///
/// Covers:
/// - Identity (Artifact {1}{W}, owner / controller wired, two triggered abilities).
/// - NamedCardFactory dispatch.
/// - ETB exiles a target creature (mv ≤ 3) an opponent controls.
/// - ETB rejects noncreature permanents (oracle: "target creature").
/// - ETB rejects controller-side creatures (oracle: "an opponent controls").
/// - ETB rejects creatures with mana value 4 or more (CR 202.3).
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "W")]
public class GlassCasketFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility Etb(Permanent card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 1);

    private static TriggeredAbility Ltb(Permanent card) =>
        card.Abilities.OfType<TriggeredAbility>().Single(t => t.TargetRequests.Count == 0);

    [Fact]
    public void GlassCasket_Identity()
    {
        var c = GlassCasketFactory.Create(_alice);

        c.Name.Should().Be("Glass Casket");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void GlassCasket_Etb_ExilesOpponentLowMvCreature()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        // Bob's mv-2 creature is a legal target.
        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted creature with mv ≤ 3 (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void GlassCasket_Etb_ExilesMvThreeCreature()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        // mv 3 — exactly at the printed "mana value 3 or less" cap.
        var bobsCreature = new Creature("Watchwolf", "{1}{G}{W}", 3, 3);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "mana value exactly 3 is within the 'mana value 3 or less' cap (CR 202.3)");
    }

    [Fact]
    public void GlassCasket_Etb_RejectsNoncreaturePermanent()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        var bobsArtifact = new Artifact("Mind Stone", "{2}", supertypes: null, subtypes: null);
        bobsArtifact.SetOwner(_bob);
        bobsArtifact.SetController(_bob);
        bobsArtifact.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsArtifact);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsArtifact } });
        foreach (var e in etb.Effects) e.Execute();

        bobsArtifact.Zone.Should().Be(ZoneType.Battlefield,
            "noncreature permanents are skipped by the printed 'target creature' filter");
    }

    [Fact]
    public void GlassCasket_Etb_RejectsControllerOwnCreature()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { aliceCreature } });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side creatures (oracle: 'an opponent controls')");
    }

    [Fact]
    public void GlassCasket_Etb_RejectsHighManaValueCreature()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        // mv 4 — above the printed "mana value 3 or less" cap (CR 202.3).
        var bobsCreature = new Creature("Big Goyf", "{2}{G}{G}", 4, 4);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "creatures with mana value 4 or more are skipped by the 'mv ≤ 3' filter");
    }

    [Fact]
    public void GlassCasket_Ltb_ReturnsExiledCardUnderOwnersControl()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = Etb(casket);
        etb.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bobsCreature } });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        var ltb = Ltb(casket);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled card to the battlefield");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void GlassCasket_Ltb_NoOpWhenNothingExiled()
    {
        var casket = GlassCasketFactory.Create(_alice);
        casket.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(casket);

        var ltb = Ltb(casket);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }
}
