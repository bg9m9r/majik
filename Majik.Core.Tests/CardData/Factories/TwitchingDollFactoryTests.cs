using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="TwitchingDollFactory"/>. Artifact Creature — Spider Toy
/// 2/2 ({1}{G}):
///   "{T}: Add one mana of any color. Put a nest counter on this creature.
///    {T}, Sacrifice this creature: Create a 2/2 green Spider creature token
///    with reach for each counter on this creature. Activate only as a
///    sorcery."
///
/// Covers ONLY the card's unique behaviour (plus a single identity assert):
/// - Identity: Artifact Creature, name, {1}{G}, 2/2, Spider + Toy subtypes.
/// - {T} mana ability: five WUBRG ManaAbility slots; activating one adds the
///   matching colour AND places exactly one nest counter (CR 605.1a).
/// - {T}, Sacrifice: sorcery-speed; mints one 2/2 green Spider-with-reach token
///   per counter (snapshot before sac, CR 121.2) and sacrifices the Doll.
/// </summary>
[Trait("Color", "G")]
public class TwitchingDollFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TwitchingDoll_Identity()
    {
        var doll = TwitchingDollFactory.Create(_alice);

        doll.Name.Should().Be("Twitching Doll");
        doll.HasType(CardType.Artifact).Should().BeTrue();
        doll.HasType(CardType.Creature).Should().BeTrue();
        doll.ManaCostValue.TotalValue.Should().Be(2, "Twitching Doll costs {1}{G}");
        doll.Power.Should().Be(2);
        doll.Toughness.Should().Be(2);
        doll.HasSubtype(CardSubtype.Spider).Should().BeTrue();
        doll.HasSubtype(CardSubtype.Toy).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // {T}: Add one mana of any color. Put a nest counter on this creature.
    // -----------------------------------------------------------------------

    [Fact]
    public void TwitchingDoll_ManaAbility_HasFiveWubrgSlots()
    {
        var doll = TwitchingDollFactory.Create(_alice);

        var manaAbilities = doll.Abilities.OfType<ManaAbility>().ToList();
        manaAbilities.Should().HaveCount(5,
            "one mana ability per WUBRG colour (modal 'any color' shape)");
    }

    [Fact]
    public void TwitchingDoll_ActivatingManaAbility_AddsColourAndOneNestCounter()
    {
        var doll = TwitchingDollFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(doll);
        doll.SetZone(ZoneType.Battlefield);
        doll.ClearSummoningSickness();

        // Activate the green slot.
        var green = doll.Abilities.OfType<ManaAbility>()
            .Single(a => a.ManaGenerated.Green == 1);

        var produced = green.Activate();
        produced.Green.Should().Be(1, "activating the green slot adds {G}");

        doll.IsTapped.Should().BeTrue("{T} is the activation cost");
        doll.Counters.Count(CounterType.Nest).Should().Be(1,
            "activating the mana ability also places one nest counter (CR 122)");
    }

    // -----------------------------------------------------------------------
    // {T}, Sacrifice this creature: Create a 2/2 green Spider with reach for
    // each counter. Activate only as a sorcery.
    // -----------------------------------------------------------------------

    [Fact]
    public void TwitchingDoll_SacAbility_IsSorcerySpeedWithTapAndSacrifice()
    {
        var doll = TwitchingDollFactory.Create(_alice);

        var sac = SacAbility(doll);

        sac.IsSorcerySpeed.Should().BeTrue(
            "the sac ability reads 'Activate only as a sorcery' (CR 117.1a)");
        sac.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1, "tap cost");
        sac.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1, "sacrifice cost");
    }

    [Fact]
    public void TwitchingDoll_Sac_CreatesOneSpiderTokenPerCounterAndSacrificesItself()
    {
        var doll = TwitchingDollFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(doll);
        doll.SetZone(ZoneType.Battlefield);

        // Seed three nest counters (e.g. three prior {T} activations).
        doll.Counters.Add(CounterType.Nest, 3);

        var sac = SacAbility(doll);
        foreach (var e in sac.Effects) e.Execute();

        // The Doll is sacrificed (CR 701.16).
        doll.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(doll);

        // Three 2/2 green Spider tokens with reach were minted.
        var spiders = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.HasSubtype(CardSubtype.Spider))
            .ToList();

        spiders.Should().HaveCount(3, "one Spider token per counter on the Doll");
        spiders.Should().AllSatisfy(s =>
        {
            s.Power.Should().Be(2);
            s.Toughness.Should().Be(2);
            CardColors.GetColors(s).Should().Contain(ManaColor.Green);
            s.Abilities.OfType<KeywordAbility>()
                .Any(k => k.Keyword.Equals("Reach", System.StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue("each token has reach (CR 702.17)");
        });
    }

    [Fact]
    public void TwitchingDoll_Sac_WithNoCounters_CreatesNoTokens()
    {
        var doll = TwitchingDollFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(doll);
        doll.SetZone(ZoneType.Battlefield);

        var sac = SacAbility(doll);
        foreach (var e in sac.Effects) e.Execute();

        doll.Zone.Should().Be(ZoneType.Graveyard, "the Doll is still sacrificed");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(c => c.IsToken && c.HasSubtype(CardSubtype.Spider))
            .Should().Be(0, "no counters → no tokens");
    }

    [Fact]
    public void Sac_OnProdPath_PublishesPermanentSacrificedEvent()
    {
        // The effects-aware Create(Player, ContinuousEffectsService) overload
        // the source-gen routes on the prod GameFacade build threads
        // effects.EventBus so the sac ability publishes
        // PermanentSacrificedEvent (CR 701.16a).
        var bus = new global::Majik.Core.Events.EventBus();
        var effects = new global::Majik.Core.Effects.ContinuousEffectsService(bus);

        var captured = new List<global::Majik.Core.Events.PermanentSacrificedEvent>();
        bus.Subscribe<global::Majik.Core.Events.PermanentSacrificedEvent>(captured.Add);

        var built = NamedCardFactory.Create("Twitching Doll", _alice, effects);
        built.Should().BeAssignableTo<Creature>();
        var doll = (Creature)built;
        _alice.Zones.Battlefield.AddCard(doll);
        doll.SetZone(ZoneType.Battlefield);

        var sac = SacAbility(doll);
        sac.Resolve();

        captured.Should().ContainSingle()
            .Which.SacrificingPlayer.Should().BeSameAs(_alice);
        _alice.Zones.Graveyard.GetCards().Should().Contain(doll);
    }

    private static ActivatedAbility SacAbility(Creature doll) =>
        doll.Abilities.OfType<ActivatedAbility>().Single(a =>
            a.Costs.OfType<AdditionalCost>().Any(c => c.CostType == AdditionalCostType.Sacrifice));
}
