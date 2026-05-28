using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MarchOfWretchedSorrowFactory"/>
/// (Strixhaven, {X}{B}).
///
/// Instant. Oracle text:
///   "As an additional cost to cast this spell, you may exile any number
///    of black cards from your hand. This spell costs {2} less to cast
///    for each card exiled this way.
///    March of Wretched Sorrow deals X damage to target creature or
///    planeswalker and you gain X life."
///
/// Covers:
///   - Identity / shape / NamedCardFactory dispatch.
///   - SpellDefinition: HasVariableX=true, one "target creature or
///     planeswalker" request.
///   - Resolution: X damage to target creature → controller gains X life.
///   - Resolution: X damage to target planeswalker (loyalty) → caster
///     gains X life.
///   - X = 0: caster doesn't gain life (X = 0 short-circuits).
///   - BuildAdditionalCost helper wires the MarchAdditionalCost cleanly.
/// </summary>
public class MarchOfWretchedSorrowFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static void PutOnBattlefield(Player owner, Card card)
    {
        card.SetOwner(owner);
        card.SetController(owner);
        owner.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
    }

    private static object IdentityResolver(object t) => t;

    // ── identity ────────────────────────────────────────────────────────────

    [Fact]
    public void Create_ShipsInstantShape_XB_Black()
    {
        var march = MarchOfWretchedSorrowFactory.Create(_alice);

        march.Should().BeOfType<Instant>();
        march.Name.Should().Be("March of Wretched Sorrow");
        march.ManaCost.Should().Be("{X}{B}");
        march.HasType(CardType.Instant).Should().BeTrue();
        march.Owner.Should().BeSameAs(_alice);
        march.Controller.Should().BeSameAs(_alice);
        CardColors.GetColors(march).Should().Contain(ManaColor.Black);
    }

    [Fact]
    public void NamedCardFactory_Dispatch_ReturnsMarchShape()
    {
        var dispatched = NamedCardFactory.Create("March of Wretched Sorrow", _alice);

        dispatched.Should().BeOfType<Instant>();
        dispatched.Name.Should().Be("March of Wretched Sorrow");
        dispatched.ManaCost.Should().Be("{X}{B}");
    }

    // ── SpellDefinition shape ───────────────────────────────────────────────

    [Fact]
    public void BuildSpellDefinition_HasVariableX_AndCreatureOrPwTarget()
    {
        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, IdentityResolver);

        def.HasVariableX.Should().BeTrue();
        def.TargetRequests.Should().HaveCount(1);
        def.TargetRequests[0].MinTargets.Should().Be(1);
        def.TargetRequests[0].MaxTargets.Should().Be(1);
        def.TargetRequests[0].Description.Should().Be("target creature or planeswalker");
    }

    // ── resolution — creature target ────────────────────────────────────────

    [Fact]
    public void Resolve_CreatureTarget_DealsXDamage_AndCasterGainsXLife()
    {
        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, IdentityResolver);

        var ogre = new Creature("Ogre", "{3}{R}", 4, 3);
        PutOnBattlefield(_bob, ogre);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 3,
            Targets: new IReadOnlyList<object>[] { new object[] { ogre } },
            Mana: ManaPayment.Empty);

        var aliceLifeBefore = _alice.LifeTotal;

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        ogre.Damage.Should().Be(3, "primary damage — X=3 to target creature (CR 119.2)");
        _alice.LifeTotal.Should().Be(aliceLifeBefore + 3,
            "caster gains X life (CR 119.4) in the same resolution step");
    }

    // ── resolution — planeswalker target ────────────────────────────────────

    [Fact]
    public void Resolve_PlaneswalkerTarget_RemovesLoyalty_AndCasterGainsXLife()
    {
        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, IdentityResolver);

        var liliana = new Planeswalker("Liliana of the Veil", "{1}{B}{B}", 3);
        PutOnBattlefield(_bob, liliana);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 2,
            Targets: new IReadOnlyList<object>[] { new object[] { liliana } },
            Mana: ManaPayment.Empty);

        var aliceLifeBefore = _alice.LifeTotal;

        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        liliana.Loyalty.Should().Be(1,
            "Planeswalker damage routes to loyalty (CR 306.7) — 3 - 2 = 1");
        _alice.LifeTotal.Should().Be(aliceLifeBefore + 2,
            "caster gains X life regardless of target type");
    }

    // ── resolution — X = 0 ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_XZero_NoDamage_NoLifeGain()
    {
        var def = MarchOfWretchedSorrowFactory.BuildSpellDefinition(_alice, IdentityResolver);

        var ogre = new Creature("Ogre", "{3}{R}", 4, 3);
        PutOnBattlefield(_bob, ogre);

        var chosen = new ChosenSpellParams(
            ModeIndex: null,
            X: 0,
            Targets: new IReadOnlyList<object>[] { new object[] { ogre } },
            Mana: ManaPayment.Empty);

        var aliceLifeBefore = _alice.LifeTotal;
        foreach (var e in def.EffectFactory(chosen)) e.Execute();

        ogre.Damage.Should().Be(0);
        _alice.LifeTotal.Should().Be(aliceLifeBefore);
    }

    // ── helper — BuildAdditionalCost ────────────────────────────────────────

    [Fact]
    public void BuildAdditionalCost_WiresBlackMarchCost()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);
        var blackCard = new Creature("Black Helper", "{1}{B}", 1, 1);
        blackCard.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(blackCard);
        blackCard.SetZone(ZoneType.Hand);

        var cost = MarchOfWretchedSorrowFactory.BuildAdditionalCost(
            spell, new ICard[] { blackCard });

        cost.Should().BeOfType<MarchAdditionalCost>();
        cost.RequiredColor.Should().Be(ManaColor.Black);
        cost.ExiledCount.Should().Be(1);
        cost.ReductionAmount.Should().Be(2);
        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void BuildAdditionalCost_EmptyList_IsLegal_NoReduction()
    {
        var spell = MarchOfWretchedSorrowFactory.Create(_alice);

        var cost = MarchOfWretchedSorrowFactory.BuildAdditionalCost(
            spell, Array.Empty<ICard>());

        cost.ExiledCount.Should().Be(0);
        cost.ReductionAmount.Should().Be(0);
        cost.CanPay(_alice).Should().BeTrue("March is OPTIONAL — zero exiles is legal");
    }
}
