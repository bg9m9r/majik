using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="JoragaTreespeakerFactory"/> (Rise of the
/// Eldrazi, {G}) — the first leveler card, paying down the level-up keyword
/// subsystem.
///
/// Oracle (verified against Scryfall):
///   "Level up {1}{G} ({1}{G}: Put a level counter on this. Level up only as
///    a sorcery.)
///    LEVEL 1-4 / 1/2 / {T}: Add {G}{G}.
///    LEVEL 5+  / 1/4 / Elves you control have '{T}: Add {G}{G}.'"
///
/// Covers: identity, dispatch, the sorcery-speed level-up ability + counter
/// placement, and the band-gated P/T + ability statics across all three
/// bands (0, 1-4, 5+).
/// </summary>
public class JoragaTreespeakerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static int ManaAbilityCount(Creature c) =>
        c.Abilities.OfType<IManaAbility>().Count();

    [Fact]
    public void Joraga_Identity()
    {
        var p = JoragaTreespeakerFactory.Create(_alice);

        p.Name.Should().Be("Joraga Treespeaker");
        p.ManaCost.Should().Be("{G}");
        p.HasType(CardType.Creature).Should().BeTrue();
        p.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        p.HasSubtype(CardSubtype.Druid).Should().BeTrue();
        p.BasePower.Should().Be(1);
        p.BaseToughness.Should().Be(1);
        p.Owner.Should().BeSameAs(_alice);
        p.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Joraga_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Joraga Treespeaker", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Joraga Treespeaker");
        card.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        card.HasSubtype(CardSubtype.Druid).Should().BeTrue();
    }

    [Fact]
    public void Joraga_HasSorcerySpeedLevelUpAbility_CostOneG()
    {
        var p = JoragaTreespeakerFactory.Create(_alice);

        var levelUp = p.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        levelUp.Costs.OfType<ManaCostCost>().Single().Cost
            .Should().Be(ManaCost.Parse("{1}{G}"));
    }

    [Fact]
    public void Joraga_LevelUpAbility_PlacesLevelCounter()
    {
        var p = JoragaTreespeakerFactory.Create(_alice);

        var levelUp = p.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.IsSorcerySpeed);
        foreach (var fx in levelUp.Effects) fx.Execute();

        p.Counters.Count(CounterType.Level).Should().Be(1,
            "resolving Level up puts one level counter on the creature (CR 702.87a)");
    }

    [Fact]
    public void Joraga_Band0_NoCounters_PrintedPT_NoManaAbility()
    {
        var svc = new ContinuousEffectsService();
        var p = JoragaTreespeakerFactory.Create(
            _alice, continuousEffects: svc, eventBus: null, replacements: null);
        p.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        svc.Compute(p);
        p.Power.Should().Be(1, "level 0 — printed 1/1");
        p.Toughness.Should().Be(1);
        // Only the level-up activated ability — no granted MANA ability yet.
        ManaAbilityCount(p).Should().Be(0,
            "the {T}: Add {G}{G} band ability is not granted at level 0 (CR 107.8)");
    }

    [Fact]
    public void Joraga_Band1_OneCounter_Is1Slash2_WithSelfManaAbility()
    {
        var svc = new ContinuousEffectsService();
        var p = JoragaTreespeakerFactory.Create(
            _alice, continuousEffects: svc, eventBus: null, replacements: null);
        p.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        p.Counters.Add(CounterType.Level, 1);
        svc.Compute(p);

        p.Power.Should().Be(1, "{LEVEL 1-4} sets base P/T to 1/2 (CR 107.8)");
        p.Toughness.Should().Be(2);
        ManaAbilityCount(p).Should().Be(1,
            "{LEVEL 1-4} grants the self {T}: Add {G}{G} mana ability");

        var mana = p.Abilities.OfType<IManaAbility>().Single();
        mana.ManaGenerated.Should().Be(ManaCost.Parse("{G}{G}"));
    }

    [Fact]
    public void Joraga_Band5Plus_FiveCounters_Is1Slash4_AnthemGrantsElves()
    {
        var svc = new ContinuousEffectsService();
        var p = JoragaTreespeakerFactory.Create(
            _alice, continuousEffects: svc, eventBus: null, replacements: null);
        p.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        // Another Elf you control — should receive the anthem at level 5+.
        var otherElf = new Creature("Llanowar Elves", "{G}", 1, 1,
            subtypes: new[] { CardSubtype.Elf }) { Owner = _alice, Controller = _alice };
        otherElf.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(otherElf);
        otherElf.SetZone(ZoneType.Battlefield);

        p.Counters.Add(CounterType.Level, 5);
        svc.Compute(p);
        svc.Compute(otherElf);

        p.Power.Should().Be(1, "{LEVEL 5+} sets base P/T to 1/4 (CR 107.8)");
        p.Toughness.Should().Be(4);

        // The {LEVEL 1-4} self ability lifted (band non-cumulative); the 5+
        // anthem grants Joraga (an Elf) one {T}: Add {G}{G}.
        ManaAbilityCount(p).Should().Be(1,
            "{LEVEL 5+} band — the 1-4 self ability lifted; the anthem grants " +
            "Joraga itself one {T}: Add {G}{G} (CR 107.8)");

        // The OTHER Elf you control also gains the anthem ability.
        ManaAbilityCount(otherElf).Should().Be(1,
            "Elves you control have '{T}: Add {G}{G}' at level 5+");
    }

    [Fact]
    public void Joraga_BandTransition_FiveToBelow_LiftsAnthem_RestoresBand1()
    {
        var svc = new ContinuousEffectsService();
        var p = JoragaTreespeakerFactory.Create(
            _alice, continuousEffects: svc, eventBus: null, replacements: null);
        p.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(p);
        p.SetZone(ZoneType.Battlefield);

        var otherElf = new Creature("Llanowar Elves", "{G}", 1, 1,
            subtypes: new[] { CardSubtype.Elf }) { Owner = _alice, Controller = _alice };
        otherElf.ActiveEffects = svc;
        _alice.Zones.Battlefield.AddCard(otherElf);
        otherElf.SetZone(ZoneType.Battlefield);

        p.Counters.Add(CounterType.Level, 5);
        svc.Compute(p);
        svc.Compute(otherElf);
        ManaAbilityCount(otherElf).Should().Be(1, "anthem live at 5+");

        // Drop back to level 4 — anthem lifts; Joraga returns to 1/2 + self mana.
        p.Counters.Remove(CounterType.Level, 1);
        svc.Compute(p);
        svc.Compute(otherElf);

        p.Toughness.Should().Be(2, "back in {LEVEL 1-4} → 1/2");
        ManaAbilityCount(otherElf).Should().Be(0,
            "the 5+ anthem lifts when the level drops below 5 (CR 107.8)");
        ManaAbilityCount(p).Should().Be(1,
            "the {LEVEL 1-4} self mana ability is restored");
    }
}
