using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Regression lock for the <c>mana-rock-self-sac-resolve-or-cost-bus-residual</c>
/// deferral.
///
/// <para>
/// The central <see cref="Majik.Core.Costs.IBusAwareCost"/> seam (PR #2736)
/// publishes <see cref="PermanentSacrificedEvent"/> (CR 701.16a) when a
/// <c>"Sacrifice CARDNAME:"</c> activated-ability cost is paid through
/// <see cref="Majik.Core.Costs.CostPayment.PayCosts(Player, IEnumerable{Majik.Core.Costs.ICost}, Majik.Core.Mana.ManaSpendContext, IEventBus)"/>
/// — covered by <see cref="SacCostCentralSeamBusTests"/>. But a subset of the
/// self-sac bodies <b>inline the sacrifice in their RESOLVE closure</b> (not as
/// the paid <see cref="Majik.Core.Costs.AdditionalCost"/>), so on any path where
/// the closure performs the move — e.g. <see cref="ActivatedAbility.Resolve()"/>
/// in the dispatcher / resolve-only harness without a CostPayment drive — the
/// central seam never runs and the sacrifice goes unobserved.
/// </para>
///
/// <para>
/// Traveler's Amulet / Wayfarer's Bauble / Renegade Map already route their
/// resolve-closure <c>SacrificeSelf</c> through
/// <see cref="Majik.Core.Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>
/// when an effects-aware bus was wired at construction. This test nails the
/// residual subset — <b>Hedron Archive + Sojourner's Companion</b> — to the same
/// guarantee: build via the effects-aware <c>Create(Player, IEventBus)</c>
/// overload, run the resolve closure, and assert the closure-time sacrifice
/// publishes a single <see cref="PermanentSacrificedEvent"/> crediting the
/// cost-payer (CR 701.16a).
/// </para>
/// </summary>
public class ResolveTimeSelfSacBusTests
{
    /// <summary>
    /// The deferral's named residual subset — self-sac bodies whose sacrifice
    /// is inlined in the RESOLVE closure. The factory is invoked through a
    /// small adapter so the bus-bearing <c>Create</c> overload is exercised.
    /// </summary>
    public static IEnumerable<object[]> ResolveTimeSelfSacCards() => new[]
    {
        new object[] { "Hedron Archive", (BuildWithBus)((p, bus) => HedronArchiveFactory.Create(p, bus)) },
        new object[] { "Sojourner's Companion", (BuildWithBus)((p, bus) => SojournersCompanionFactory.Create(p, bus)) },
    };

    public delegate Permanent BuildWithBus(Player owner, IEventBus eventBus);

    [Theory]
    [MemberData(nameof(ResolveTimeSelfSacCards))]
    public void ResolveClosure_WithBus_SelfSacrifice_PublishesPermanentSacrificedEvent(
        string cardName, BuildWithBus build)
    {
        // Arrange — build via the effects-aware bus-bearing overload, exactly
        // as the prod GameFacade routed build does (effects.EventBus threaded).
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(sacrificed.Add);

        var perm = build(alice, bus);
        perm.SetController(alice);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        var ability = perm.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Costs
                .OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice
                    && ReferenceEquals(c.Permanent, perm)));

        // Act — resolve the ability WITHOUT paying its costs through
        // CostPayment. The sacrifice therefore happens inside the resolve
        // closure (the residual path the central cost seam does NOT reach).
        ability.Resolve();

        // Assert — the resolve-closure sacrifice routed through the bus.
        perm.Zone.Should().Be(ZoneType.Graveyard,
            $"'{cardName}' resolve-closure self-sacrifice must move it to the graveyard");
        sacrificed.Should().ContainSingle(
            $"'{cardName}' resolve-closure self-sacrifice must publish a single " +
            "PermanentSacrificedEvent when built with an effects-aware bus")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == perm
                && ev.SacrificingPlayer == alice);
    }
}
