using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Banner of Kinship (Modern Horizons 3 — Artifact {5}).
///
/// Oracle (verified against Scryfall 2026-06-24):
///   "As this artifact enters, choose a creature type. This artifact enters
///    with a fellowship counter on it for each creature you control of the
///    chosen type.
///    Creatures you control of the chosen type get +1/+1 for each fellowship
///    counter on this artifact."
///
/// Coverage:
///   * Identity: Artifact, {5}.
///   * Unwired single-arg path: no chosen type, no counters, no anthem.
///   * As-enters type choice stored + exposed via GetChosenType.
///   * ETB loads one fellowship counter per controlled creature of the chosen
///     type (CR 614.1d) — and ignores other-type / opponent creatures.
///   * The chosen-type anthem grants +N/+N where N = fellowship-counter count
///     (CR 613.7c), and tracks the count live when more counters are added.
///   * A creature NOT of the chosen type is unaffected.
///   * The Banner leaving the battlefield lifts the buff.
/// </summary>
[Trait("Color", "C")]
public class BannerOfKinshipFactoryTests
{
    private readonly EventBus _bus = new();
    private readonly ReplacementBus _replacements = new();
    private readonly ZoneService _zones;
    private readonly ContinuousEffectsService _effects = new();
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    public BannerOfKinshipFactoryTests()
    {
        _zones = new ZoneService(_bus, _replacements);
    }

    private static System.Func<Player, CardSubtype> Choose(CardSubtype t) => _ => t;

    private Creature CreatureFor(Player owner, string name, CardSubtype subtype, int p = 2, int t = 2)
    {
        var c = new Creature(name, "{1}{G}", p, t, subtypes: new[] { subtype })
        {
            Owner = owner,
            Controller = owner,
            ActiveEffects = _effects,
        };
        owner.Zones.Battlefield.AddCard(c);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    /// <summary>
    /// Build a fully-wired Banner choosing <paramref name="chosen"/> and route
    /// it Stack → Battlefield so the CR 614.1d ETB-counter replacement fires.
    /// </summary>
    private Artifact ResolveBanner(CardSubtype chosen)
    {
        var banner = BannerOfKinshipFactory.Create(_alice, _replacements, _effects, Choose(chosen));
        banner.ActiveEffects = _effects;
        banner.SetZone(ZoneType.Stack);
        _alice.Zones.Stack.AddCard(banner);
        _zones.MoveCard(banner, ZoneType.Stack, ZoneType.Battlefield, controller: _alice);
        return banner;
    }

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void BannerOfKinship_IsArtifact_AtCost5()
    {
        var c = BannerOfKinshipFactory.Create(_alice);

        c.Name.Should().Be("Banner of Kinship");
        c.ManaCost.Should().Be("{5}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeFalse("Banner of Kinship is a non-creature Artifact");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void BannerOfKinship_SingleArgPath_NoChoice_NoAnthem_NoCounters()
    {
        var c = BannerOfKinshipFactory.Create(_alice);

        BannerOfKinshipFactory.GetChosenType(c).Should().BeNull(
            "the single-arg path resolves no creature-type choice");
        c.Counters.Count(CounterType.Fellowship).Should().Be(0);
    }

    [Fact]
    public void BannerOfKinship_StoresChosenType()
    {
        var c = BannerOfKinshipFactory.Create(_alice, _replacements, _effects, Choose(CardSubtype.Goblin));

        BannerOfKinshipFactory.GetChosenType(c).Should().Be(CardSubtype.Goblin);
    }

    // -----------------------------------------------------------------------
    // CR 614.1d — enters WITH a fellowship counter per controlled creature
    // of the chosen type.
    // -----------------------------------------------------------------------

    [Fact]
    public void Etb_LoadsOneFellowshipCounterPerControlledCreatureOfChosenType()
    {
        // Two Goblins you control, one Bear you control, one Goblin an opponent
        // controls. Only the two own Goblins count.
        CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);
        CreatureFor(_alice, "Goblin Raider", CardSubtype.Goblin);
        CreatureFor(_alice, "Grizzly Bears", CardSubtype.Bear);
        CreatureFor(_bob, "Goblin Piker", CardSubtype.Goblin);

        var banner = ResolveBanner(CardSubtype.Goblin);

        banner.Zone.Should().Be(ZoneType.Battlefield);
        banner.Counters.Count(CounterType.Fellowship).Should().Be(2,
            "CR 614.1d — one fellowship counter per creature YOU control of the chosen type (two Goblins)");
    }

    [Fact]
    public void Etb_NoControlledCreaturesOfChosenType_EntersWithZeroCounters()
    {
        CreatureFor(_alice, "Grizzly Bears", CardSubtype.Bear);

        var banner = ResolveBanner(CardSubtype.Goblin);

        banner.Counters.Count(CounterType.Fellowship).Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // CR 613.7c — "+1/+1 for each fellowship counter on this artifact."
    // -----------------------------------------------------------------------

    [Fact]
    public void CreatureOfChosenType_GetsPlusNPlusN_WhereNIsFellowshipCount()
    {
        // One Goblin present at ETB → Banner enters with one fellowship counter.
        var goblin = CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);

        ResolveBanner(CardSubtype.Goblin);

        goblin.GetPower().Should().Be(3, "+1/+1 for the single fellowship counter (CR 613.7c)");
        goblin.GetToughness().Should().Be(3);
    }

    [Fact]
    public void Anthem_TracksFellowshipCounterCount_Live()
    {
        var goblin = CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);

        var banner = ResolveBanner(CardSubtype.Goblin);

        // One counter loaded at ETB → +1/+1.
        goblin.GetPower().Should().Be(3);

        // Add two more fellowship counters; the dynamic-boost lord re-samples the
        // live count each layer pass (counter mutation bumps the effect
        // generation, CR 613), so the buff grows to +3/+3.
        banner.Counters.Add(CounterType.Fellowship, 2);

        goblin.GetPower().Should().Be(5, "now +3/+3 for three fellowship counters");
        goblin.GetToughness().Should().Be(5);
    }

    [Fact]
    public void CreatureOfDifferentType_IsUnaffected()
    {
        CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);
        var bear = CreatureFor(_alice, "Grizzly Bears", CardSubtype.Bear);

        ResolveBanner(CardSubtype.Goblin);

        bear.GetPower().Should().Be(2, "a Bear is not the chosen creature type (Goblin)");
        bear.GetToughness().Should().Be(2);
    }

    [Fact]
    public void OpponentCreatureOfChosenType_IsUnaffected()
    {
        CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);
        var oppGoblin = CreatureFor(_bob, "Goblin Raider", CardSubtype.Goblin);

        ResolveBanner(CardSubtype.Goblin);

        oppGoblin.GetPower().Should().Be(2,
            "'Creatures YOU control' is controller-scoped (CR 109.5)");
        oppGoblin.GetToughness().Should().Be(2);
    }

    [Fact]
    public void BannerLeavesBattlefield_BuffLifts()
    {
        var goblin = CreatureFor(_alice, "Goblin Piker", CardSubtype.Goblin);
        var banner = ResolveBanner(CardSubtype.Goblin);

        goblin.GetPower().Should().Be(3);

        // CR 613 — the lord's continuous effect stops applying when its source
        // leaves the battlefield (LordStaticEffect.IsActive gate).
        banner.SetZone(ZoneType.Graveyard);

        goblin.GetPower().Should().Be(2, "buff lifts on LTB");
        goblin.GetToughness().Should().Be(2);
    }
}
