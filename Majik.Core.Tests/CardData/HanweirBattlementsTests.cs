using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="HanweirBattlementsFactory"/> (Eldritch Moon, Land).
/// Oracle text (verified against Scryfall):
///   "{T}: Add {C}.
///    {R}, {T}: Target creature gains haste until end of turn.
///    {3}{R}{R}, {T}: If you both own and control this land and a creature
///    named Hanweir Garrison, exile them, then meld them into Hanweir, the
///    Writhing Township."
///
/// Mirrors <see cref="DenOfTheBugbearTests"/> / <see cref="ArenaOfGloryFactory"/>
/// shape (utility land: {T}: Add + activated combat ability) and the
/// Kiki-Jiki / Aboleth Spawn target-grant pattern:
/// - Identity (Land, no supertype, name, owner/controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - {T}: Add {C} mana ability.
/// - {R}, {T}: target creature gains haste — ActivatedAbility with a
///   ManaCostCost({R}) + tap cost, one "target creature" TargetRequest, and
///   a resolution effect that registers a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Haste") on the chosen
///   creature's <see cref="Permanent.ActiveEffects"/>.
/// - Meld clause is a documented v1 stub (no meld primitive exists) — the
///   third activated ability is attached structurally with its printed cost
///   so the surface is inspectable, but resolution is a no-op.
/// </summary>
public class HanweirBattlementsTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void HanweirBattlements_Identity()
    {
        var land = HanweirBattlementsFactory.Create(_alice);

        land.Name.Should().Be("Hanweir Battlements");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is plain Land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Hanweir Battlements is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_HanweirBattlements()
    {
        var card = NamedCardFactory.Create("Hanweir Battlements", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Hanweir Battlements");
        card.HasType(CardType.Land).Should().BeTrue();

        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} mana ability is wired");
        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the haste-grant ability and the meld-stub ability are both wired");
    }

    // -----------------------------------------------------------------------
    // {T}: Add {C}
    // -----------------------------------------------------------------------

    [Fact]
    public void HanweirBattlements_ManaAbility_ProducesColorless()
    {
        var land = HanweirBattlementsFactory.Create(_alice);

        // {C} is stored on ManaCost as one Generic pip (same posture as
        // AetherHub's colorless mana ability), with no coloured pips.
        var mana = land.Abilities.OfType<ManaAbility>().Single();
        mana.ManaGenerated.Generic.Should().Be(1,
            "{T}: Add {C} produces one colorless mana");
        mana.ManaGenerated.White.Should().Be(0);
        mana.ManaGenerated.Blue.Should().Be(0);
        mana.ManaGenerated.Black.Should().Be(0);
        mana.ManaGenerated.Red.Should().Be(0);
        mana.ManaGenerated.Green.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // {R}, {T}: Target creature gains haste until end of turn
    // -----------------------------------------------------------------------

    [Fact]
    public void HanweirBattlements_HasteAbility_HasManaAndTapCostAndTargetRequest()
    {
        var land = HanweirBattlementsFactory.Create(_alice);

        var haste = HanweirBattlementsFactory.HasteAbility(land);
        haste.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the haste ability carries one ManaCostCost ({R})");
        haste.Costs.Count(c => c is not ManaCostCost).Should().Be(1,
            "the haste ability also carries the {T} tap cost");
        haste.TargetRequests.Should().ContainSingle(
            "the haste ability targets one creature");
        haste.TargetRequests[0].MinTargets.Should().Be(1);
        haste.TargetRequests[0].MaxTargets.Should().Be(1);
        haste.IsSorcerySpeed.Should().BeFalse(
            "granting haste is instant-speed per oracle");
    }

    [Fact]
    public void HanweirBattlements_HasteAbility_GrantsHasteToChosenCreatureUntilEndOfTurn()
    {
        var land = HanweirBattlementsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var effects = new ContinuousEffectsService();
        bear.ActiveEffects = effects;

        var haste = HanweirBattlementsFactory.HasteAbility(land);
        haste.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });
        foreach (var e in haste.Effects) e.Execute();

        var chars = effects.Compute(bear);
        chars.Keywords.Should().Contain("Haste",
            "the resolved ability grants Haste to the chosen creature");
    }

    // -----------------------------------------------------------------------
    // Meld clause — documented v1 stub
    // -----------------------------------------------------------------------

    [Fact]
    public void HanweirBattlements_MeldAbility_HasPrintedCostAndIsNoOp()
    {
        var land = HanweirBattlementsFactory.Create(_alice);

        var meld = HanweirBattlementsFactory.MeldAbility(land);
        meld.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the meld ability carries one ManaCostCost ({3}{R}{R})");
        meld.Costs.Count(c => c is not ManaCostCost).Should().Be(1,
            "the meld ability also carries the {T} tap cost");

        // v1 stub: resolution is a no-op (no meld primitive exists). Running
        // the effect must not throw.
        var run = () => { foreach (var e in meld.Effects) e.Execute(); };
        run.Should().NotThrow("the meld clause is a documented no-op stub");
    }
}

