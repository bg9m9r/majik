using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Closes the <c>enchantment-seal-self-sac-resolve-closure-bus</c> deferral's
/// residual: a self-sacrificing enchantment whose self-sac runs inline in its
/// RESOLVE closure (not only as an <see cref="AdditionalCost"/>) must still
/// publish a <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
/// cost-payer when a bus is threaded.
///
/// <para>
/// The central <see cref="Costs.IBusAwareCost"/> cost-payment seam (#2736) only
/// covers the COST leg — when the live activation path pays the
/// <see cref="AdditionalCost.Sacrifice"/> cost it moves the seal and publishes.
/// But several factories also keep a defensive resolve-closure self-sac
/// fallback (the historic "sacrifice payment is a stub, the closure does the
/// move" posture). Seal of Cleansing / Cindervines / Omen of the Sea routed
/// that fallback through the bus-aware <see cref="Primitives.Fx.Sacrifice(ICard, Player, IEventBus)"/>;
/// Seal of Fire and Aura of Silence were the two stragglers that still did a
/// raw bus-less battlefield→graveyard move, so a sacrifice performed by the
/// resolve closure fired no event.
/// </para>
///
/// <para>
/// This test exercises the closure leg directly: it runs the ability's effect
/// against a seal that is STILL on the battlefield (cost not pre-paid), so the
/// closure's <c>SacrificeSelf</c> is the code that performs the move. With the
/// effects-aware <c>Create(Player, ContinuousEffectsService)</c> overload (the
/// production GameFacade build path) the closure-driven sacrifice must publish.
/// </para>
/// </summary>
public class EnchantmentSealResolveClosureSacBusTests
{
    public static IEnumerable<object[]> ResolveClosureSelfSacFactories => new[]
    {
        new object[] { "Seal of Fire",    (Func<Player, ContinuousEffectsService, Enchantment>)((o, e) => SealOfFireFactory.Create(o, e)) },
        new object[] { "Aura of Silence", (Func<Player, ContinuousEffectsService, Enchantment>)((o, e) => AuraOfSilenceFactory.Create(o, e)) },
    };

    [Theory]
    [MemberData(nameof(ResolveClosureSelfSacFactories))]
    public async System.Threading.Tasks.Task ResolveClosure_SelfSac_WithBus_PublishesPermanentSacrificedEvent(
        string name, Func<Player, ContinuousEffectsService, Enchantment> create)
    {
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);
        var effects = new ContinuousEffectsService(bus);

        var seal = create(alice, effects);
        seal.Name.Should().Be(name);
        alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        // Run the resolution effect WITHOUT paying the sacrifice cost first, so
        // the resolve-closure self-sac fallback is the code that moves the seal.
        var ability = seal.Abilities.OfType<ActivatedAbility>().First();
        var ctx = new ResolutionContext(
            Controller: alice,
            Agent: null,
            Game: null,
            ChosenTargets: Array.Empty<IReadOnlyList<object>>());
        foreach (var effect in ability.Effects)
        {
            await effect.ExecuteAsync(ctx);
        }

        seal.Zone.Should().Be(ZoneType.Graveyard,
            $"'{name}' resolve closure must sacrifice itself");
        seen.Should().ContainSingle(
            $"'{name}' resolve-closure self-sacrifice must publish PermanentSacrificedEvent (CR 701.16a)")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == seal
                && ev.SacrificingPlayer == alice
                && !ev.WasToken);
    }

    [Theory]
    [MemberData(nameof(ResolveClosureSelfSacFactories))]
    public async System.Threading.Tasks.Task ResolveClosure_SelfSac_WithoutBus_StillSacrifices_NoPublish(
        string name, Func<Player, ContinuousEffectsService, Enchantment> create)
    {
        _ = create; // bus-less path uses the single-arg dispatcher
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var seen = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(seen.Add);

        var seal = (Enchantment)Majik.Core.CardData.NamedCardFactory.Create(name, alice);
        alice.Zones.Battlefield.AddCard(seal);
        seal.SetZone(ZoneType.Battlefield);

        var ability = seal.Abilities.OfType<ActivatedAbility>().First();
        var ctx = new ResolutionContext(
            Controller: alice,
            Agent: null,
            Game: null,
            ChosenTargets: Array.Empty<IReadOnlyList<object>>());
        foreach (var effect in ability.Effects)
        {
            await effect.ExecuteAsync(ctx);
        }

        seal.Zone.Should().Be(ZoneType.Graveyard, "the move still happens with no bus");
        seen.Should().BeEmpty("no bus was threaded into the single-arg overload");
    }
}
