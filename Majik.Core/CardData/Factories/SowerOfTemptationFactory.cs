using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sower of Temptation (Lorwyn,
/// Creature — Faerie Wizard {2}{U}{U}, 2/2).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, gain control of target creature for as long as
///    this creature remains on the battlefield."
///
/// ## Implemented (v1)
///
/// - 2/2 Creature — Faerie Wizard, mana cost {2}{U}{U}, owner / controller wired.
/// - <b>ETB triggered ability (CR 603.2)</b> — "When this creature enters, gain
///   control of target creature <i>for as long as this creature remains on the
///   battlefield</i>." Modeled declaratively as an <c>etb_self</c>
///   (<see cref="EnterBattlefieldSelfTriggerDef"/>) trigger carrying a single
///   <see cref="GainControlEffectDef"/> with the new persistent-steal duration
///   (<c>duration: "while_source_on_battlefield"</c>, <c>targetFilter:
///   "creature"</c>, <c>untap: false</c>, <c>gainsHaste: false</c>).
///
///   This is the canonical <b>"for as long as &lt;condition&gt;" (CR 611.2b)</b>
///   control-change card. Unlike the Threaten / Eldrazi Obligator until-end-of-turn
///   family, the steal does NOT revert at the cleanup step: at resolution the
///   verb registers a <see cref="TemporaryControlChangeEffect"/> carrying an
///   <c>until</c> predicate keyed on Sower's own zone
///   (<c>() =&gt; sower.Zone == ZoneType.Battlefield</c>). The effect therefore
///   reports <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> = <c>false</c> and
///   stays active until Sower leaves play; the live
///   <see cref="ContinuousEffectsService"/> prunes it the moment Sower's
///   departure rides a <see cref="Majik.Core.Events.CardMovedEvent"/> off the
///   battlefield, firing <c>OnExpired</c> to restore the stolen creature's prior
///   controller (CR 611.2b).
///
///   CR 608.2b — an illegal target at resolution (the creature has left the
///   battlefield since the ability went on the stack) fizzles cleanly. Without a
///   live continuous-effects service (pure-shape test path) the control swap
///   no-ops, mirroring the <see cref="ZealousConscriptsFactory"/> /
///   <see cref="EldraziObligatorFactory"/> posture.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 2/2 P/T / Faerie Wizard subtypes AND the ETB
/// gain-control ability are loaded from the embedded JSON definition
/// (<c>sower-of-temptation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The factory threads the live per-game
/// <see cref="ContinuousEffectsService"/> so the gain_control verb can register
/// its control-change on the ABILITY path.
///
/// ## Note on the printed Flying keyword
///
/// Sower of Temptation has Flying. The JSON schema does not model standalone
/// keyword lines on creatures, so the self-Flying keyword is not wired here (the
/// same residual recorded for Zealous Conscripts' / Eldrazi Obligator's printed
/// keywords); the gameplay-relevant behaviour (the ETB persistent steal) is
/// fully implemented.
/// </summary>
[CardName("Sower of Temptation")]
public static class SowerOfTemptationFactory
{
    public const string CardName = "Sower of Temptation";
    public const string Slug = "sower-of-temptation";

    /// <summary>
    /// Construct Sower of Temptation with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the ETB trigger is wired and
    /// declares its target, but the control swap no-ops at resolution without a
    /// service (the pure-shape posture, identical to Zealous Conscripts).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Sower of Temptation. When
    /// <paramref name="continuousEffects"/> is supplied, the ETB-trigger
    /// <c>gain_control</c> effect registers its CR 611.2b
    /// <see cref="TemporaryControlChangeEffect"/> (with the "for as long as this
    /// remains on the battlefield" <c>until</c> predicate) against that service
    /// at resolution. This is the overload the SourceGen-emitted effects-aware
    /// dispatcher routes to in production (GameFacade's instance-swap rebuild —
    /// CR 613.7c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Live per-game layers service the
    /// ETB-trigger control-change registers against. May be null — no live
    /// steal.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(
            definition, owner, replacements: null, continuous: continuousEffects);
    }
}
