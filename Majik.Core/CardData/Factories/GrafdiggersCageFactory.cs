using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Grafdigger's Cage (Dark Ascension, {1}).
///
/// Artifact. Oracle text:
///   "Creature cards in graveyards and libraries can't enter the
///    battlefield.
///    Players can't cast spells from graveyards or libraries."
///
/// ## Implemented (v1)
///
/// - Artifact with mana cost {1}, correct owner / controller.
/// - <b>Printed static (CR 614)</b>: creature cards in graveyards and
///   libraries can't ETB. Wired via
///   <see cref="GrafdiggersCageStaticEffect"/> — while Cage is on the
///   battlefield, an <see cref="IReplacementEffect{ZoneMoveIntent}"/>
///   is registered on the supplied <see cref="ReplacementBus"/> that
///   <b>cancels</b> any <see cref="ZoneMoveIntent"/> with
///   <c>ToZone = Battlefield</c>, <c>FromZone ∈ { Graveyard, Library }</c>,
///   and a card of type <see cref="CardType.Creature"/>. Cancellation
///   matches the printed "can't enter the battlefield" wording — the
///   card stays in its source zone (distinct from Containment Priest's
///   exile-rewrite).
/// - <b>Printed static (CR 601.3)</b>: players can't cast spells from
///   graveyards or libraries. Same lifecycle binder registers a global
///   cast-from-zone blocklist entry in
///   <see cref="Majik.Core.Rules.CastingRestrictions"/> for both
///   Graveyard and Library zones;
///   <see cref="Majik.Core.Rules.ActionValidator.ValidateCastSpell"/>
///   rejects any <see cref="Majik.Core.Rules.CastSpellAction"/> whose
///   <see cref="Majik.Core.Rules.CastSpellAction.FromZone"/> matches.
///   Symmetric across players (affects Cage's controller too).
///
/// ## Scope notes
///
/// - <b>Reanimation</b>: Reanimate, Animate Dead, Unburial Rites etc.
///   produce a ZoneMoveIntent for Graveyard → Battlefield on a creature
///   card; Cage cancels the move, leaving the creature in its
///   graveyard. The reanimation spell itself still resolves (its other
///   effects, if any, also resolve).
/// - <b>Token creatures</b>: tokens are created directly on the
///   battlefield (their ZoneMoveIntent source is rarely Graveyard or
///   Library), so they generally pass through unaffected by the
///   replacement.
/// - <b>Non-creature reanimation</b>: artifact / enchantment /
///   planeswalker reanimation targets pass through — the predicate is
///   creature-typed only.
/// - <b>Flashback / escape / aftermath / disturb / jump-start</b>: all
///   reject under the global cast-from-zone block (they cast from
///   graveyard). Bolas's Citadel-style library casts likewise reject.
/// - <b>Activated abilities from non-hand zones</b>: out of scope.
///   Dredge, channel, cycling, "from a graveyard" activated abilities
///   are all unaffected — Cage's printed text only constrains
///   <i>casting spells</i> and creature ETBs from those zones.
///
/// ## Deferred (v1 gaps)
///
/// - <b>FromZone stamping for every cast path</b>: same gap that the
///   Drannith Magistrate factory documents — the production
///   <see cref="Majik.Core.Services.SpellCaster"/> /
///   <see cref="Majik.Core.Game.SpellCastFlow"/> pipeline does not yet
///   stamp <see cref="Majik.Core.Rules.CastSpellAction.FromZone"/> for
///   every cast path. When the caller doesn't stamp,
///   <see cref="Majik.Core.Rules.ActionValidator"/> treats the cast as
///   unrestricted on the from-zone axis and Cage no-ops on that axis.
/// </summary>
[CardName("Grafdigger's Cage")]
public static class GrafdiggersCageFactory
{
    public const string CardName = "Grafdigger's Cage";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Grafdigger's Cage with no replacement-bus or event-bus
    /// wired. Suitable for card-shape / dispatcher tests — the printed
    /// statics will not register.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, replacementBus: null, eventBus: null);

    /// <summary>
    /// Construct Grafdigger's Cage with the printed-static lifecycle
    /// wired against <paramref name="replacementBus"/> and
    /// <paramref name="eventBus"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacementBus">The <see cref="ReplacementBus"/> to
    /// register the creature-ETB cancel replacement on. May be null —
    /// neither static will activate.</param>
    /// <param name="eventBus">Event bus for ETB/LTB tracking. May be
    /// null — the lifecycle will still sync once on Attach.</param>
    public static Artifact Create(
        Player owner,
        ReplacementBus? replacementBus,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var cage = new Artifact(CardName, PrintedManaCost);
        cage.SetOwner(owner);
        cage.SetController(owner);

        if (replacementBus != null)
        {
            var lifecycle = new GrafdiggersCageStaticEffect(
                source: cage,
                replacementBus: replacementBus,
                eventBus: eventBus);
            lifecycle.Attach();
        }

        return cage;
    }
}
