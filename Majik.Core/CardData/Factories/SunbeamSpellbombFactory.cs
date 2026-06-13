using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sunbeam Spellbomb (Mirrodin / reprints).
///
/// Artifact — {1}. Oracle text:
///   "{W}, Sacrifice this artifact: You gain 5 life.
///    {1}, Sacrifice this artifact: Draw a card."
///
/// Mirrors <see cref="AetherSpellbombFactory"/> — the same two-activated-ability
/// sac-spellbomb shape (a colored effect plus a {1} cantrip) — but neither mode
/// targets: the {W} mode just gains the controller 5 life.
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{W}, Sacrifice: you gain 5 life</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{W}")
///   plus <see cref="AdditionalCost"/>.Sacrifice on the spellbomb itself. The
///   resolution effect gains the controller 5 life (CR 119.3) and sacrifices
///   the spellbomb.
/// - <b>{1}, Sacrifice: draw a card</b> — second
///   <see cref="ActivatedAbility"/> on the same card. <see cref="ManaCostCost"/>("{1}")
///   plus self-sacrifice; resolution moves the spellbomb to its owner's
///   graveyard and draws one card for the controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op stub.
///   The effect closure performs the zone move so behavior is observable
///   (mirrors <see cref="AetherSpellbombFactory"/> / Mishra's Bauble). Remove
///   the explicit move-to-graveyard once <see cref="AdditionalCost.Pay"/>
///   performs the sacrifice itself.
/// </summary>
[CardName("Sunbeam Spellbomb")]
public static class SunbeamSpellbombFactory
{
    public const string CardName = "Sunbeam Spellbomb";

    /// <summary>
    /// Amount of life gained by the {W} mode. Oracle: "You gain 5 life."
    /// </summary>
    private const int LifeGainAmount = 5;

    /// <summary>
    /// Construct Sunbeam Spellbomb owned and controlled by
    /// <paramref name="owner"/>. Shape-only — no event bus, so the
    /// self-sacrifice cost publishes nothing (legacy posture).
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to (the source generator recognises
    /// <c>Create(Player, ContinuousEffectsService)</c> as the effects-aware
    /// overload — see <see cref="FestivalCrasherFactory"/>). Threads
    /// <c>effects.EventBus</c> into the self-sacrifice cost so paying it
    /// publishes a <see cref="PermanentSacrificedEvent"/> (CR 701.16a)
    /// crediting the cost-payer.
    /// </summary>
    public static Artifact Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Canonical builder. <paramref name="eventBus"/> (when non-null) is
    /// threaded into the self-sacrifice <see cref="AdditionalCost"/> so the
    /// cost-payment path publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a). Null preserves the legacy publish-nothing posture.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = new Artifact(CardName, "{1}");
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {W}, Sacrifice this artifact: You gain 5 life.
        // CR 602 — activated ability; no targets. CR 119.3 — life gain
        // affects the ability's controller.
        // ----------------------------------------------------------------
        var lifegainEffect = new Effect(
            "Sunbeam Spellbomb: gain 5 life + sac self",
            () =>
            {
                owner.GainLife(LifeGainAmount);
                SacrificeSelf(spellbomb, owner);
            });

        var lifegainAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{W}"),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { lifegainEffect });

        spellbomb.AddAbility(lifegainAbility);

        // ----------------------------------------------------------------
        // {1}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Sunbeam Spellbomb: draw a card + sac self",
            () =>
            {
                SacrificeSelf(spellbomb, owner);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty-library loss handled by SBAs
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(spellbomb, eventBus),
            },
            effects: new IEffect[] { drawEffect });

        spellbomb.AddAbility(drawAbility);

        return spellbomb;
    }

    /// <summary>
    /// Move the spellbomb from the battlefield to its owner's graveyard.
    /// Defensive against double-execution (idempotent if already sacrificed).
    /// Mirrors the Aether Spellbomb sacrifice closure — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a stub.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
