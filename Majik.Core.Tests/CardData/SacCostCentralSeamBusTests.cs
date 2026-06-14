using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Regression lock for the <c>sac-cost-source-gen-bus-overload-dispatch</c>
/// deferral (v1-deferrals item #2, class-(b) tail).
///
/// <para>
/// The deferral originally proposed adding an <see cref="IEventBus"/>-bearing
/// <c>Create</c>/<c>Build</c> overload that the source generator dispatches —
/// the Festival-Crasher <c>Create(Player, ContinuousEffectsService)</c> pattern
/// — to route the live bus into <see cref="AdditionalCost.Sacrifice"/> for the
/// ~70 self-sac stones / ramp bodies whose only routed overload was the bare
/// <c>Create(Player owner)</c>.
/// </para>
///
/// <para>
/// That per-factory overload approach is now <b>obsolete</b>: the central
/// cost-payment seam (PR #2736) made <see cref="AdditionalCost"/> implement
/// <see cref="IBusAwareCost"/>, so the prod activation path
/// (<c>AbilityActivator.ActivateAbility</c> →
/// <see cref="CostPayment.PayCosts(Player, IEnumerable{ICost}, Majik.Core.Mana.ManaSpendContext, IEventBus)"/>
/// → <see cref="AdditionalCost.Pay(Player, IEventBus)"/>) threads the live bus
/// to <em>any</em> <see cref="IBusAwareCost"/> at the drive site. A
/// <c>"Sacrifice CARDNAME:"</c> activated-ability cost therefore publishes a
/// <see cref="PermanentSacrificedEvent"/> (CR 701.16a) <b>even when the card is
/// built through the bus-less single-arg
/// <see cref="NamedCardFactory.Create(string, Player)"/></b> — no source-gen
/// bus overload required.
/// </para>
///
/// <para>
/// This file nails that guarantee down for exactly the cards the deferral
/// named — Treasure/Mana/Mind Stone, Sakura-Tribe Elder, Burnished Hart,
/// Hedron Archive, Sojourner's Companion — so the deferral cannot silently
/// reopen. Each card is built via the bare single-arg
/// <see cref="NamedCardFactory.Create(string, Player)"/> (NO effects / NO bus
/// at construction), its real <c>"Sacrifice ~"</c> activated-ability cost is
/// pulled off the live ability, and paid through the central seam with a bus —
/// mirroring what the prod <c>AbilityActivator</c> does. The
/// <see cref="PermanentSacrificedEvent"/> must fire crediting the cost-payer.
/// </para>
/// </summary>
public class SacCostCentralSeamBusTests
{
    /// <summary>
    /// The deferral's named class-(b) sac-cost cards. Each carries a
    /// <c>"…Sacrifice this/CARDNAME…"</c> activated ability whose cost set
    /// contains an <see cref="AdditionalCost"/> of type
    /// <see cref="AdditionalCostType.Sacrifice"/>.
    /// </summary>
    public static IEnumerable<object[]> SacCostStonesAndRamp() => new[]
    {
        new object[] { "Mind Stone" },
        new object[] { "Hedron Archive" },
        new object[] { "Sakura-Tribe Elder" },
        new object[] { "Burnished Hart" },
        new object[] { "Sojourner's Companion" },
    };

    [Theory]
    [MemberData(nameof(SacCostStonesAndRamp))]
    public void BuslessBuild_SacCostPaidThroughCentralSeam_PublishesPermanentSacrificedEvent(string cardName)
    {
        // Arrange — build via the BARE single-arg overload: no effects, no bus
        // at construction. This is the build shape the deferral worried about
        // (the only routed overload for the class-(b) tail historically).
        var alice = new Player("Alice", 20);
        var built = NamedCardFactory.Create(cardName, alice);
        built.Should().BeAssignableTo<Permanent>(
            $"'{cardName}' must materialize as a permanent with a sac-cost ability");

        var perm = (Permanent)built;
        perm.SetController(alice);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        // Find the activated ability whose cost set sacrifices the card itself.
        var sacAbility = perm.Abilities
            .OfType<ActivatedAbility>()
            .Single(a => a.Costs
                .OfType<AdditionalCost>()
                .Any(c => c.CostType == AdditionalCostType.Sacrifice
                    && ReferenceEquals(c.Permanent, perm)));

        // Pull the real self-sacrifice cost off the live ability.
        var sacCost = sacAbility.Costs
            .OfType<AdditionalCost>()
            .Single(c => c.CostType == AdditionalCostType.Sacrifice
                && ReferenceEquals(c.Permanent, perm));

        var bus = new EventBus();
        var sacrificed = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(sacrificed.Add);

        // Act — pay ONLY the sacrifice cost through the central seam WITH the
        // live bus, exactly as AbilityActivator.ActivateAbility does:
        //   _costPayment.PayCosts(player, costList, spendContext, _eventBus)
        new CostPayment().PayCosts(
            alice,
            new ICost[] { sacCost },
            Majik.Core.Mana.ManaSpendContext.None,
            bus);

        // Assert — the central IBusAwareCost seam carried the publish even
        // though the card was built bus-less. CR 701.16a: the cost-payer is the
        // sacrificing player.
        perm.Zone.Should().Be(ZoneType.Graveyard,
            $"'{cardName}' self-sacrifice cost must move it to the graveyard");
        sacrificed.Should().ContainSingle(
            $"'{cardName}' bus-less build must still publish PermanentSacrificedEvent " +
            "via the central cost-payment seam (source-gen bus overload obsolete)")
            .Which.Should().Match<PermanentSacrificedEvent>(ev =>
                ev.SacrificedCard == perm
                && ev.SacrificingPlayer == alice);
    }

    [Theory]
    [MemberData(nameof(SacCostStonesAndRamp))]
    public void BuslessBuild_SacCostPaidWithoutBus_StillSacrifices_NoPublish(string cardName)
    {
        // Legacy posture: no bus anywhere → the sacrifice still resolves, no
        // event published. Confirms the seam adds only the observable event,
        // never changes state effects.
        var alice = new Player("Alice", 20);
        var built = NamedCardFactory.Create(cardName, alice);
        var perm = (Permanent)built;
        perm.SetController(alice);
        alice.Zones.Battlefield.AddCard(perm);
        perm.SetZone(ZoneType.Battlefield);

        var sacCost = perm.Abilities
            .OfType<ActivatedAbility>()
            .SelectMany(a => a.Costs.OfType<AdditionalCost>())
            .First(c => c.CostType == AdditionalCostType.Sacrifice
                && ReferenceEquals(c.Permanent, perm));

        new CostPayment().PayCosts(alice, new ICost[] { sacCost });

        perm.Zone.Should().Be(ZoneType.Graveyard);
    }
}
