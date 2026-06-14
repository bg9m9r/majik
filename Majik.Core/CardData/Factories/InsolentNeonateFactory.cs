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
/// Named-card factory for Insolent Neonate (Shadows over Innistrad, {R}).
///
/// Creature — Vampire 1/1. Oracle text:
///   "Menace (This creature can't be blocked except by two or more
///    creatures.)
///    Discard a card, Sacrifice this creature: Draw a card."
///
/// ## Implemented (v1)
///
/// - 1/1 Vampire with mana cost {R}, owner / controller stamped.
/// - <see cref="KeywordAbility"/> marker for Menace (CR 702.110), consumed
///   by <see cref="Majik.Core.Combat.CombatAbilities.HasMenace"/> (same
///   posture as Grief / Hive of the Eye Tyrant / Lord of Atlantis).
/// - <b>"Discard a card, Sacrifice this creature: Draw a card"</b> —
///   <see cref="ActivatedAbility"/> (CR 602.1) with two costs:
///   <list type="number">
///     <item><see cref="DiscardACardCost"/> — first cost, picks the first
///       card in the controller's hand (deterministic v1 picker, same
///       policy as <see cref="PsychicFrogFactory"/>'s pump activation /
///       <see cref="FaithlessSalvagingFactory"/>'s resolve-time discard).</item>
///     <item><see cref="AdditionalCost.Sacrifice"/> on the Neonate itself —
///       the cost surface registers the intent; the actual battlefield →
///       graveyard zone move is performed inside the effect closure
///       (mirrors <see cref="CausticCaterpillarFactory"/> / Aether
///       Spellbomb / Mind Stone — the generic <see cref="AdditionalCost.Pay"/>
///       sacrifice path is a no-op stub).</item>
///   </list>
///   Effect: draw one card from the top of the controller's library
///   (CR 121.1 — single top-of-library draw, empty library flags the SBA
///   loss via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>).
///
/// ## Order of operations
///
/// CR 117.1c — all costs for an activated ability are paid simultaneously
/// from the player's perspective. The implementation pays discard first,
/// then sacrifice, then resolves the draw effect, but the cost surface
/// makes both atomic (legality is checked before any payment).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard pick prompt</b>: <see cref="DiscardACardCost.Target"/>
///   may be set by an agent before activation, otherwise the deterministic
///   first-in-hand picker fires. A real agent-driven "choose a card to
///   discard" prompt waits on the shared discard-prompt surface
///   (same gap as Faithless Looting / Psychic Frog / Liliana of the Veil).
/// - <b>Activation-zone gate</b>: <see cref="ActivatedAbility"/> doesn't
///   gate on <see cref="ZoneType.Battlefield"/> yet; the effect closure
///   guards on the Neonate's current zone before sacrificing so a stale
///   activation re-entry can't double-sacrifice.
/// </summary>
[CardName("Insolent Neonate")]
public static class InsolentNeonateFactory
{
    public const string CardName = "Insolent Neonate";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Insolent Neonate owned and controlled by
    /// <paramref name="owner"/>. Menace keyword marker + the discard-sac-
    /// draw activated ability are attached to the card. No bus ⇒ the
    /// self-sacrifice cost publishes nothing (legacy shape-only posture).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the <b>production</b> <c>GameFacade</c> routed
    /// build dispatches to. Forwards <c>effects.EventBus</c> so paying the
    /// self-sacrifice cost publishes a <see cref="PermanentSacrificedEvent"/>
    /// (CR 701.16a) for aristocrat payoffs. Mirrors the Festival-Crasher /
    /// Spellbomb seam.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Construct Insolent Neonate. When <paramref name="eventBus"/> is supplied
    /// the self-sacrifice activation cost publishes a
    /// <see cref="PermanentSacrificedEvent"/> (CR 701.16a).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.110 — Menace keyword marker. Consumed by
        // CombatAbilities.HasMenace at block-declaration time.
        card.AddAbility(new KeywordAbility("Menace", card, owner));

        // ----------------------------------------------------------------
        // Discard a card, Sacrifice this creature: Draw a card.
        // CR 602.1 — activated ability. Two costs (discard + sacrifice-
        // self), single effect (draw one). The sacrifice payment is
        // performed inside the effect closure because the generic
        // AdditionalCost.Sacrifice payment is a no-op stub (mirrors
        // Caustic Caterpillar / Aether Spellbomb / Mind Stone).
        //
        // RE-SOURCE-SAFE (agatha-bespoke-factory-resolutioncontext-source-
        // migration): the effect sacrifices the live ResolutionContext.Source
        // permanent (the ability's own source at resolution) and draws for
        // ResolutionContext.Controller (the activator), falling back to `card`
        // / `owner` only on the context-less legacy sync path
        // (ResolutionContext.Legacy, where Source / Controller are null). The
        // sacrifice cost (AdditionalCost.Sacrifice(card, …)) re-homes via
        // AdditionalCost.RebindSource (Stage 1), so when Agatha's Soul
        // Cauldron's group-grant re-homes the REAL ability — including its
        // bespoke DiscardACardCost, which the oracle-rebuild fallback cannot
        // reconstruct — onto a counter-bearing bearer via
        // ActivatedAbility.RebindTo (CR 707.2 / 613.1f), the BEARER is
        // sacrificed and the BEARER's controller draws, never the exiled
        // Insolent Neonate. Marked RebindSafe.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: sacrifice self + draw a card",
            ctx =>
            {
                var subject = (ctx.Source as Permanent) ?? card;
                var drawer = ctx.Controller ?? subject.Controller ?? owner;

                // Sacrifice payment — battlefield → owner's graveyard.
                // CR 701.16a — route through the bus-aware Fx.Sacrifice overload
                // when a bus is wired so PermanentSacrificedEvent fires; bus-less
                // = move only. Idempotent guard against stale activations.
                if (subject.Zone == ZoneType.Battlefield)
                {
                    var sacrificer = subject.Controller ?? drawer;
                    if (eventBus != null) Primitives.Fx.Sacrifice(subject, sacrificer, eventBus);
                    else Primitives.Fx.Sacrifice(subject);
                }

                // CR 121.1 — draw one card from the top of the drawer's
                // library. Empty library flags the CR 704.5b SBA loss via
                // MarkTriedToDrawFromEmptyLibrary (same handling as
                // Faithless Looting / Faithless Salvaging / Psychic Frog).
                var top = drawer.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    drawer.MarkTriedToDrawFromEmptyLibrary();
                    return ValueTask.CompletedTask;
                }
                drawer.Zones.Library.RemoveCard(top);
                drawer.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
                return ValueTask.CompletedTask;
            });

        var drawAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new DiscardACardCost(),
                AdditionalCost.Sacrifice(card, eventBus),
            },
            effects: new IEffect[] { drawEffect },
            rebindSafe: true);

        card.AddAbility(drawAbility);

        return card;
    }
}
