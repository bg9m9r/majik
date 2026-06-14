using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the <c>sac-cost-bus-creature-self-sac-bodies</c> deferral — the
/// creature / permanent subset of the sacrifice-bus-thread family. Each card's
/// <c>Sacrifice ~:</c> activated ability carries an
/// <see cref="AdditionalCost.Sacrifice"/> on the permanent itself, but the
/// shape-only <c>Create(Player)</c> overload supplied no <see cref="IEventBus"/>,
/// so paying that sacrifice cost published nothing — "whenever a/an [player]
/// sacrifices …" aristocrat payoffs (Mayhem Devil, Blood Artist, Zulaport
/// Cutthroat, …) never fired on the self-sacrifice activation path.
///
/// <para>The fix mirrors the established Festival-Crasher / Spellbomb seam: an
/// effects-aware <c>Create(Player, ContinuousEffectsService?)</c> overload the
/// source generator dispatches in the production routed build threads
/// <c>effects.EventBus</c> into <see cref="AdditionalCost.Sacrifice(Permanent,
/// IEventBus?)"/> so the self-sacrifice cost publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the cost-payer.
/// The single-arg overload stays bus-less (publish-nothing legacy posture) for
/// shape / dispatcher tests.</para>
/// </summary>
public class CreatureSelfSacBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    private static AdditionalCost SacCost(Card card) =>
        card.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    public static IEnumerable<object[]> SelfSacFactories => new[]
    {
        new object[] { "Caustic Caterpillar",   (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => CausticCaterpillarFactory.Create(o, e)) },
        new object[] { "Bottle Gnomes",         (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => BottleGnomesFactory.Create(o, e)) },
        new object[] { "Selfless Spirit",       (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SelflessSpiritFactory.Create(o, e)) },
        new object[] { "Mogg Fanatic",          (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => MoggFanaticFactory.Create(o, e)) },
        new object[] { "Fanatical Firebrand",   (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => FanaticalFirebrandFactory.Create(o, e)) },
        new object[] { "Insolent Neonate",      (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => InsolentNeonateFactory.Create(o, e)) },
        new object[] { "Cathar Commando",       (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => CatharCommandoFactory.Create(o, e)) },
        new object[] { "Glen Elendra Archmage", (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => GlenElendraArchmageFactory.Create(o, e)) },
        new object[] { "Mausoleum Wanderer",    (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => MausoleumWandererFactory.Create(o, e)) },
        new object[] { "Seal of Fire",          (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SealOfFireFactory.Create(o, e)) },
        new object[] { "Aura of Silence",       (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => AuraOfSilenceFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(SelfSacFactories))]
    public void EffectsAwareOverload_ThreadsBus_SacCostPublishes(
        string name, System.Func<Player, ContinuousEffectsService, Permanent> create)
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var card = create(alice, effects);
        card.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Pay only the sacrifice cost in isolation (the bus-bearing one).
        SacCost(card).Pay(alice);

        card.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == card
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(SelfSacFactories))]
    public void ShapeOnlyOverload_StaysBusLess_NoPublish(
        string name, System.Func<Player, ContinuousEffectsService, Permanent> create)
    {
        _ = create; // shape-only path uses the single-arg dispatcher below
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        // Single-arg overload — no bus threaded (legacy publish-nothing posture).
        var card = (Permanent)Majik.Core.CardData.NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        SacCost(card).Pay(alice);

        card.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }
}
