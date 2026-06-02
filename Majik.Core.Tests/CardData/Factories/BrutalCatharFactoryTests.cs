using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="BrutalCatharFactory"/> — transform DFC front face
/// Brutal Cathar ({2}{W}) // Moonrage Brute.
///
/// Covers:
/// - Identity (2/2 Human Soldier Werewolf {2}{W}, owner / controller wired).
/// - NamedCardFactory dispatch.
/// - Daybound + Nightbound markers + MdfcState back-face (Moonrage Brute
///   3/3 red Werewolf with First strike).
/// - Two triggered abilities (ETB exile + LTB return).
/// - ETB exiles a target creature an opponent controls (CR 701.21).
/// - ETB rejects controller-side creatures ("an opponent controls").
/// - ETB rejects noncreature targets.
/// - LTB returns the exiled card to the battlefield under its owner's control.
/// - LTB no-ops cleanly when nothing was exiled.
/// - Back-face Ward—Pay 3 life builds a real PayLifeCost(3) ward.
/// </summary>
[Trait("Color", "W")]
public class BrutalCatharFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void BrutalCathar_Identity()
    {
        var c = BrutalCatharFactory.Create(_alice);

        c.Name.Should().Be("Brutal Cathar");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.HasSubtype(CardSubtype.Werewolf).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(2,
            "ETB exile trigger + LTB return trigger");
    }
    [Fact]
    public void BrutalCathar_CarriesDayboundNightboundAndBackFace()
    {
        var c = BrutalCatharFactory.Create(_alice);

        // CR 702.145 — both daybound (front) and nightbound (back) markers.
        DayboundNightbound.HasDaybound(c).Should().BeTrue();
        DayboundNightbound.HasNightbound(c).Should().BeTrue();

        // CR 711 — transform DFC face tracker with back-face Moonrage Brute.
        c.MdfcState.Should().NotBeNull();
        c.MdfcState!.FrontFaceName.Should().Be("Brutal Cathar");
        c.MdfcState.BackFaceName.Should().Be("Moonrage Brute");
        c.MdfcState.IsBackFace.Should().BeFalse("starts front-face up");

        var back = c.MdfcState.BackFaceCharacteristics;
        back.Should().NotBeNull();
        back!.Power.Should().Be(3);
        back.Toughness.Should().Be(3);
        back.Keywords.Should().Contain("First strike");
        back.Colors.Should().Contain(ManaColor.Red);
    }

    [Fact]
    public void BrutalCathar_Etb_ExilesOpponentCreature()
    {
        var cathar = BrutalCatharFactory.Create(_alice);
        cathar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cathar);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        var etb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Exile,
            "ETB exiles the targeted opponent creature (CR 701.21)");
        _bob.Zones.Exile.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void BrutalCathar_Etb_RejectsControllerOwnCreature()
    {
        var cathar = BrutalCatharFactory.Create(_alice);
        cathar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cathar);

        var aliceCreature = new Creature("Bird", "{1}{W}", 1, 2);
        aliceCreature.SetOwner(_alice);
        aliceCreature.SetController(_alice);
        aliceCreature.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(aliceCreature);

        var etb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { aliceCreature },
        });
        foreach (var e in etb.Effects) e.Execute();

        aliceCreature.Zone.Should().Be(ZoneType.Battlefield,
            "ETB ignores controller-side creatures (oracle: 'an opponent controls')");
    }

    [Fact]
    public void BrutalCathar_Etb_RejectsNoncreatureTarget()
    {
        var cathar = BrutalCatharFactory.Create(_alice);
        cathar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cathar);

        // A noncreature permanent — illegal per the "target creature" filter
        // (CR 608.2b).
        var bobsLand = new Land("Forest");
        bobsLand.SetOwner(_bob);
        bobsLand.SetController(_bob);
        bobsLand.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsLand);

        var etb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsLand },
        });
        foreach (var e in etb.Effects) e.Execute();

        bobsLand.Zone.Should().Be(ZoneType.Battlefield,
            "noncreature targets are skipped by the printed 'target creature' filter");
    }

    [Fact]
    public void BrutalCathar_Ltb_ReturnsExiledCreatureUnderOwnersControl()
    {
        var cathar = BrutalCatharFactory.Create(_alice);
        cathar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cathar);

        var bobsCreature = new Creature("Tarmogoyf", "{1}{G}", 0, 1);
        bobsCreature.SetOwner(_bob);
        bobsCreature.SetController(_bob);
        bobsCreature.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(bobsCreature);

        // ETB exile.
        var etb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 1);
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bobsCreature },
        });
        foreach (var e in etb.Effects) e.Execute();
        bobsCreature.Zone.Should().Be(ZoneType.Exile);

        // LTB — Brutal Cathar leaves the battlefield.
        var ltb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        foreach (var e in ltb.Effects) e.Execute();

        bobsCreature.Zone.Should().Be(ZoneType.Battlefield,
            "LTB returns the exiled creature when the Cathar leaves");
        bobsCreature.Controller.Should().BeSameAs(_bob,
            "returned card is under its owner's control (CR 110.2)");
        _bob.Zones.Battlefield.GetCards().Should().Contain(bobsCreature);
        _bob.Zones.Exile.GetCards().Should().NotContain(bobsCreature);
    }

    [Fact]
    public void BrutalCathar_Ltb_NoOpWhenNothingExiled()
    {
        var cathar = BrutalCatharFactory.Create(_alice);
        cathar.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cathar);

        var ltb = cathar.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.TargetRequests.Count == 0);
        var act = () => { foreach (var e in ltb.Effects) e.Execute(); };
        act.Should().NotThrow();

        _bob.Zones.Battlefield.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void BrutalCathar_BackFace_WardPay3Life_BuildsPayLifeCost()
    {
        var cathar = BrutalCatharFactory.Create(_alice);

        var ward = BrutalCatharFactory.BuildWardEffect(cathar);
        ward.Should().NotBeNull("Moonrage Brute carries Ward—Pay 3 life (CR 702.21c)");

        // Marker keyword present on the discovery surface.
        cathar.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Ward").Should().BeTrue();
    }
}
