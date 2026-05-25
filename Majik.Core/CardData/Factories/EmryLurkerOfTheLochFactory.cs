using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emry, Lurker of the Loch (Throne of Eldraine,
/// {4}{U} reduced by Affinity for artifacts).
///
/// Legendary Creature — Merfolk Wizard 1/2. Oracle text:
///   "Affinity for artifacts (This spell costs {1} less to cast for each
///    artifact you control.)
///    When Emry enters, mill four cards.
///    {T}: Choose target artifact card in your graveyard. You may cast
///    that card this turn. (You still pay its costs. Timing rules still
///    apply.)"
///
/// ## Implemented (v1)
///
/// - 1/2 Legendary Merfolk Wizard with printed mana cost {4}{U}.
/// - <b>Affinity for artifacts (CR 702.40)</b>: wired via
///   <see cref="CostReductionAbility.AffinityFor"/>(<see cref="CardType.Artifact"/>).
///   The cost-reducer scans the caster's battlefield at cast time
///   (<see cref="CostReduction.GetEffectiveCost"/>) and lowers Emry's
///   generic-mana requirement by 1 per controller-controlled artifact.
///   Coloured pip ({U}) untouched (CR 117.7c); floor-at-zero applies.
///   Mirrors the canonical Affinity binder; same shape as Frogmite /
///   Cranial Plating / Myr Enforcer.
/// - <b>ETB triggered ability — "When Emry enters, mill four cards."
///   (CR 603.1 + CR 701.13)</b>: wired via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. On resolution the
///   effect calls <see cref="MillAction.Apply"/> with count = 4. The
///   ETB count is exposed as <see cref="MillOnEnterCount"/> so tests and
///   callers don't repeat the magic number.
/// - <b>Activated {T} — "Choose target artifact card in your graveyard.
///   You may cast that card this turn." (CR 118.9)</b>: wired via
///   <see cref="ActivatedAbility"/> with a single
///   <see cref="AdditionalCost.Tap"/> cost. The resolution effect picks
///   the first artifact card in the controller's graveyard (deterministic
///   v1 — same posture as Stoneforge Mystic's "first Equipment in hand"
///   picker), then stamps a runtime grave-cast grant on that card via
///   <see cref="Card.GrantRuntimeGraveyardCast"/> with the card's own
///   printed mana cost. Mirrors Yawgmoth's Will / Lurrus of the Dream-
///   Den's grant shape — the existing
///   <see cref="GraveyardCastAlternativeCost"/> machinery handles the
///   zone-restriction lift, alternative-cost-replaces-printed-cost mana
///   payment, and default post-resolution destination (battlefield for
///   permanent cards) for free.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Target prompt for the activated ability</b>: oracle says "choose
///   target artifact card in your graveyard" — v1 picks the first
///   artifact card in the controller's graveyard. Full prompting
///   requires threading <see cref="Players.Agents.TargetRequest"/>
///   through <see cref="ActivatedAbility.TargetRequests"/>; same gap as
///   Stoneforge Mystic's "attach to a creature you control".
/// - <b>Until-end-of-turn grant clearing</b>: the
///   <see cref="Card.RuntimeGraveyardCastCost"/> stamp is not cleared at
///   end of turn. Same posture as Yawgmoth's Will — the stamp is benign
///   past EOT for the same reasons. A bus-aware overload could subscribe
///   to <c>TurnEndedEvent</c> and clear the stamp; deferred.
/// - <b>"Timing rules still apply" gate</b>: the reminder text says
///   sorceries from the graveyard are still sorcery-speed. The shared
///   <see cref="GraveyardCastAlternativeCost"/> defers timing checks to
///   the engine's normal cast-speed machinery; nothing extra here.
/// - <b>Legend rule</b>: Emry is Legendary; the engine's
///   <see cref="Rules.StateBasedActions"/> already enforces CR 704.5j
///   (legend rule) when two same-named legendaries share a controller.
///   No factory-side wiring needed.
/// </summary>
[CardName("Emry, Lurker of the Loch")]
public static class EmryLurkerOfTheLochFactory
{
    public const string CardName = "Emry, Lurker of the Loch";
    public const string PrintedManaCost = "{4}{U}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// How many cards Emry mills on entry (CR 701.13). Exposed as a
    /// constant so tests / callers don't hard-code the magic number.
    /// </summary>
    public const int MillOnEnterCount = 4;

    /// <summary>
    /// Construct Emry with no live trigger-manager wiring. The ETB
    /// mill-4 trigger is attached to the card shape but not registered;
    /// tests exercise the ETB effect by either firing the trigger
    /// manually or driving the card through ZoneService (which publishes
    /// the <see cref="Events.CardMovedEvent"/> that the trigger
    /// consumes).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Emry with an optional <see cref="TriggerManager"/>.
    /// When supplied, the ETB mill-4 trigger is registered so a
    /// <see cref="Events.CardMovedEvent"/> to the battlefield places the
    /// ability on the stack automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Affinity for artifacts (CR 702.40 / CR 117.7).
        // CostReduction.GetEffectiveCost scans the caster's battlefield
        // at cast time and reduces the generic-mana requirement by 1 per
        // matching artifact the caster controls. Coloured pip ({U})
        // untouched; floor-at-zero applies (CR 117.7c).
        // ----------------------------------------------------------------
        card.AddAbility(CostReductionAbility.AffinityFor(CardType.Artifact));

        // ----------------------------------------------------------------
        // ETB triggered ability — "When Emry enters, mill four cards."
        // (CR 603.1 + CR 701.13). Deterministic — the mill is owner-side
        // only (oracle text is "mill four cards" with no target, so the
        // controller is the implicit mill target per CR 109.5 / CR 701.13).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: mill {MillOnEnterCount} cards",
            () => MillAction.Apply(owner, MillOnEnterCount));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated {T} — "Choose target artifact card in your graveyard.
        // You may cast that card this turn." (CR 118.9).
        //
        // On resolution: pick the first artifact card in the controller's
        // graveyard (deterministic v1 — see class xmldoc), then stamp a
        // runtime grave-cast grant on it via Card.GrantRuntimeGraveyardCast
        // with the card's own printed mana cost. Mirrors Yawgmoth's Will
        // / Lurrus of the Dream-Den's grant shape — callers cast the
        // stamped card by composing a GraveyardCastAlternativeCost from
        // the stamped cost.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: grant grave-cast on first artifact in graveyard",
            () =>
            {
                var pick = FindArtifactCardInGraveyard(owner);
                if (pick == null) return;
                pick.GrantRuntimeGraveyardCast(pick.ManaCostValue);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }

    /// <summary>
    /// Find a legal Emry-activation target — an artifact card in the
    /// controller's graveyard. v1 deterministic — returns the first
    /// match. Future agent-prompt path will replace with a
    /// <see cref="Players.Agents.TargetRequest"/>. Returns null when the
    /// graveyard has no artifact cards (activation resolves as a no-op
    /// per CR 117 — chosen target is null, the may-effect simply does
    /// nothing).
    /// </summary>
    private static Card? FindArtifactCardInGraveyard(Player owner) =>
        owner.Zones.Graveyard.GetCards()
            .OfType<Card>()
            .FirstOrDefault(c => c.HasType(CardType.Artifact));
}
