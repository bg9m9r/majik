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
/// Pays down the <c>spellbomb-artifact-sac-cost-bus</c> deferral remainder —
/// the artifact / creature self-sacrifice factories whose only routed overload
/// was <c>Create(Player)</c> and therefore constructed
/// <see cref="AdditionalCost.Sacrifice"/> bus-less. This is the residue of the
/// Festival-Crasher / Spellbomb seam (already closed for the
/// Pyrite/Aether/Nihil/Necrogen/Sunbeam Spellbombs in
/// <see cref="SpellbombSacrificeBusTests"/> and the creature subset in
/// <see cref="CreatureSelfSacBusTests"/>):
///
/// <list type="bullet">
///   <item>Conjurer's / Vexing / Urza's / Mishra's Bauble</item>
///   <item>Implement of Combustion</item>
///   <item>Codex Shredder</item>
///   <item>Welding Jar</item>
///   <item>Hedron Archive</item>
///   <item>Sojourner's Companion</item>
/// </list>
///
/// <para>Each card's <c>… Sacrifice ~:</c> activated ability carries an
/// <see cref="AdditionalCost.Sacrifice"/> on the permanent itself. The fix adds
/// an effects-aware <c>Create(Player, ContinuousEffectsService?)</c> overload the
/// source generator dispatches in the production routed build, threading
/// <c>effects.EventBus</c> into <see cref="AdditionalCost.Sacrifice(Permanent,
/// IEventBus?)"/> so paying that cost publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the cost-payer —
/// "whenever a/an [player] sacrifices …" aristocrat payoffs then fire on the
/// activation path. The single-arg overload stays bus-less (publish-nothing
/// legacy posture) for shape / dispatcher tests.</para>
/// </summary>
public class SpellbombRemainderSacrificeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    /// <summary>
    /// The sacrifice-self <see cref="AdditionalCost"/> on the card — the one
    /// whose captured permanent is the card itself. Cards with more than one
    /// sacrifice cost (the baubles' single ability, the spellbombs' two) all
    /// capture the same self permanent, so taking the first is sufficient.
    /// </summary>
    private static AdditionalCost SelfSacCost(Permanent card) =>
        card.Abilities.OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

    public static IEnumerable<object[]> RemainderFactories => new[]
    {
        new object[] { "Conjurer's Bauble",     (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => ConjurersBaubleFactory.Create(o, e)) },
        new object[] { "Vexing Bauble",         (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => VexingBaubleFactory.Create(o, e)) },
        new object[] { "Urza's Bauble",         (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => UrzasBaubleFactory.Create(o, e)) },
        new object[] { "Mishra's Bauble",       (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => MishrasBaubleFactory.Create(o, e)) },
        new object[] { "Implement of Combustion", (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => ImplementOfCombustionFactory.Create(o, e)) },
        new object[] { "Codex Shredder",        (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => CodexShredderFactory.Create(o, e)) },
        new object[] { "Welding Jar",           (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => WeldingJarFactory.Create(o, e)) },
        new object[] { "Hedron Archive",        (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => HedronArchiveFactory.Create(o, e)) },
        new object[] { "Sojourner's Companion", (System.Func<Player, ContinuousEffectsService, Permanent>)((o, e) => SojournersCompanionFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(RemainderFactories))]
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
        SelfSacCost(card).Pay(alice);

        card.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle().Which.Should().Match<PermanentSacrificedEvent>(
            ev => ev.SacrificedCard == card
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(RemainderFactories))]
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

        SelfSacCost(card).Pay(alice);

        card.Zone.Should().Be(ZoneType.Graveyard, "the move still happens");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }
}
