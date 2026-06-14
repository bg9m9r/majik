using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Pays down the <c>utility-artifact-self-sac-resolve-closure-bus</c> deferral
/// (v1-deferrals — the artifact self-sac bodies residual the central COST seam
/// does NOT reach).
///
/// <para>
/// These utility artifacts (Mindslaver, Lantern of Insight, Codex Shredder,
/// Welding Jar, Implement of Combustion) perform the actual battlefield →
/// graveyard sacrifice INSIDE the RESOLVE closure of their activated ability —
/// NOT (only) as the activation <see cref="AdditionalCost.Sacrifice"/>. The
/// resolve closure historically hand-rolled the zone move directly, so when the
/// ability is resolved on the resolve leg the sacrifice published NO
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) — aristocrat payoffs
/// (Mayhem Devil, It That Betrays, Blood Artist) never observed it. The central
/// <c>IBusAwareCost</c> cost-payment seam (#2736) only covers the COST leg; it
/// never sees a resolve-closure sacrifice.
/// </para>
///
/// <para>
/// The fix mirrors the Festival-Crasher / Spellbomb / creature-self-sac seam
/// (#2757): the effects-aware <c>Create(Player, ContinuousEffectsService?)</c>
/// overload the source generator dispatches in the production routed build
/// threads <c>effects.EventBus</c> into the resolve closure, which now routes
/// the self-sacrifice through the bus-aware
/// <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>
/// overload. The bus-less single-arg overload still sacrifices (publish-nothing
/// legacy posture) for shape / dispatcher tests. The on-battlefield guard keeps
/// it idempotent so the cost-seam publish and the resolve publish never both
/// fire.
/// </para>
///
/// <para>
/// Goblin Engineer is the odd one out: its activated ability does not sacrifice
/// ITSELF — it sacrifices ANOTHER artifact ("Sacrifice an artifact") inside the
/// resolve closure, which the effects-aware overload already routes through the
/// bus-aware <c>Fx.Sacrifice</c>. Covered by a dedicated test.
/// </para>
/// </summary>
public class UtilityArtifactSelfSacResolveTimeBusTests
{
    private static (EventBus bus, ContinuousEffectsService effects, List<PermanentSacrificedEvent> seen) Wired()
    {
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        return (bus, new ContinuousEffectsService(bus), seen);
    }

    /// <summary>
    /// Every utility-artifact self-sac body whose RESOLVE closure performs the
    /// sacrifice of ITSELF, built through its effects-aware
    /// <c>Create(Player, ContinuousEffectsService)</c> overload (the
    /// source-gen-dispatched prod build shape).
    /// </summary>
    public static IEnumerable<object[]> SelfSacArtifacts() => new[]
    {
        new object[] { "Mindslaver" },
        new object[] { "Lantern of Insight" },
        new object[] { "Codex Shredder" },
        new object[] { "Welding Jar" },
        new object[] { "Implement of Combustion" },
    };

    private static Artifact BuildWithEffects(string name, Player owner, ContinuousEffectsService effects) => name switch
    {
        "Mindslaver" => MindslaverFactory.Create(owner, effects),
        "Lantern of Insight" => LanternOfInsightFactory.Create(owner, effects),
        "Codex Shredder" => CodexShredderFactory.Create(owner, effects),
        "Welding Jar" => WeldingJarFactory.Create(owner, effects),
        "Implement of Combustion" => ImplementOfCombustionFactory.Create(owner, effects),
        _ => throw new System.ArgumentOutOfRangeException(nameof(name), name, null),
    };

    /// <summary>
    /// The activated ability whose cost set sacrifices the card itself (every
    /// one of these bodies has exactly one such self-sac activated ability).
    /// </summary>
    private static ActivatedAbility SelfSacAbility(Artifact card) =>
        card.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Costs
                .OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice
                    && ReferenceEquals(c.Permanent, card)));

    [Theory]
    [MemberData(nameof(SelfSacArtifacts))]
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
    [MemberData(nameof(SelfSacArtifacts))]
    public void ResolveClosure_BusLessBuild_StillSacrifices_NoPublish(string name)
    {
        // Legacy posture: bare single-arg NamedCardFactory build (no bus). The
        // resolve closure still sacrifices; nothing is published.
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        var card = (Artifact)NamedCardFactory.Create(name, alice);
        card.SetController(alice);
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        SelfSacAbility(card).Resolve();

        card.Zone.Should().Be(ZoneType.Graveyard, $"'{name}' still sacrifices without a bus");
        seen.Should().BeEmpty($"'{name}' bus-less build must not publish PermanentSacrificedEvent");
    }

    /// <summary>
    /// Goblin Engineer's "{R}, {T}, Sacrifice an artifact: ..." activated
    /// ability sacrifices ANOTHER artifact (not itself) inside its resolve
    /// closure. The effects-aware overload threads the bus so the sacrificed
    /// artifact publishes <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the controller — already wired (#regression guard).
    /// </summary>
    [Fact]
    public void GoblinEngineer_ResolveClosure_SacrificesAnotherArtifact_WhenBusWired_Publishes()
    {
        var alice = new Player("Alice", 20);
        var (_, effects, seen) = Wired();

        var engineer = GoblinEngineerFactory.Create(alice, effects);
        alice.Zones.Battlefield.AddCard(engineer);
        engineer.SetZone(ZoneType.Battlefield);

        // A separate artifact on the battlefield to feed the "Sacrifice an
        // artifact" cost.
        var fodder = new Artifact("Ornithopter", "{0}");
        fodder.SetOwner(alice);
        fodder.SetController(alice);
        alice.Zones.Battlefield.AddCard(fodder);
        fodder.SetZone(ZoneType.Battlefield);

        // An artifact card in the graveyard to reanimate (so the closure does
        // not early-out before the sacrifice).
        var graveArtifact = new Artifact("Memnite", "{1}");
        graveArtifact.SetOwner(alice);
        alice.Zones.Graveyard.AddCard(graveArtifact);
        graveArtifact.SetZone(ZoneType.Graveyard);

        var ability = engineer.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any());
        ability.Resolve();

        fodder.Zone.Should().Be(ZoneType.Graveyard,
            "Goblin Engineer's resolve closure must sacrifice the chosen artifact");
        seen.Should().ContainSingle("the bus-wired sacrifice-an-artifact closure must publish")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == fodder
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }
}
