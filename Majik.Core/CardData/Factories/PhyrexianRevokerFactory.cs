using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phyrexian Revoker (New Phyrexia, {2}).
///
/// Artifact Creature — Phyrexian Horror 2/1. Oracle text:
///   "As Phyrexian Revoker enters, name a nonland card.
///    Activated abilities of sources with the chosen name can't be
///    activated unless they're mana abilities."
///
/// Phyrexian Revoker is the creature-bodied sibling of
/// <see cref="PithingNeedleFactory"/> — same printed static (CR 602.5c)
/// gated by the same name-restriction registry. Card-name selection
/// happens "as ~ enters" (CR 614.12), so the lifecycle reuses
/// <see cref="PithingNeedleStaticEffect"/> rather than re-implementing it.
///
/// ## Implemented (v1)
///
/// - 2/1 <see cref="Creature"/> — Phyrexian Horror, mana cost {2}. The
///   base Creature constructor only stamps <see cref="CardType.Creature"/>;
///   <see cref="CardType.Artifact"/> is additively flagged (mirrors
///   <see cref="OrnithopterFactory"/> / <see cref="KappaCannoneerFactory"/>'s
///   multi-type shape).
/// - <b>Printed static (CR 602.5c)</b>: name-targeted activated-ability
///   suppression via <see cref="PithingNeedleStaticEffect"/>: as the
///   Revoker enters, the supplied <c>nameSelector</c> resolves the
///   chosen nonland card name; that name is registered into
///   <see cref="Majik.Core.Rules.ActivatedAbilityRestrictions"/>, and
///   <see cref="Majik.Core.Rules.ActionValidator"/> rejects activated-
///   ability activations whose source has that name.
/// - <b>CR 605 mana-ability exemption</b>: inherited from
///   <see cref="PithingNeedleStaticEffect"/> — mana abilities take the
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> path which
///   bypasses <see cref="Majik.Core.Rules.ActionValidator"/>, so they
///   activate normally even on a named source.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Nonland" constraint on the chosen name</b>: the printed text
///   says "name a nonland card" (CR 614.12 / CR 205.3i). The lifecycle
///   accepts any string the selector returns; nonland-validation is the
///   caller's responsibility (mirrors the Pithing Needle "card name"
///   selector contract). When the agent prompt lands, the prompt itself
///   will enforce nonland filtering and this factory inherits the
///   constraint automatically.
/// - <b>"As ~ enters" choice timing</b>: same wrinkle as Pithing Needle —
///   the choice is technically made as part of the ETB replacement, not
///   after. The lifecycle treats the resolution point of the ETB as the
///   prompt moment, observationally equivalent in the engine's current
///   ETB pipeline.
/// </summary>
[CardName("Phyrexian Revoker")]
public static class PhyrexianRevokerFactory
{
    public const string CardName = "Phyrexian Revoker";
    public const string PrintedManaCost = "{2}";
    public const int Power = 2;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Phyrexian Revoker with no selector wired. Suitable for
    /// card-shape / dispatcher tests — the printed static will not
    /// register any name restriction.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, nameSelector: null, eventBus: null);

    /// <summary>
    /// Construct a Phyrexian Revoker whose printed static is fully wired
    /// against <paramref name="eventBus"/> and resolves the chosen name
    /// via <paramref name="nameSelector"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="nameSelector">Resolves the chosen nonland card name
    /// when the Revoker enters the battlefield. Called with the Revoker's
    /// controller. May be null — the suppression simply won't activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Creature Create(
        Player owner,
        Func<Player, string>? nameSelector,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Horror });

        // CR 301.1 / 302.1 — Phyrexian Revoker is an Artifact Creature.
        // The base Creature ctor only stamps CardType.Creature; additively
        // flag Artifact so HasType / colour-identity lookups see both
        // types (mirrors Ornithopter / Vault Skirge / Kappa Cannoneer).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        if (nameSelector != null)
        {
            // Reuse PithingNeedleStaticEffect — same CR 602.5c suppression
            // semantics, same name-restriction registry, same LTB cleanup
            // via CardMovedEvent. Phyrexian Revoker is functionally
            // identical to Pithing Needle for the printed static; the
            // only printed difference (Needle "card name" vs Revoker
            // "nonland card") is enforced at the selector prompt layer
            // — see class xmldoc.
            var lifecycle = new PithingNeedleStaticEffect(
                source: card,
                controller: owner,
                nameSelector: nameSelector,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return card;
    }
}
