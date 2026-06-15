using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="DeadOfWinterFactory"/>.
///
/// Card: Dead of Winter — Sorcery {2}{B} (Modern Horizons).
///   "All nonsnow creatures get -X/-X until end of turn, where X is the number
///    of snow permanents you control."
///
/// Snow-keyed, magnitude-driven sibling of Biting Rain (fixed -2/-2). Two
/// behaviours unique to this card are covered here:
///   - X is derived from the number of snow permanents the CASTER controls
///     (CR 109.5 — "you"; CR 205.4 — Snow supertype). Counts every permanent
///     type, not just creatures.
///   - The sweep skips NONSNOW creatures only — snow creatures are immune
///     (CR 205.4). Still symmetric across all players (CR 109.5).
///
/// Plus a single Identity assert (name / type / mana cost) built from the
/// embedded JSON via CardDefinitionLoader/CardDefinitionFactory.
/// </summary>
[Trait("Color", "B")]
public class DeadOfWinterFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void DeadOfWinter_Identity()
    {
        var c = DeadOfWinterFactory.Create(_alice);

        c.Name.Should().Be("Dead of Winter");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void CountSnowPermanents_CountsOnlyCastersSnowPermanents_AcrossAllTypes()
    {
        // Alice: 2 snow lands + 1 snow artifact + 1 nonsnow land = 3 snow.
        AddPermanent(_alice, new Land("Snow-Covered Swamp", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Land("Snow-Covered Island", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Artifact("Coldsteel Heart", "{2}", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Land("Swamp"));
        // Bob's snow permanent must NOT count toward Alice's X (CR 109.5 — "you").
        AddPermanent(_bob, new Land("Snow-Covered Forest", new[] { CardSupertype.Snow }));

        DeadOfWinterFactory.CountSnowPermanents(_alice).Should().Be(3);
        DeadOfWinterFactory.CountSnowPermanents(_bob).Should().Be(1);
    }

    [Fact]
    public void Resolve_AppliesMinusXMinusX_ToNonsnowCreatures_SymmetricallyAcrossPlayers()
    {
        // Alice controls 2 snow permanents → X = 2.
        AddPermanent(_alice, new Land("Snow-Covered Swamp", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Land("Snow-Covered Island", new[] { CardSupertype.Snow }));

        var aliceBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);
        var bobBig = NewCreature(_bob, "Serra Angel", "{3}{W}{W}", 4, 4);

        var effects = DeadOfWinterFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        // -2/-2 hits BOTH players' nonsnow creatures (CR 109.5 — symmetric).
        aliceBear.Toughness.Should().Be(0, "2 - 2 = 0");
        bobBig.Toughness.Should().Be(2, "4 - 2 = 2");
        bobBig.Power.Should().Be(2, "4 - 2 = 2");
        aliceBear.IsDead().Should().BeTrue("toughness 0 is lethal (CR 704.5f)");
        bobBig.IsDead().Should().BeFalse("toughness 2 > 0, alive");
    }

    [Fact]
    public void Resolve_SkipsSnowCreatures_ButHitsNonsnowOnes()
    {
        AddPermanent(_alice, new Land("Snow-Covered Swamp", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Land("Snow-Covered Island", new[] { CardSupertype.Snow }));
        AddPermanent(_alice, new Land("Snow-Covered Mountain", new[] { CardSupertype.Snow }));

        // The snow creature is itself a snow PERMANENT Alice controls, so it
        // adds to X: 3 snow lands + 1 snow creature = X = 4 (CR 205.4 — every
        // snow permanent counts, not just snow lands).
        var snowGolem = NewCreature(_alice, "Phyrexian Snowcrusher", "{6}", 5, 5,
            supertypes: new[] { CardSupertype.Snow });
        var nonsnowBear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        DeadOfWinterFactory.CountSnowPermanents(_alice).Should().Be(4,
            "3 snow lands + the snow creature itself");

        var effects = DeadOfWinterFactory.BuildResolveEffect(new[] { _alice }, _alice);
        foreach (var e in effects) e.Execute();

        // CR 205.4 — snow creature untouched by its own card's nonsnow sweep.
        snowGolem.Power.Should().Be(5, "snow creature is exempt from the nonsnow sweep");
        snowGolem.Toughness.Should().Be(5);
        snowGolem.IsDead().Should().BeFalse();

        // Nonsnow creature takes -4/-4 (X = 4).
        nonsnowBear.Power.Should().Be(-2, "2 - 4 = -2");
        nonsnowBear.Toughness.Should().Be(-2, "2 - 4 = -2");
        nonsnowBear.IsDead().Should().BeTrue("toughness ≤ 0 is lethal (CR 704.5f)");
    }

    [Fact]
    public void Resolve_NoSnowPermanents_IsNoOp_LeavesCreaturesUntouched()
    {
        var bear = NewCreature(_alice, "Grizzly Bears", "{1}{G}", 2, 2);

        var effects = DeadOfWinterFactory.BuildResolveEffect(new[] { _alice, _bob }, _alice);
        foreach (var e in effects) e.Execute();

        // X = 0 → -0/-0 no-op (CR 205.4 — caster controls no snow permanents).
        bear.Power.Should().Be(2);
        bear.Toughness.Should().Be(2);
        bear.IsDead().Should().BeFalse();
    }

    private static void AddPermanent(Player owner, Permanent p)
    {
        p.SetOwner(owner);
        p.SetController(owner);
        p.ActiveEffects = new ContinuousEffectsService();
        owner.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);
    }

    private static Creature NewCreature(
        Player owner, string name, string manaCost, int power, int toughness,
        IEnumerable<CardSupertype>? supertypes = null)
    {
        var c = new Creature(name, manaCost, power, toughness, supertypes);
        c.SetOwner(owner);
        c.SetController(owner);
        c.ActiveEffects = new ContinuousEffectsService();
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }
}
