using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SungoldSentinelFactory"/>.
///
/// Sungold Sentinel (Innistrad: Midnight Hunt, {1}{W}) — Creature — Human
/// Soldier 3/2. Oracle text (verified against Scryfall 2026-06-02):
///   "Whenever this creature enters or attacks, exile up to one target card
///    from a graveyard.
///    Coven — {1}{W}: Choose a color. This creature gains hexproof from that
///    color until end of turn and can't be blocked by creatures of that color
///    this turn. Activate only if you control three or more creatures with
///    different powers."
///
/// Covers identity, the enter/attack exile trigger, the Coven gate, and the
/// hexproof-from-colour + can't-be-blocked-by-colour grant.
/// </summary>
[Trait("Color", "W")]
public class SungoldSentinelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity ─────────────────────────────────────────────────────────

    [Fact]
    public void SungoldSentinel_Identity()
    {
        var c = SungoldSentinelFactory.Create(_alice);

        c.Name.Should().Be("Sungold Sentinel");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Soldier).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SungoldSentinel_DispatchesThroughNamedFactory()
    {
        var card = NamedCardFactory.Create("Sungold Sentinel", _alice);
        card.Should().NotBeNull();
        card.Should().BeAssignableTo<Creature>();
        card.Name.Should().Be("Sungold Sentinel");
    }

    // ── Enter/attack exile trigger ─────────────────────────────────────────

    [Fact]
    public void SungoldSentinel_HasEnterOrAttackExileTrigger_UpToOneTarget()
    {
        var c = SungoldSentinelFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        // One enters trigger + one attacks trigger (same exile effect).
        triggers.Should().HaveCount(2,
            "the exile clause triggers on BOTH enter and attack.");

        // "up to one" — the target request allows zero targets.
        triggers.Should().OnlyContain(t =>
            t.TargetRequests.Count == 1 && t.TargetRequests[0].MinTargets == 0
            && t.TargetRequests[0].MaxTargets == 1);
    }

    [Fact]
    public void SungoldSentinel_EtbResolution_ExilesChosenGraveyardCard()
    {
        var sentinel = SungoldSentinelFactory.Create(_alice);

        // A card in Bob's graveyard.
        var corpse = new Creature("Corpse", "{B}", 1, 1) { Owner = _bob };
        _bob.Zones.Graveyard.AddCard(corpse);
        corpse.SetZone(ZoneType.Graveyard);

        SungoldSentinelFactory.ResolveExile(corpse);

        corpse.Zone.Should().Be(ZoneType.Exile);
        _bob.Zones.Exile.GetCards().Should().Contain(corpse);
        _bob.Zones.Graveyard.GetCards().Should().NotContain(corpse);
    }

    [Fact]
    public void SungoldSentinel_ExileResolution_NullTargetIsCleanNoOp()
    {
        // "up to one target" — zero targets chosen is legal; resolution is a
        // clean no-op.
        var act = () => SungoldSentinelFactory.ResolveExile(null);
        act.Should().NotThrow();
    }

    // ── Coven-gated activated ability ──────────────────────────────────────

    [Fact]
    public void SungoldSentinel_HasCovenActivatedAbility_With1WCost()
    {
        var c = SungoldSentinelFactory.Create(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1, "the Coven grant is the only activated ability.");
        activated[0].Costs.OfType<ManaCostCost>().Should().ContainSingle()
            .Which.Cost.ToString().Should().Be("1W");
    }

    [Fact]
    public void SungoldSentinel_CovenGate_FalseWithFewerThanThreeDistinctPowers()
    {
        // Only Sungold itself on the battlefield — one distinct power → no Coven.
        var sentinel = SungoldSentinelFactory.Create(_alice);
        sentinel.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sentinel);

        SungoldSentinelFactory.CanActivateCoven(_alice).Should().BeFalse();
    }

    [Fact]
    public void SungoldSentinel_CovenGate_TrueWithThreeDistinctPowers()
    {
        var sentinel = SungoldSentinelFactory.Create(_alice); // power 3
        sentinel.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(sentinel);
        AddCreature(_alice, "P1", 1);
        AddCreature(_alice, "P2", 2);

        SungoldSentinelFactory.CanActivateCoven(_alice).Should().BeTrue();
    }

    // ── Hexproof-from-colour + can't-be-blocked-by-colour grant ────────────

    [Fact]
    public void SungoldSentinel_Grant_HexproofFromChosenColor_BlocksOpponentMatchingColorSpell()
    {
        var svc = new ContinuousEffectsService();
        var sentinel = SungoldSentinelFactory.Create(_alice, svc);
        sentinel.SetZone(ZoneType.Battlefield);

        SungoldSentinelFactory.ResolveColorGrant(sentinel, ManaColor.Red, svc);

        var spec = new TargetSpec("creature").Creatures();
        // CR 702.11e — an opponent's RED spell can't target it; a BLUE one can.
        TargetLegality.IsLegal(spec, sentinel, _bob, sourceColor: "Red").Should().BeFalse();
        TargetLegality.IsLegal(spec, sentinel, _bob, sourceColor: "Blue").Should().BeTrue();
        // Controller can still target with any colour.
        TargetLegality.IsLegal(spec, sentinel, _alice, sourceColor: "Red").Should().BeTrue();
    }

    [Fact]
    public void SungoldSentinel_Grant_CantBeBlockedByCreaturesOfChosenColor()
    {
        var svc = new ContinuousEffectsService();
        var sentinel = SungoldSentinelFactory.Create(_alice, svc);
        sentinel.SetZone(ZoneType.Battlefield);

        SungoldSentinelFactory.ResolveColorGrant(sentinel, ManaColor.Red, svc);

        // A red would-be blocker can't block; a blue one can (CR 509.1b).
        var redBlocker = new Creature("Goblin", "{R}", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };
        var blueBlocker = new Creature("Drake", "{U}", 2, 2)
        { Owner = _bob, Controller = _bob, Zone = ZoneType.Battlefield };

        BlockLegality.CanBlock(redBlocker, sentinel, out _).Should().BeFalse();
        BlockLegality.CanBlock(blueBlocker, sentinel, out _).Should().BeTrue();
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private void AddCreature(Player owner, string name, int power)
    {
        var c = new Creature(name, "{1}", power, power)
        { Owner = owner, Controller = owner, Zone = ZoneType.Battlefield };
        owner.Zones.Battlefield.AddCard(c);
    }
}
