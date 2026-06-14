using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
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
/// Pays down the <c>creature-onetime-sac-bodies-resolve-time-bus</c> deferral
/// (v1-deferrals item #2 — the creature self-sac bodies residual the central
/// COST seam does NOT reach).
///
/// <para>
/// These one-time creature self-sacrifice bodies (Caustic Caterpillar, Mogg
/// Fanatic, Selfless Spirit, Vampire Hexmage, Cursecatcher, Glen Elendra
/// Archmage, Dauntless Bodyguard, Yavimaya Elder, Mausoleum Wanderer, Insolent
/// Neonate, Fanatical Firebrand, Bottle Gnomes) perform the actual battlefield →
/// graveyard sacrifice INSIDE the RESOLVE closure of their activated ability —
/// NOT (only) as the activation <see cref="AdditionalCost.Sacrifice"/>. The
/// resolve closure historically hand-rolled the zone move (or routed through the
/// bus-less <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard)"/>), so when
/// the ability is resolved directly the sacrifice published NO
/// <see cref="PermanentSacrificedEvent"/> — "whenever a/an [player] sacrifices …"
/// aristocrat payoffs (Mayhem Devil, It That Betrays, Blood Artist) never
/// observed it (CR 701.16a). The central <c>IBusAwareCost</c> cost-payment seam
/// (#2736) only covers the COST leg; it never sees a resolve-closure sacrifice.
/// </para>
///
/// <para>
/// The fix mirrors the Festival-Crasher / Spellbomb / Expedition-Map seam: the
/// effects-aware <c>Create(Player, ContinuousEffectsService?)</c> overload the
/// source generator dispatches in the production routed build threads
/// <c>effects.EventBus</c> into the resolve closure, which now routes the
/// self-sacrifice through the bus-aware
/// <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>
/// overload. The bus-less single-arg overload still sacrifices (publish-nothing
/// legacy posture) for shape / dispatcher tests.
/// </para>
/// </summary>
public class CreatureSelfSacResolveTimeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    /// <summary>
    /// Every creature self-sac body whose RESOLVE closure performs the
    /// sacrifice, built through its effects-aware <c>Create(Player,
    /// ContinuousEffectsService)</c> overload (the source-gen-dispatched prod
    /// build shape).
    /// </summary>
    public static IEnumerable<object[]> ResolveSacBodies() => new[]
    {
        new object[] { "Caustic Caterpillar" },
        new object[] { "Mogg Fanatic" },
        new object[] { "Fanatical Firebrand" },
        new object[] { "Bottle Gnomes" },
        new object[] { "Insolent Neonate" },
        new object[] { "Selfless Spirit" },
        new object[] { "Vampire Hexmage" },
        new object[] { "Cursecatcher" },
        new object[] { "Glen Elendra Archmage" },
        new object[] { "Dauntless Bodyguard" },
        new object[] { "Yavimaya Elder" },
        new object[] { "Mausoleum Wanderer" },
    };

    private static Creature BuildWithEffects(string name, Player owner, ContinuousEffectsService effects) => name switch
    {
        "Caustic Caterpillar" => CausticCaterpillarFactory.Create(owner, effects),
        "Mogg Fanatic" => MoggFanaticFactory.Create(owner, effects),
        "Fanatical Firebrand" => FanaticalFirebrandFactory.Create(owner, effects),
        "Bottle Gnomes" => BottleGnomesFactory.Create(owner, effects),
        "Insolent Neonate" => InsolentNeonateFactory.Create(owner, effects),
        "Selfless Spirit" => SelflessSpiritFactory.Create(owner, effects),
        "Vampire Hexmage" => VampireHexmageFactory.Create(owner, effects),
        "Cursecatcher" => CursecatcherFactory.Create(owner, effects),
        "Glen Elendra Archmage" => GlenElendraArchmageFactory.Create(owner, effects),
        "Dauntless Bodyguard" => DauntlessBodyguardFactory.Create(owner, effects),
        "Yavimaya Elder" => YavimayaElderFactory.Create(owner, effects),
        "Mausoleum Wanderer" => MausoleumWandererFactory.Create(owner, effects),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>
    /// The activated ability whose cost set sacrifices the card itself (every
    /// one of these bodies has exactly one such self-sac activated ability).
    /// </summary>
    private static ActivatedAbility SelfSacAbility(Creature card) =>
        card.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Costs
                .OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice
                    && ReferenceEquals(c.Permanent, card)));

    [Theory]
    [MemberData(nameof(ResolveSacBodies))]
    public void ResolveClosure_WhenBusWired_PublishesPermanentSacrificedEvent(string name)
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var card = BuildWithEffects(name, alice, effects);
        card.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Resolve the self-sac ability DIRECTLY (resolve path — the cost is NOT
        // paid here, so the sacrifice happens inside the resolve closure). This
        // is exactly the leg the central cost seam never sees.
        SelfSacAbility(card).Resolve();

        card.Zone.Should().Be(ZoneType.Graveyard,
            $"'{name}' resolve-closure must sacrifice it to the graveyard");
        seen.Should().ContainSingle(
            $"'{name}' bus-wired resolve closure must publish PermanentSacrificedEvent " +
            "(CR 701.16a) crediting the controller")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == card
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(ResolveSacBodies))]
    public void ResolveClosure_BusLessBuild_StillSacrifices_NoPublish(string name)
    {
        // Legacy posture: bare single-arg NamedCardFactory build (no bus). The
        // resolve closure still sacrifices; nothing is published.
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        var card = (Creature)NamedCardFactory.Create(name, alice);
        card.SetController(alice);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        SelfSacAbility(card).Resolve();

        card.Zone.Should().Be(ZoneType.Graveyard, $"'{name}' still sacrifices without a bus");
        seen.Should().BeEmpty($"'{name}' bus-less build must not publish PermanentSacrificedEvent");
    }

    /// <summary>
    /// Mage's Attendant is the odd one out: it does not sacrifice ITSELF — its
    /// ETB mints a 1/1 Wizard TOKEN carrying a "{1}, Sacrifice this token:
    /// Counter …" activated ability, and that token's sacrifice is paid as the
    /// activation COST (not in a resolve closure). The effects-aware overload
    /// now threads the bus into the token's <see cref="AdditionalCost.Sacrifice"/>
    /// so paying the cost publishes <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a) crediting the cost-payer.
    /// </summary>
    [Fact]
    public void MagesAttendant_WizardToken_SacCost_WhenBusWired_Publishes()
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        // Mint the token directly via the public token builder, threading the
        // bus exactly as the effects-aware Create overload does at ETB.
        var token = MagesAttendantFactory.CreateWizardToken(
            alice, zones: null, stack: null, eventBus: effects.EventBus);
        token.SetController(alice);
        alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var sacCost = token.Abilities
            .OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

        // Pay the sacrifice cost in isolation (the bus-bearing one).
        sacCost.Pay(alice);

        token.Zone.Should().Be(ZoneType.Graveyard);
        seen.Should().ContainSingle("the bus-wired token sac-cost must publish")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == token
                && ev.SacrificingPlayer == alice
                && ev.WasToken);
    }

    [Fact]
    public void MagesAttendant_WizardToken_SacCost_BusLess_NoPublish()
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        // Bus-less token build (legacy shape-only posture).
        var token = MagesAttendantFactory.CreateWizardToken(alice);
        token.SetController(alice);
        alice.Zones.Battlefield.AddCard(token);
        token.SetZone(ZoneType.Battlefield);

        var sacCost = token.Abilities
            .OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs)
            .OfType<AdditionalCost>()
            .First(c => c.CostType == AdditionalCostType.Sacrifice);

        sacCost.Pay(alice);

        token.Zone.Should().Be(ZoneType.Graveyard, "the token still sacrifices");
        seen.Should().BeEmpty("no bus threaded into the bus-less token build");
    }
}
