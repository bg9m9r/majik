using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="DetentionSphereFactory"/>.
///
/// Detention Sphere (Return to Ravnica, {1}{W}{U}) — Enchantment.
///   "When this enchantment enters, you may exile target nonland permanent
///    not named Detention Sphere and all other permanents with the same name
///    as that permanent.
///    When this enchantment leaves the battlefield, return the exiled cards
///    to the battlefield under their owner's control."
///
/// Covers:
/// - Identity (Enchantment {1}{W}{U}, owner / controller wired).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles the target AND all other permanents with the same name
///   (controller-agnostic — own copies caught too).
/// - ETB rejects lands at resolution time.
/// - LTB returns every exiled card to the battlefield under its owner's
///   control.
/// - LTB no-ops cleanly when nothing was exiled.
/// </summary>
[Trait("Color", "WU")]
public class DetentionSphereFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DetentionSphere_Identity()
    {
        var c = DetentionSphereFactory.Create(_alice);

        c.Name.Should().Be("Detention Sphere");
        c.ManaCost.Should().Be("{1}{W}{U}");
        c.HasType(CardType.Enchantment).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }

    [Fact]
    public void DetentionSphere_Etb_ExilesTargetAndAllSameNamePermanents()
    {
        var sphere = DetentionSphereFactory.Create(_alice);
        sphere.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sphere);

        // Bob controls two Goblin tokens with the same name; Alice controls one.
        var bobGoblin1 = MakeCreature("Goblin", _bob);
        var bobGoblin2 = MakeCreature("Goblin", _bob);
        var aliceGoblin = MakeCreature("Goblin", _alice);
        // An unrelated creature that must NOT be swept.
        var bobBear = MakeCreature("Grizzly Bears", _bob);

        var etb = sphere.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobGoblin1 },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobGoblin1.Zone.Should().Be(ZoneType.Exile, "the chosen target is exiled");
        bobGoblin2.Zone.Should().Be(ZoneType.Exile, "same-name permanent is exiled");
        aliceGoblin.Zone.Should().Be(ZoneType.Exile,
            "same-name sweep is controller-agnostic (CR 201.2) — own copies too");
        bobBear.Zone.Should().Be(ZoneType.Battlefield,
            "a differently-named permanent is untouched");
    }

    [Fact]
    public void DetentionSphere_Etb_RejectsLandTarget()
    {
        var sphere = DetentionSphereFactory.Create(_alice);
        sphere.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sphere);

        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = sphere.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "lands are skipped by the printed 'nonland' filter (CR 608.2b)");
    }

    [Fact]
    public void DetentionSphere_Ltb_ReturnsAllExiledCardsUnderOwnersControl()
    {
        var sphere = DetentionSphereFactory.Create(_alice);
        sphere.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sphere);

        var bobGoblin = MakeCreature("Goblin", _bob);
        var aliceGoblin = MakeCreature("Goblin", _alice);

        var etb = sphere.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobGoblin },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobGoblin.Zone.Should().Be(ZoneType.Exile);
        aliceGoblin.Zone.Should().Be(ZoneType.Exile);

        // LTB — Detention Sphere leaves the battlefield.
        var ltb = sphere.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        bobGoblin.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns every exiled card to the battlefield");
        bobGoblin.Controller.Should().BeSameAs(_bob,
            "returned under its owner's control (CR 110.2)");
        aliceGoblin.Zone.Should().Be(ZoneType.Battlefield);
        aliceGoblin.Controller.Should().BeSameAs(_alice,
            "each card returns under ITS OWN owner's control");

        _bob.Zones.Battlefield.GetCards().Should().Contain(bobGoblin);
        _alice.Zones.Battlefield.GetCards().Should().Contain(aliceGoblin);
    }

    [Fact]
    public void DetentionSphere_Ltb_NoOpWhenNothingExiled()
    {
        var sphere = DetentionSphereFactory.Create(_alice);
        sphere.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sphere);

        var ltb = sphere.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
        _alice.Zones.Battlefield.GetCards().Should().ContainSingle()
            .Which.Name.Should().Be("Detention Sphere");
    }

    private static Creature MakeCreature(string name, Player controller)
    {
        var c = new Creature(name, "{1}", 1, 1);
        c.SetOwner(controller);
        c.SetController(controller);
        c.SetZone(ZoneType.Battlefield);
        controller.Zones.Battlefield.AddCard(c);
        return c;
    }
}
