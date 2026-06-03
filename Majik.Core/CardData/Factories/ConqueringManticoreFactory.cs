using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conquering Manticore (Born of the Gods,
/// Creature — Manticore {4}{R}{R}, 5/5).
///
/// Oracle text (verified against Scryfall):
///   "Flying
///    When this creature enters, gain control of target creature an opponent
///    controls until end of turn. Untap that creature. It gains haste until end
///    of turn."
///
/// ## Implemented (v1)
///
/// - 5/5 Creature — Manticore, mana cost {4}{R}{R}, owner / controller wired,
///   red (from the {R} pips per CR 202.2c), with the printed <b>Flying</b>
///   keyword (CR 702.9) declared in the embedded JSON's <c>"keywords"</c> array.
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this creature enters, gain
///   control of target creature an opponent controls until end of turn. Untap
///   that creature. It gains haste until end of turn." Battlefield-active
///   <c>etb_self</c> trigger with a single declarative <c>gain_control</c> effect
///   (<see cref="GainControlEffectDef"/>, <c>targetFilter:
///   "creature_you_dont_control"</c> — CR 109.5 "a creature an opponent
///   controls"; <c>duration: "end_of_turn"</c>, <c>untap: true</c>,
///   <c>gainsHaste: true</c>). At resolution it registers a CR 613.2 / CR 514.2
///   <see cref="TemporaryControlChangeEffect"/> on the live per-game
///   <see cref="ContinuousEffectsService"/> (control reverts to the prior
///   controller at the cleanup step), untaps the stolen creature (CR 701.21),
///   and grants it haste until end of turn (CR 302.6) so it can attack the turn
///   it changes control. CR 608.2b — an illegal target at resolution (the
///   creature left the battlefield) fizzles cleanly.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 5/5 P/T / Manticore subtype / Flying keyword AND
/// the ETB gain_control triggered ability are loaded from the embedded JSON
/// definition (<c>conquering-manticore.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The factory only threads the live
/// per-game <see cref="ContinuousEffectsService"/> into the build so the
/// gain_control verb can register its continuous control-change effect on the
/// ABILITY path (mirroring Zealous Conscripts / Eldrazi Obligator, the rest of
/// the now-generic ETB/cast-trigger Threaten family).
/// </summary>
[CardName("Conquering Manticore")]
public static class ConqueringManticoreFactory
{
    public const string CardName = "Conquering Manticore";
    public const string Slug = "conquering-manticore";

    /// <summary>
    /// Construct Conquering Manticore with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the ETB trigger is wired and
    /// declares its target, but the control swap no-ops at resolution without a
    /// service (the pure-shape posture, identical to Zealous Conscripts).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Conquering Manticore. When
    /// <paramref name="continuousEffects"/> is supplied, the ETB
    /// <c>gain_control</c> effect registers its CR 613
    /// <see cref="TemporaryControlChangeEffect"/> (+ untap + haste rider) against
    /// that service at resolution, so a stolen creature actually changes control
    /// until end of turn. This is the overload the SourceGen-emitted
    /// effects-aware dispatcher routes to in production (GameFacade's
    /// instance-swap rebuild — CR 613.7c).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Live per-game layers service the ETB
    /// control-change registers against. May be null — no live steal.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Creature)CardDefinitionFactory.Build(
            definition, owner, replacements: null, continuous: continuousEffects);
    }
}
