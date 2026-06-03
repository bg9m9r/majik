using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Inspiring Call (Commander 2013 / reprints, {2}{G}, Instant).
///
/// Oracle text (verified against Scryfall):
///   "Draw a card for each creature you control with a +1/+1 counter on it.
///    Those creatures gain indestructible until end of turn. (Damage and
///    effects that say "destroy" don't destroy them.)"
///
/// Coverage:
/// - Card identity (Instant, green, {2}{G}, CMC 3, owner/controller) loaded
///   from the embedded JSON def via <see cref="CardDefinitionLoader"/>.
/// - SpellDefinition shape — no modes, no X, no target requests (CR 601 — the
///   spell has no targets; it counts a set at resolution).
/// - Resolve: caster draws exactly one card per controlled creature that has
///   a +1/+1 counter on it (CR 121.1), and exactly those creatures gain
///   indestructible until end of turn (CR 702.12 / CR 514.2 cleanup expiry).
/// - Creatures without a +1/+1 counter neither add to the draw count nor gain
///   indestructible.
/// - Zero qualifying creatures → draw 0 (CR — "for each" of an empty set).
/// </summary>
[Trait("Color", "G")]
public class InspiringCallFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // ── Identity + dispatch ────────────────────────────────────────────────

    [Fact]
    public void InspiringCall_HasInstantShape_Green_AtCost2G()
    {
        var card = InspiringCallFactory.Create(_alice);

        card.Name.Should().Be("Inspiring Call");
        card.ManaCost.Should().Be("{2}{G}");
        card.HasType(CardType.Instant).Should().BeTrue();
        CardColors.GetColors(card).Should().Contain(ManaColor.Green);
        card.ManaCostValue.TotalValue.Should().Be(3);
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    // ── SpellDefinition — structural shape ─────────────────────────────────

    [Fact]
    public void InspiringCall_SpellDefinition_HasNoTargets_NoModes_NoX()
    {
        var def = InspiringCallFactory.BuildSpellDefinition(_alice);

        def.HasVariableX.Should().BeFalse();
        def.Modes.Should().BeEmpty();
        def.TargetRequests.Should().BeEmpty();
    }

    // ── Resolve ────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_DrawsOnePerCreatureWithCounter_AndGrantsThoseIndestructible()
    {
        // Two creatures with a +1/+1 counter, one without.
        var withA = NewBattlefieldCreature("Counter A", counters: 1);
        var withB = NewBattlefieldCreature("Counter B", counters: 2); // count by creature, not counters
        var without = NewBattlefieldCreature("No Counter", counters: 0);

        // Library to draw from.
        var l1 = NewLibraryCard("L1");
        var l2 = NewLibraryCard("L2");
        var l3 = NewLibraryCard("L3");

        var effect = InspiringCallFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        // Draw count == number of creatures with a +1/+1 counter (2), NOT the
        // total counters (3). CR 121.1 — "for each creature ... with a +1/+1
        // counter on it".
        _alice.Zones.Hand.GetCards().Should().Equal(new ICard[] { l1, l2 });
        _alice.Zones.Library.GetCards().Should().Equal(new ICard[] { l3 });

        // Only the countered creatures gain indestructible (CR 702.12).
        withA.ActiveEffects!.Compute(withA).Keywords.Should().Contain("Indestructible");
        withB.ActiveEffects!.Compute(withB).Keywords.Should().Contain("Indestructible");
        without.ActiveEffects!.Compute(without).Keywords.Should().NotContain("Indestructible");
    }

    [Fact]
    public void Resolve_NoQualifyingCreatures_DrawsZero_GrantsNothing()
    {
        var plain = NewBattlefieldCreature("Plain", counters: 0);
        NewLibraryCard("L1");

        var effect = InspiringCallFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().HaveCount(1);
        plain.ActiveEffects!.Compute(plain).Keywords.Should().NotContain("Indestructible");
    }

    [Fact]
    public void Resolve_IndestructibleExpiresAtEndOfTurn()
    {
        var cre = NewBattlefieldCreature("Countered", counters: 1);
        NewLibraryCard("L1");
        var svc = cre.ActiveEffects!;

        var effect = InspiringCallFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        svc.Compute(cre).Keywords.Should().Contain("Indestructible");

        // CR 514.2 — end-of-turn cleanup expires the grant.
        svc.ExpireEndOfTurn();

        svc.Compute(cre).Keywords.Should().NotContain("Indestructible");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private Creature NewBattlefieldCreature(string name, int counters)
    {
        var c = new Creature(name, "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
            ActiveEffects = new ContinuousEffectsService(),
        };
        c.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(c);
        if (counters > 0) c.Counters.Add(CounterType.PlusOnePlusOne, counters);
        return c;
    }

    private ICard NewLibraryCard(string name)
    {
        var c = new Sorcery(name, "{0}") { Owner = _alice, Controller = _alice };
        c.SetZone(ZoneType.Library);
        _alice.Zones.Library.AddCard(c);
        return c;
    }
}
