using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
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
/// Unit tests for <see cref="TakeUpTheShieldFactory"/>.
///
/// Card: Take Up the Shield — Instant {1}{W} (Theros).
///   Oracle text (verified against Scryfall 2026-06-24):
///     "Put a +1/+1 counter on target creature. It gains lifelink and
///      indestructible until end of turn. (Damage and effects that say
///      "destroy" don't destroy it.)"
///
/// Covers:
///   - Identity: name, Instant type, White colour, mana value 2 ({1}{W}).
///   - SpellDefinition shape: no modes, no X, one 1..1 "target creature" slot.
///   - Resolve: target gains a +1/+1 counter (CR 122) plus Lifelink (CR 702.15)
///     and Indestructible (CR 702.12) until end of turn (CR 613.1f Layer 6).
///   - Both granted keywords expire at end-of-turn cleanup (CR 514.2).
///   - Non-creature target → no-op (no counter, no grants) (CR 608.2b).
/// </summary>
[Trait("Color", "W")]
public class TakeUpTheShieldFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob   = new("Bob", 20);

    // ── Identity ────────────────────────────────────────────────────────────

    [Fact]
    public void TakeUpTheShield_Create_HasInstantShape_White_AtCost1W()
    {
        var card = TakeUpTheShieldFactory.Create(_alice);

        card.Name.Should().Be("Take Up the Shield");
        card.ManaCost.Should().Be("{1}{W}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.White);
        card.ManaCostValue.TotalValue.Should().Be(2, because: "{1}{W} = mana value 2");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void TakeUpTheShield_BuildSpellDefinition_OneTargetCreature_NoModes_NoX()
    {
        var def = TakeUpTheShieldFactory.BuildSpellDefinition(o => o);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1, because: "the spell must target a creature (CR 601.2c)");
        def.TargetRequests[0].MaxTargets.Should().Be(1);
    }

    // ── Resolve ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_PutsCounter_AndGrantsLifelinkAndIndestructible()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBattlefieldCreature("Grizzly Bears", svc);

        ResolveOn(bear);

        // CR 122 — single +1/+1 counter (2/2 → 3/3).
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        var computed = svc.Compute(bear);
        computed.Power.Should().Be(3);
        computed.Toughness.Should().Be(3);

        // CR 613.1f Layer 6 — both keywords granted until end of turn.
        computed.Keywords.Should().Contain("Lifelink");
        computed.Keywords.Should().Contain("Indestructible");
    }

    [Fact]
    public void Resolve_GrantsExpireAtEndOfTurn()
    {
        var svc = new ContinuousEffectsService();
        var bear = NewBattlefieldCreature("Grizzly Bears", svc);

        ResolveOn(bear);

        svc.Compute(bear).Keywords.Should().Contain("Lifelink");
        svc.Compute(bear).Keywords.Should().Contain("Indestructible");

        // CR 514.2 — end-of-turn cleanup expires both grants. The +1/+1
        // counter is permanent and survives.
        svc.ExpireEndOfTurn();

        var after = svc.Compute(bear);
        after.Keywords.Should().NotContain("Lifelink");
        after.Keywords.Should().NotContain("Indestructible");
        bear.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1,
            because: "the +1/+1 counter is permanent, not until-end-of-turn");
    }

    [Fact]
    public void Resolve_NonCreatureTarget_NoOp()
    {
        var aura = new Enchantment("Pacifism", "{1}{W}");
        aura.SetOwner(_bob);
        aura.SetController(_bob);
        aura.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(aura);

        ResolveOn(aura);

        aura.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(0,
            because: "Take Up the Shield can only target a creature (CR 608.2b)");
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private Creature NewBattlefieldCreature(string name, ContinuousEffectsService svc)
    {
        var c = new Creature(name, "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = svc,
        };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        return c;
    }

    private static void ResolveOn(object target)
    {
        var def = TakeUpTheShieldFactory.BuildSpellDefinition(o => o);
        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: null,
            Targets: new IReadOnlyList<object>[] { new[] { target } },
            Mana: ManaPayment.Empty,
            AllPlayers: Array.Empty<Player>());

        var effects = def.EffectFactory(chosen);
        effects.Should().HaveCount(1);
        foreach (var e in effects) e.Execute();
    }
}
