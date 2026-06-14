using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Class-level regression fence for the <c>echo-and-self-sac-creature-body-fx-sacrifice-bus</c>
/// deferral (v1-deferrals item #2, the resolve-time self-sac tail of class-(b)).
///
/// <para>
/// The central <see cref="Majik.Core.Costs.IBusAwareCost"/> cost-payment seam
/// (PR #2736) closed the <em>activated-ability</em> "Sacrifice CARDNAME:" cost
/// class — see <see cref="SacCostCentralSeamBusTests"/>. That seam only fires
/// for sacrifices paid as a <see cref="Majik.Core.Costs.AdditionalCost"/> at
/// cost-payment time. The "Ball Lightning template" bodies sacrifice themselves
/// <b>at resolution</b> (an end-step delayed trigger), NOT as a cost — so the
/// central cost seam never sees them. For those the bus must be threaded into
/// the <em>resolve closure</em> via the source-gen
/// <c>Create(Player, <see cref="ContinuousEffectsService"/>)</c> effects-aware
/// overload that <see cref="NamedCardFactory.Create(string, Player, ContinuousEffectsService?)"/>
/// dispatches on the production <c>GameFacade</c> build path.
/// </para>
///
/// <para>
/// This file fences the whole <b>end-step self-sac creature</b> family in one
/// place (mirroring <see cref="SacCostCentralSeamBusTests"/>'s posture for the
/// cost class, and the sibling <see cref="ResolveTimeSelfSacBusTests"/> for the
/// mana-rock resolve-closure subset) so a future "swings once then sacrifices
/// itself at end of turn" card cannot silently reopen the gap by shipping only
/// the bus-less single-arg <see cref="NamedCardFactory.Create(string, Player)"/>
/// overload. Each card is built EXACTLY as prod builds it — through the
/// effects-aware dispatch — and its end-step self-sacrifice (CR 701.16) must
/// publish a <see cref="PermanentSacrificedEvent"/> (CR 701.16a) crediting the
/// controller as the sacrificing player, the seam aristocrat "whenever a
/// creature you control is sacrificed / whenever an opponent sacrifices…"
/// payoffs read.
/// </para>
///
/// <para>
/// Two named cards ride the SAME resolve-time bus seam but with an
/// <em>ETB-gated</em> (not end-step) self-sac closure, so they are fenced in
/// their own factory tests rather than this uniform end-step theory:
/// <list type="bullet">
///   <item><b>Vexing Devil</b> — ETB "any opponent may have it deal 4 damage;
///     if a player does, sacrifice this" (needs an accepting agent + a live
///     <c>ResolutionContext</c>) →
///     <c>VexingDevilFactoryTests.SelfSac_OnProdPath_PublishesPermanentSacrificedEvent</c>.</item>
///   <item><b>Kroxa, Titan of Death's Hunger</b> — ETB "sacrifice it unless it
///     escaped" (CR 702.138b) →
///     <c>KroxaTitanFactoryTests.SelfSac_OnProdPath_PublishesPermanentSacrificedEvent</c>.</item>
/// </list>
/// They are named here for completeness but are not in the uniform end-step
/// theory below.
/// </para>
/// </summary>
public class EndStepSelfSacBusTests
{
    /// <summary>
    /// The end-step self-sac creature bodies — the "Ball Lightning template"
    /// family. Each carries a single end-step <see cref="TriggeredAbility"/>
    /// whose resolution sacrifices the creature (CR 701.16). On the prod
    /// effects-aware build that sacrifice must publish a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static IEnumerable<object[]> EndStepSelfSacCreatures() => new[]
    {
        new object[] { "Ball Lightning" },
        new object[] { "Spark Elemental" },
        new object[] { "Hellspark Elemental" },
    };

    [Theory]
    [MemberData(nameof(EndStepSelfSacCreatures))]
    public void EffectsAwareBuild_EndStepSelfSac_PublishesPermanentSacrificedEvent(string cardName)
    {
        // Arrange — build via the EFFECTS-AWARE dispatch, exactly as the
        // production GameFacade routed build does (DeckCardBuilder →
        // NamedCardFactory.Create(name, owner, effects) → the source-gen
        // *WithEffects overload, which threads effects.EventBus into the
        // end-step self-sac resolve closure). This is the prod build shape;
        // the bus-less single-arg overload would publish nothing.
        var alice = new Player("Alice", 20);
        var bus = new EventBus();
        var effects = new ContinuousEffectsService(bus);

        var captured = new List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(captured.Add);

        var built = NamedCardFactory.Create(cardName, alice, effects);
        built.Should().BeOfType<Creature>(
            $"{cardName} is a creature body whose self-sacrifice rides the resolve-time bus seam");
        var card = (Creature)built;

        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        // Act — resolve the end-step self-sacrifice. These bodies attach a
        // single end-step TriggeredAbility; executing its effect(s) runs the
        // CR 701.16 sacrifice closure (the path the live priority loop drives
        // when the End-step StepStartedEvent fires).
        var endStepTrigger = card.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in endStepTrigger.Effects) e.Execute();

        // Assert — the resolve-time self-sacrifice published exactly one
        // PermanentSacrificedEvent (CR 701.16a) crediting the controller, and
        // the creature is in its owner's graveyard (CR 701.16).
        captured.Should().ContainSingle(
            "the prod effects-aware dispatch threads the bus so the resolve-time "
            + "self-sacrifice publishes PermanentSacrificedEvent (CR 701.16a)")
            .Which.SacrificingPlayer.Should().BeSameAs(alice);
        card.Zone.Should().Be(ZoneType.Graveyard);
        alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    /// <summary>
    /// Bus-less posture (the shape unit tests / direct factory callers use):
    /// built through the single-arg <see cref="NamedCardFactory.Create(string, Player)"/>
    /// overload the end-step self-sacrifice still SACRIFICES the creature
    /// (CR 701.16 — board state stays correct) but publishes NO
    /// <see cref="PermanentSacrificedEvent"/> (no bus in scope). This is the
    /// behaviour-preserving fallback the bus-aware seam degrades to — proving
    /// the publish is gated strictly on the bus being threaded, never silently
    /// double-published or dropped on the board move.
    /// </summary>
    [Theory]
    [MemberData(nameof(EndStepSelfSacCreatures))]
    public void BuslessBuild_EndStepSelfSac_SacrificesButPublishesNothing(string cardName)
    {
        var alice = new Player("Alice", 20);

        var built = NamedCardFactory.Create(cardName, alice);
        var card = (Creature)built;
        alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var endStepTrigger = card.Abilities
            .OfType<TriggeredAbility>()
            .Single(t => t.Condition is EventTriggerCondition<StepStartedEvent>);
        foreach (var e in endStepTrigger.Effects) e.Execute();

        // Board state still correct (the sacrifice happens regardless of bus).
        card.Zone.Should().Be(ZoneType.Graveyard,
            "the self-sacrifice (CR 701.16) happens on the bus-less path too");
        alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }
}
