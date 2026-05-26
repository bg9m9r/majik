using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="MoggWarMarshalFactory"/>.
///
/// Mogg War Marshal (Coldsnap / Modern Horizons 2, {1}{R}):
///   Creature — Goblin Warrior 1/1.
///   Echo {1}{R}.
///   When Mogg War Marshal enters or dies, create a 1/1 red Goblin
///   creature token.
///
/// Covers:
///   - Identity (Goblin Warrior 1/1, {1}{R}, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Echo keyword marker (shape-only — no upkeep loop yet).
///   - TWO triggers: ETB (Battlefield active) + dies (Battlefield +
///     Graveyard active — Wurmcoil posture).
///   - ETB trigger condition matches CardMovedEvent into the battlefield.
///   - Dies trigger condition matches CardMovedEvent Battlefield → Graveyard.
///   - Resolving each trigger creates exactly one 1/1 red Goblin creature
///     token under the card's controller.
/// </summary>
public class MoggWarMarshalTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static TriggeredAbility GetEtbTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Count == 1
                && t.ActiveZones.Contains(ZoneType.Battlefield));

    private static TriggeredAbility GetDiesTrigger(Creature c) =>
        c.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.ActiveZones.Contains(ZoneType.Graveyard));

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_Identity()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);

        mwm.Name.Should().Be("Mogg War Marshal");
        mwm.ManaCost.Should().Be("{1}{R}");
        mwm.HasType(CardType.Creature).Should().BeTrue();
        mwm.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        mwm.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        mwm.BasePower.Should().Be(1);
        mwm.BaseToughness.Should().Be(1);
        mwm.Owner.Should().BeSameAs(_alice);
        mwm.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MoggWarMarshal_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Mogg War Marshal", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Mogg War Marshal");
        card.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        card.HasSubtype(CardSubtype.Warrior).Should().BeTrue();
        ((Creature)card).BasePower.Should().Be(1);
        ((Creature)card).BaseToughness.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Echo keyword marker (shape-only — no upkeep loop wired in v1)
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_HasEchoKeywordMarker()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);

        var echoMarker = mwm.Abilities.OfType<KeywordAbility>()
            .FirstOrDefault(k => string.Equals(k.Keyword, "Echo",
                System.StringComparison.OrdinalIgnoreCase));
        echoMarker.Should().NotBeNull(
            "Echo {1}{R} is wired as a description-only KeywordAbility marker " +
            "(CR 702.49 — upkeep sac-unless-pay loop is deferred).");
    }

    // -----------------------------------------------------------------------
    // Trigger structure
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_HasTwoTriggers_OneEtb_OneDies()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);

        var triggers = mwm.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(2,
            "one ETB trigger + one dies trigger sharing the same token-creation body.");

        // ETB trigger: ActiveZones = {Battlefield} only.
        var etb = GetEtbTrigger(mwm);
        etb.ActiveZones.Should().BeEquivalentTo(new[] { ZoneType.Battlefield });

        // Dies trigger: ActiveZones = {Battlefield, Graveyard} (Wurmcoil
        // posture so the zone-guard still matches after ZoneService stamps
        // Zone = Graveyard pre-publish).
        var dies = GetDiesTrigger(mwm);
        dies.ActiveZones.Should().BeEquivalentTo(
            new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }

    [Fact]
    public void MoggWarMarshal_EtbTrigger_FiresOnEnterBattlefield()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);
        // ActiveZones zone-guard: the source must be in Battlefield at the
        // time IsTriggered checks (the ZoneService move stamps the new zone
        // before publishing the CardMovedEvent).
        mwm.SetZone(ZoneType.Battlefield);

        var etb = GetEtbTrigger(mwm);
        var enter = new CardMovedEvent(mwm, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(enter).Should().BeTrue(
            "ETB trigger fires when Mogg War Marshal enters the battlefield " +
            "(CR 603.6a).");
    }

    [Fact]
    public void MoggWarMarshal_DiesTrigger_FiresOnBattlefieldToGraveyard()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);
        // ZoneService publishes the CardMovedEvent AFTER stamping Zone =
        // Graveyard, so the dies trigger's ActiveZones includes Graveyard
        // (Wurmcoil / Matter Reshaper posture) and the source must be in
        // Graveyard at IsTriggered time.
        mwm.SetZone(ZoneType.Graveyard);

        var dies = GetDiesTrigger(mwm);
        var died = new CardMovedEvent(mwm, ZoneType.Battlefield, ZoneType.Graveyard);
        dies.IsTriggered(died).Should().BeTrue(
            "dies trigger fires Battlefield → Graveyard (CR 603.6c / CR 700.4).");
    }

    // -----------------------------------------------------------------------
    // Token creation — ETB trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_EtbResolve_CreatesOneOneRedGoblinToken()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);
        mwm.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mwm);

        var goblinsBefore = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken);

        var etb = GetEtbTrigger(mwm);
        foreach (var e in etb.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken)
            .ToList();
        tokens.Should().HaveCount(goblinsBefore + 1,
            "ETB trigger creates exactly one 1/1 Goblin token.");

        var token = tokens.Last();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Token creation — dies trigger
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_DiesResolve_CreatesOneOneRedGoblinToken()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);
        mwm.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(mwm);

        var goblinsBefore = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken);

        var dies = GetDiesTrigger(mwm);
        foreach (var e in dies.Effects) e.Execute();

        var tokens = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.HasSubtype(CardSubtype.Goblin) && c.IsToken)
            .ToList();
        tokens.Should().HaveCount(goblinsBefore + 1,
            "dies trigger creates exactly one 1/1 Goblin token.");

        var token = tokens.Last();
        token.BasePower.Should().Be(1);
        token.BaseToughness.Should().Be(1);
        token.HasSubtype(CardSubtype.Goblin).Should().BeTrue();
        token.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Trigger self-match (ETB only on self entering)
    // -----------------------------------------------------------------------

    [Fact]
    public void MoggWarMarshal_EtbTrigger_DoesNotFire_OnOtherCardEntering()
    {
        var mwm = MoggWarMarshalFactory.Create(_alice);
        var bears = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bears.SetOwner(_alice);

        var etb = GetEtbTrigger(mwm);
        var other = new CardMovedEvent(bears, ZoneType.Hand, ZoneType.Battlefield);
        etb.IsTriggered(other).Should().BeFalse(
            "the ETB trigger is self-match — other creatures entering don't fire it.");
    }
}
