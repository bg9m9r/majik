using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Obligator (Oath of the Gatewatch,
/// Creature — Eldrazi {2}{R}, 3/1).
///
/// Oracle text (verified against Scryfall):
///   "Devoid (This card has no color.)
///    When you cast this spell, you may pay {1}{C}. If you do, gain control of
///    target creature until end of turn, untap that creature, and it gains haste
///    until end of turn. ({C} represents colorless mana.)
///    Haste"
///
/// ## Implemented (v1)
///
/// - 3/1 Creature — Eldrazi, mana cost {2}{R}, owner / controller wired.
///   <b>Devoid</b> (CR 702.114) is stamped via <see cref="Card.SetDevoid"/> so
///   the card is colorless despite its {R} pip.
/// - <b>Cast triggered ability (CR 603.2.1)</b> — "When you cast this spell, you
///   may pay {1}{C}. If you do, gain control of target creature until end of
///   turn, untap that creature, and it gains haste until end of turn." Modeled
///   declaratively as a <c>cast_self</c>
///   (<see cref="CastSelfTriggerDef"/>) trigger — active in the Stack zone (the
///   spell is on the stack when it triggers; the trigger goes on the stack above
///   the spell, CR 603.3b) — carrying a single <see cref="GainControlEffectDef"/>
///   with the OPTIONAL reflexive payment rider
///   (<see cref="GainControlEffectDef.OptionalManaCost"/> = <c>{1}{C}</c>,
///   <c>targetFilter: "creature"</c>, <c>duration: "end_of_turn"</c>,
///   <c>untap: true</c>, <c>gainsHaste: true</c>).
///
///   At resolution the verb (CR 601.2b / 603.4) prompts the controller's agent
///   yes/no; on "yes" it pays {1}{C} via <see cref="Player.PayMana"/> and, only
///   if the payment succeeds, registers a CR 613.2 / CR 514.2
///   <see cref="TemporaryControlChangeEffect"/> on the live per-game
///   <see cref="ContinuousEffectsService"/> (control reverts at cleanup), untaps
///   the stolen creature (CR 701.21), and grants it haste until end of turn
///   (CR 302.6). Declining, or an unpayable {1}{C}, skips the entire "if you do"
///   clause. The target is chosen as the trigger goes on the stack (CR 603.3d),
///   independent of the later payment.
///
///   {C} (CR 107.4c) folds into a generic pip in v1's pool model
///   (<see cref="Majik.Core.ValueObjects.ManaCost.Parse"/>), so {1}{C} is
///   charged as two generic mana — the dedicated-colorless spend restriction is
///   the same v1 simplification snow ({S}) and other {C}-cost cards carry.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 3/1 P/T / Eldrazi subtype AND the cast-trigger
/// optional-steal ability are loaded from the embedded JSON definition
/// (<c>eldrazi-obligator.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The factory threads the live per-game
/// <see cref="ContinuousEffectsService"/> (so the gain_control verb can register
/// its control-change on the ABILITY path) and stamps Devoid.
///
/// ## Note on the printed Haste keyword
///
/// Eldrazi Obligator itself has Haste. The JSON schema does not model standalone
/// keyword lines on creatures, so the self-Haste keyword is not wired here (the
/// same residual recorded for Zealous Conscripts); the gameplay-relevant
/// behaviour (the optional cast-trigger steal + the stolen creature's haste
/// rider) is fully implemented.
/// </summary>
[CardName("Eldrazi Obligator")]
public static class EldraziObligatorFactory
{
    public const string CardName = "Eldrazi Obligator";
    public const string Slug = "eldrazi-obligator";

    /// <summary>
    /// Construct Eldrazi Obligator with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the cast trigger is wired and
    /// declares its target, but the control swap no-ops at resolution without a
    /// service (the pure-shape posture, identical to Zealous Conscripts).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Eldrazi Obligator. When
    /// <paramref name="continuousEffects"/> is supplied, the cast-trigger
    /// optional <c>gain_control</c> effect registers its CR 613
    /// <see cref="TemporaryControlChangeEffect"/> (+ untap + haste rider)
    /// against that service at resolution once the optional {1}{C} is paid. This
    /// is the overload the SourceGen-emitted effects-aware dispatcher routes to
    /// in production (GameFacade's instance-swap rebuild — CR 613.7c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Live per-game layers service the
    /// cast-trigger control-change registers against. May be null — no live
    /// steal.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(
            definition, owner, replacements: null, continuous: continuousEffects);
        // CR 702.114 — Devoid: the card is colorless despite its {R} pip.
        card.SetDevoid(true);
        return card;
    }
}
