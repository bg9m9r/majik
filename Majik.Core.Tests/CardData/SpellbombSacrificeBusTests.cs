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
/// Pays down class-(b) of sacrifice-bus-thread (ctor-signature change to
/// RECEIVE a bus) for the Spellbomb cycle. Each spellbomb's
/// <c>{T}/{cost}, Sacrifice ~</c> activated ability carries an
/// <see cref="AdditionalCost.Sacrifice"/> on the spellbomb itself, but the
/// shape-only <c>Create(Player)</c> overload supplied no
/// <see cref="IEventBus"/>, so paying that sacrifice cost published nothing —
/// "whenever a/an [opponent] sacrifices …" aristocrat payoffs never fired on
/// the activation path.
///
/// <para>The fix mirrors the Festival-Crasher seam: an effects-aware
/// <c>Create(Player, ContinuousEffectsService?)</c> overload the source
/// generator dispatches in the production routed build threads
/// <c>effects.EventBus</c> into <see cref="AdditionalCost.Sacrifice(Permanent,
/// IEventBus?)"/> so the self-sacrifice cost publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
/// cost-payer. The single-arg overload stays bus-less (publish-nothing legacy
/// posture) for shape / dispatcher tests.</para>
/// </summary>
public class SpellbombSacrificeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    private static AdditionalCost SacCost(Artifact bomb) =>
        bomb.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    public static IEnumerable<object[]> SpellbombFactories => new[]
    {
        new object[] { "Pyrite Spellbomb",   (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => PyriteSpellbombFactory.Create(o, e)) },
        new object[] { "Aether Spellbomb",   (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => AetherSpellbombFactory.Create(o, e)) },
        new object[] { "Nihil Spellbomb",    (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => NihilSpellbombFactory.Create(o, e)) },
        new object[] { "Necrogen Spellbomb", (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => NecrogenSpellbombFactory.Create(o, e)) },
        new object[] { "Sunbeam Spellbomb",  (System.Func<Player, ContinuousEffectsService, Artifact>)((o, e) => SunbeamSpellbombFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(SpellbombFactories))]
    public void EffectsAwareOverload_ThreadsBus_SacCostPublishes(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        var alice = new Player("Alice", 20);
        var (bus, effects, seen) = Wired();

        var bomb = create(alice, effects);
        bomb.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        // Pay only the sacrifice cost in isolation (the bus-bearing one).
        SacCost(bomb).Pay(alice);

        bomb.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == bomb
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(SpellbombFactories))]
    public void ShapeOnlyOverload_StaysBusLess_NoPublish(
        string name, System.Func<Player, ContinuousEffectsService, Artifact> create)
    {
        _ = create; // shape-only path uses the single-arg overload below
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        // Single-arg overload — no bus threaded (legacy publish-nothing posture).
        var bomb = (Artifact)Majik.Core.CardData.NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(bomb);
        bomb.SetZone(ZoneType.Battlefield);

        SacCost(bomb).Pay(alice);

        bomb.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }
}
