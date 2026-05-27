using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for Drogskol Captain (Innistrad, {1}{W}{U}).
///
/// Oracle:
///   "Flying."
///   "Other Spirit creatures you control get +1/+1 and have hexproof."
///
/// Coverage:
///   * Identity (name, type, cost, Spirit + Soldier subtypes, 2/2,
///     owner / controller).
///   * NamedCardFactory dispatch.
///   * Printed Flying keyword on Captain itself (CR 702.9).
///   * LordStaticEffect: other Spirit creatures you control gain +1/+1
///     and Hexproof (CR 613.7c + CR 613.1f).
///   * Non-Spirit creatures are NOT pumped.
///   * Opponent's Spirit is NOT pumped (controller-scoped).
///   * Captain itself doesn't double-buff via its own static
///     (includeSelf: false).
///   * Two Captains buff each other (each excludes self only).
///   * LTB lifts the bonus when Captain leaves the battlefield.
/// </summary>
public class DrogskolCaptainFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DrogskolCaptain_Identity()
    {
        var c = DrogskolCaptainFactory.Create(_alice);

        c.Name.Should().Be("Drogskol Captain");
        c.ManaCost.Should().Be("{1}{W}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void DrogskolCaptain_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Drogskol Captain", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Drogskol Captain");
        c.HasSubtype(CardSubtype.Spirit).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
    }

    [Fact]
    public void DrogskolCaptain_HasPrintedFlying()
    {
        var c = DrogskolCaptainFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Select(k => k.Keyword)
            .Should().Contain("Flying",
                "CR 702.9 — Flying is the printed first keyword.");
    }

    [Fact]
    public void DrogskolCaptain_BuffsOtherSpirit_Plus1Plus1AndHexproof()
    {
        var svc = new ContinuousEffectsService();

        var otherSpirit = new Creature("Mausoleum Wanderer", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var captain = DrogskolCaptainFactory.Create(_alice, svc);
        captain.Zone = ZoneType.Battlefield;
        captain.ActiveEffects = svc;

        otherSpirit.GetPower().Should().Be(2,
            "other Spirits Alice controls get +1/+1 (1 → 2 power).");
        otherSpirit.GetToughness().Should().Be(2);

        svc.Compute(otherSpirit).Keywords.Should().Contain("Hexproof",
            "other Spirits gain Hexproof from Captain's static.");
    }

    [Fact]
    public void DrogskolCaptain_DoesNotPump_NonSpirit()
    {
        var svc = new ContinuousEffectsService();

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2,
            subtypes: new[] { CardSubtype.Bear })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var captain = DrogskolCaptainFactory.Create(_alice, svc);
        captain.Zone = ZoneType.Battlefield;
        captain.ActiveEffects = svc;

        bear.GetPower().Should().Be(2,
            "Captain only buffs creatures matching the Spirit subtype.");
        bear.GetToughness().Should().Be(2);
        svc.Compute(bear).Keywords.Should().NotContain("Hexproof",
            "non-Spirit creatures don't receive the granted Hexproof.");
    }

    [Fact]
    public void DrogskolCaptain_DoesNotPump_OpponentSpirit()
    {
        var svc = new ContinuousEffectsService();

        var oppSpirit = new Creature("Bob's Spirit", "{1}{U}", 2, 1,
            subtypes: new[] { CardSubtype.Spirit })
        {
            Owner = _bob,
            Controller = _bob,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var captain = DrogskolCaptainFactory.Create(_alice, svc);
        captain.Zone = ZoneType.Battlefield;
        captain.ActiveEffects = svc;

        oppSpirit.GetPower().Should().Be(2,
            "Captain's static is scoped to its controller's Spirits " +
            "(CR 109.5 — 'you').");
        oppSpirit.GetToughness().Should().Be(1);
        svc.Compute(oppSpirit).Keywords.Should().NotContain("Hexproof",
            "opponent's Spirits don't get the granted Hexproof.");
    }

    [Fact]
    public void DrogskolCaptain_DoesNotSelfPump()
    {
        // includeSelf: false — Captain's own +1/+1 + Hexproof static
        // doesn't stack on itself. Its OWN Flying comes from the printed
        // keyword, not the static.
        var svc = new ContinuousEffectsService();

        var captain = DrogskolCaptainFactory.Create(_alice, svc);
        captain.Zone = ZoneType.Battlefield;
        captain.ActiveEffects = svc;

        captain.GetPower().Should().Be(2,
            "Captain doesn't self-buff via 'Other Spirits'.");
        captain.GetToughness().Should().Be(2);
        svc.Compute(captain).Keywords.Should().NotContain("Hexproof",
            "Captain itself does not gain Hexproof from its own static " +
            "(printed text says 'Other').");
    }

    [Fact]
    public void TwoCaptains_BuffEachOther_3_3WithHexproof()
    {
        var svc = new ContinuousEffectsService();

        var captainA = DrogskolCaptainFactory.Create(_alice, svc);
        captainA.Zone = ZoneType.Battlefield;
        captainA.ActiveEffects = svc;

        var captainB = DrogskolCaptainFactory.Create(_alice, svc);
        captainB.Zone = ZoneType.Battlefield;
        captainB.ActiveEffects = svc;

        // includeSelf: false only excludes self vs self. Each Captain
        // buffs the OTHER, so both end up 3/3 with Hexproof.
        captainA.GetPower().Should().Be(3,
            "the other Captain's static still applies to this one.");
        captainA.GetToughness().Should().Be(3);
        captainB.GetPower().Should().Be(3);
        captainB.GetToughness().Should().Be(3);

        svc.Compute(captainA).Keywords.Should().Contain("Hexproof",
            "Captain B's static grants Hexproof to Captain A.");
        svc.Compute(captainB).Keywords.Should().Contain("Hexproof");
    }

    [Fact]
    public void DrogskolCaptain_LTB_LiftsBuffFromOtherSpirit()
    {
        var svc = new ContinuousEffectsService();

        var otherSpirit = new Creature("Mausoleum Wanderer", "{U}", 1, 1,
            subtypes: new[] { CardSubtype.Spirit })
        {
            Owner = _alice,
            Controller = _alice,
            Zone = ZoneType.Battlefield,
            ActiveEffects = svc,
        };

        var captain = DrogskolCaptainFactory.Create(_alice, svc);
        captain.Zone = ZoneType.Battlefield;
        captain.ActiveEffects = svc;

        otherSpirit.GetPower().Should().Be(2);
        svc.Compute(otherSpirit).Keywords.Should().Contain("Hexproof");

        // Captain dies — LordStaticEffect.IsActive() short-circuits when
        // source isn't on the battlefield (CR 613).
        captain.SetZone(ZoneType.Graveyard);

        otherSpirit.GetPower().Should().Be(1, "bonus lifts on LTB.");
        otherSpirit.GetToughness().Should().Be(1);
        svc.Compute(otherSpirit).Keywords.Should().NotContain("Hexproof",
            "granted Hexproof lifts when Captain leaves the battlefield.");
    }
}
