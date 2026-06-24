using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Get a Leg Up (Bloomburrow, {G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Until end of turn, target creature gets +1/+1 for each creature you
///    control and gains reach."
///
/// Get a Leg Up = Distortion Strike's single-target pump body composed with a
/// count-scaled magnitude (CR 608.2 — "for each creature you control" read once
/// at resolution) and Atarka's Command's reach grant (CR 702.17).
///
/// Coverage:
/// - Identity ({G} Instant, name, owner/controller, green) loaded from the
///   embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - SpellDefinition shape — single 1..1 "target creature" request, no modes,
///   no X (CR 601).
/// - Resolve: target gets +N/+N where N = creatures the caster controls
///   (CR 608.2 / CR 613.1g), including the target itself when it is the
///   caster's creature (CR 109.5).
/// - Resolve: target gains reach until end of turn (CR 702.17).
/// - Both grants expire at end of turn (CR 514.2).
/// - Illegal target (creature off the battlefield) → no-op (CR 608.2b).
/// </summary>
[Trait("Color", "G")]
public class GetALegUpFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob",   20);

    // ── Identity ───────────────────────────────────────────────────────────

    [Fact]
    public void GetALegUp_Identity_InstantAtG_Green()
    {
        var card = GetALegUpFactory.Create(_alice);

        card.Name.Should().Be("Get a Leg Up");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
        card.ManaCostValue.TotalValue.Should().Be(1);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
    }

    // ── SpellDefinition shape ──────────────────────────────────────────────

    [Fact]
    public void GetALegUp_SpellDefinition_HasSingleTargetCreatureRequest_NoModes_NoX()
    {
        var def = GetALegUpFactory.BuildSpellDefinition(_alice, resolver: x => x);

        def.Modes.Should().BeEmpty();
        def.HasVariableX.Should().BeFalse();
        def.TargetRequests.Should().HaveCount(1);
        var req = def.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("creature");
    }

    // ── Pump scales by creature count ──────────────────────────────────────

    [Fact]
    public void Resolve_PumpScalesByCreaturesYouControl_IncludingTarget()
    {
        // Caster controls three creatures (the 2/2 target + two others).
        var target = NewBattlefieldCreature(_alice, "Target", 2, 2);
        NewBattlefieldCreature(_alice, "Ally A", 1, 1);
        NewBattlefieldCreature(_alice, "Ally B", 1, 1);
        // An opponent creature must NOT count toward "you control".
        NewBattlefieldCreature(_bob, "Enemy", 5, 5);

        var def = GetALegUpFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var e in def.EffectFactory(Chosen(target))) e.Execute();

        // N = 3 creatures Alice controls → +3/+3 on a 2/2 → 5/5 (CR 608.2 /
        // CR 613.1g; the target is one of the three, CR 109.5).
        target.GetPower().Should().Be(5);
        target.GetToughness().Should().Be(5);
    }

    // ── Reach grant ────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TargetGainsReach()
    {
        var target = NewBattlefieldCreature(_alice, "Target", 2, 2);

        var def = GetALegUpFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var e in def.EffectFactory(Chosen(target))) e.Execute();

        // CR 702.17 — the target gains reach until end of turn.
        target.ActiveEffects!.Compute(target).Keywords.Should().Contain("Reach");
    }

    // ── End-of-turn expiry ─────────────────────────────────────────────────

    [Fact]
    public void Resolve_PumpAndReach_ExpireAtEndOfTurn()
    {
        var target = NewBattlefieldCreature(_alice, "Target", 2, 2);
        NewBattlefieldCreature(_alice, "Ally", 1, 1);
        var svc = target.ActiveEffects!;

        var def = GetALegUpFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var e in def.EffectFactory(Chosen(target))) e.Execute();

        target.GetPower().Should().Be(4); // 2/2 + N=2
        svc.Compute(target).Keywords.Should().Contain("Reach");

        // CR 514.2 — "until end of turn" effects expire in cleanup.
        svc.ExpireEndOfTurn();

        target.GetPower().Should().Be(2, "the pump expires at end of turn (CR 514.2)");
        target.GetToughness().Should().Be(2);
        svc.Compute(target).Keywords.Should().NotContain("Reach",
            "the reach grant expires at end of turn (CR 514.2)");
    }

    // ── Illegal target ─────────────────────────────────────────────────────

    [Fact]
    public void Resolve_TargetNotOnBattlefield_NoOp()
    {
        var dead = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        { Owner = _alice, Controller = _alice, ActiveEffects = new ContinuousEffectsService() };
        dead.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(dead);
        NewBattlefieldCreature(_alice, "Ally", 1, 1);

        var def = GetALegUpFactory.BuildSpellDefinition(_alice, resolver: x => x);
        foreach (var e in def.EffectFactory(Chosen(dead))) e.Execute();

        dead.GetPower().Should().Be(2, "the pump no-ops off the battlefield (CR 608.2b)");
        dead.ActiveEffects!.Compute(dead).Keywords.Should().NotContain("Reach",
            "the reach grant no-ops off the battlefield (CR 608.2b)");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private ChosenSpellParams Chosen(object target) => new(
        ModeIndex: null,
        X:         null,
        Targets:   new[] { (IReadOnlyList<object>)new object[] { target } },
        Mana:      ManaPayment.Empty);

    private Creature NewBattlefieldCreature(Player owner, string name, int p, int t)
    {
        var c = new Creature(name, "{1}{G}", p, t)
        {
            Owner = owner,
            Controller = owner,
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }
}
