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
/// Tests for <see cref="BragoKingEternalFactory"/> (Conspiracy, {2}{W}{U}).
///
/// Covers shape, Flying marker, combat-damage trigger structure, and the
/// exile-then-immediate-return blink on resolve. The "any number of nonland
/// permanents" multi-target distribution is a documented v1 gap (single
/// 1..1 target collapse — same posture as Slogurk's "up to three" target).
/// </summary>
public class BragoKingEternalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Brago_Identity()
    {
        var c = BragoKingEternalFactory.Create(_alice);

        c.Name.Should().Be("Brago, King Eternal");
        c.ManaCost.Should().Be("{2}{W}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.Supertypes.Should().Contain(CardSupertype.Legendary);
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Brago_HasFlyingMarker()
    {
        var c = BragoKingEternalFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void Brago_HasSingleCombatDamageTrigger_TargetingOwnNonlandPermanent()
    {
        var c = BragoKingEternalFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "single combat-damage trigger");

        var trig = triggers[0];
        trig.TargetRequests.Should().HaveCount(1);
        trig.TargetRequests[0].MinTargets.Should().Be(1);
        trig.TargetRequests[0].MaxTargets.Should().Be(1);
        trig.TargetRequests[0].Description.Should().Contain("nonland");
        trig.TargetRequests[0].Description.Should().Contain("you control");
    }

    [Fact]
    public void Brago_Resolve_BlinksTargetedPermanent_BackToBattlefield()
    {
        var brago = BragoKingEternalFactory.Create(_alice);
        brago.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(brago);

        // A nonland permanent under Alice's control — Brago's classic blink
        // target (a Mulldrifter / Solemn Simulacrum-style ETB engine).
        var simulacrum = new Creature("Solemn Simulacrum", "{4}", 2, 2);
        simulacrum.SetOwner(_alice);
        simulacrum.SetController(_alice);
        simulacrum.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(simulacrum);

        var trig = brago.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { simulacrum },
        });
        foreach (var e in trig.Effects) e.Execute();

        // Same-resolution exile-then-return → permanent ends up back on
        // the battlefield (CR 701.21 + CR 614, mirrors Cloudshift).
        simulacrum.Zone.Should().Be(ZoneType.Battlefield,
            "Brago's exile-then-return resolves in the same step (CR 614)");
        _alice.Zones.Battlefield.GetCards().Should().Contain(simulacrum);
        _alice.Zones.Exile.GetCards().Should().NotContain(simulacrum);
        simulacrum.Controller.Should().BeSameAs(_alice,
            "the re-entry happens under the permanent's owner's control (CR 614)");
    }

    [Fact]
    public void Brago_Resolve_RejectsLandTarget()
    {
        // The printed "nonland permanent" rider is re-checked at resolution.
        var brago = BragoKingEternalFactory.Create(_alice);
        brago.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(brago);

        var island = new Land("Island");
        island.SetOwner(_alice);
        island.SetController(_alice);
        island.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(island);

        var trig = brago.Abilities.OfType<TriggeredAbility>().Single();
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { island },
        });
        foreach (var e in trig.Effects) e.Execute();

        island.Zone.Should().Be(ZoneType.Battlefield,
            "land targets fail the resolution-time legality check → no blink");
        _alice.Zones.Exile.GetCards().Should().NotContain(island);
    }

    [Fact]
    public void Brago_Resolve_NoTarget_NoOp()
    {
        var brago = BragoKingEternalFactory.Create(_alice);
        brago.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(brago);

        var trig = brago.Abilities.OfType<TriggeredAbility>().Single();
        // No target supplied — trigger resolves with an empty target list.
        trig.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });

        Action act = () =>
        {
            foreach (var e in trig.Effects) e.Execute();
        };
        act.Should().NotThrow("missing-target path is a clean no-op");
    }

    [Fact]
    public void Brago_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Brago, King Eternal", _alice);

        c.Should().NotBeNull();
        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Brago, King Eternal");
        ((Creature)c).Power.Should().Be(2);
        ((Creature)c).Toughness.Should().Be(4);
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Advisor).Should().BeTrue();
    }
}
