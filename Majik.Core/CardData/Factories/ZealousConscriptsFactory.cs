using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Zealous Conscripts (Avacyn Restored,
/// Creature — Human Warrior {4}{R}, 3/3).
///
/// Oracle text (verified against Scryfall):
///   "Haste
///    When this creature enters, gain control of target permanent until end of
///    turn. Untap that permanent. It gains haste until end of turn."
///
/// ## Implemented (v1)
///
/// - 3/3 Creature — Human Warrior, mana cost {4}{R}, owner / controller wired,
///   red (from the {R} pip per CR 202.2c).
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this creature enters,
///   gain control of target PERMANENT until end of turn. Untap that permanent.
///   It gains haste until end of turn." Battlefield-active <c>etb_self</c>
///   trigger with a single declarative <c>gain_control</c> effect
///   (<see cref="GainControlEffectDef"/>, <c>targetFilter: "permanent"</c>,
///   <c>duration: "end_of_turn"</c>, <c>untap: true</c>, <c>gainsHaste: true</c>).
///   At resolution it registers a CR 613.2 / CR 514.2
///   <see cref="TemporaryControlChangeEffect"/> on the live per-game
///   <see cref="ContinuousEffectsService"/> (control reverts to the prior
///   controller at the cleanup step), untaps the stolen permanent (CR 701.21),
///   and — for a stolen creature — grants it haste until end of turn (CR 302.6)
///   so it can attack the turn it changes control. Targets ANY permanent (not
///   just a creature): an artifact / enchantment / land / planeswalker is a
///   legal target; the control swap + untap apply, the haste rider no-ops on a
///   non-creature. CR 608.2b — an illegal target at resolution (the permanent
///   left the battlefield) fizzles cleanly.
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 3/3 P/T / Human Warrior subtypes AND the ETB
/// gain_control triggered ability are loaded from the embedded JSON definition
/// (<c>zealous-conscripts.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The factory only threads the live
/// per-game <see cref="ContinuousEffectsService"/> into the build so the
/// gain_control verb can register its continuous control-change effect on the
/// ABILITY path (mirroring how the spell path threads the same service).
///
/// ## Note on the printed Haste keyword
///
/// Zealous Conscripts itself has Haste (it can attack the turn it enters). The
/// JSON schema does not model standalone keyword lines on creatures, so the
/// self-Haste keyword is not wired here; the gameplay-relevant behaviour (the
/// ETB steal + the stolen permanent's haste rider) is fully implemented. The
/// self-Haste residual is recorded in the v1 deferrals backlog.
/// </summary>
[CardName("Zealous Conscripts")]
public static class ZealousConscriptsFactory
{
    public const string CardName = "Zealous Conscripts";
    public const string Slug = "zealous-conscripts";

    /// <summary>
    /// Construct Zealous Conscripts with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the ETB trigger is wired and
    /// declares its target, but the control swap no-ops at resolution without
    /// a service (the pure-shape posture, identical to the spell path).
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Zealous Conscripts. When
    /// <paramref name="continuousEffects"/> is supplied, the ETB
    /// <c>gain_control</c> effect registers its CR 613
    /// <see cref="TemporaryControlChangeEffect"/> (+ untap + haste rider)
    /// against that service at resolution, so a stolen permanent actually
    /// changes control until end of turn. This is the overload the
    /// SourceGen-emitted effects-aware dispatcher routes to in production
    /// (GameFacade's instance-swap rebuild — CR 613.7c).
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
