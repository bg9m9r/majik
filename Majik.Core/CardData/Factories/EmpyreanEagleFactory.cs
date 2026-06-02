using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Empyrean Eagle (Modern Horizons,
/// Creature — Bird Spirit {1}{W}{U}).
///
/// Oracle text:
///   "Flying.
///    Other creatures you control with flying get +1/+1."
///
/// ## Implemented (v1)
/// - 2/3 Creature — Bird Spirit, mana cost {1}{W}{U}, owner/controller wired.
/// - <b>Flying</b> on Empyrean Eagle itself (CR 702.9) — wired as a
///   <see cref="KeywordAbility"/> marker.
/// - <b>Keyword-gated anthem "Other creatures you control with flying get
///   +1/+1"</b> wired via <see cref="LordStaticEffect"/>'s
///   <c>matchingKeyword</c> variant: <c>matchingKeyword: "Flying"</c>,
///   <c>power: 1, toughness: 1</c>, <c>includeSelf: false</c>. CR 613.7c
///   (Layer 7c P/T). The affected set is the controller's OTHER creatures
///   whose EFFECTIVE keyword set contains Flying — read post-Layer-6
///   (<see cref="Creature.HasEffectiveKeyword"/>), so a creature GRANTED
///   flying still qualifies (CR 613.8 dependency: the Layer-7c anthem
///   depends on the Layer-6 keyword grants). The "Other" clause is honoured
///   by <c>includeSelf: false</c> — the Eagle is itself a flyer but doesn't
///   pump itself via its own static; a SECOND Empyrean Eagle does pump it
///   (each excludes only itself). Controller-scoped (default filter — not
///   <c>opponentsOnly</c>): an opponent's flyer is unaffected.
///
/// Multiple copies stack: two Empyrean Eagles give each OTHER flyer +2/+2,
/// and each Eagle pumps the other (both become 3/4).
///
/// ## Deferred (v1 gaps)
/// - <b>Control-change re-eval</b>: the controller scope reads
///   <see cref="Permanent.Controller"/> live, but the registered effect's
///   source is captured at register time — same caveat as the other lords.
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone changes;
///   <see cref="ContinuousEffect.IsActive"/> short-circuits when the Eagle
///   isn't on the battlefield so the bonus lifts correctly, but a future
///   Prune pass could drop the entry. Same shape as Goblin Chieftain.
/// - <b>Prod instance-swap wiring</b>: like every
///   <see cref="LordStaticEffect"/>-style creature anthem, the live bonus
///   requires the factory's effects-aware overload to be called with the
///   game's <see cref="ContinuousEffectsService"/>. GameFacade's non-land
///   instance-swap rebuild does not currently route through the
///   effects-aware overload (same residual as Leyline of the Guildpact /
///   Dryad of the Ilysian Grove); proven correct at the test layer here.
/// </summary>
[CardName("Empyrean Eagle")]
public static class EmpyreanEagleFactory
{
    public const string CardName = "Empyrean Eagle";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Empyrean Eagle with the printed Flying keyword wired but no
    /// live continuous-effects service. Suitable for shape / dispatcher
    /// tests — the keyword-gated anthem is not registered, so other flyers
    /// you control don't yet receive +1/+1.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Empyrean Eagle. When
    /// <paramref name="continuousEffects"/> is supplied, a keyword-gated
    /// <see cref="LordStaticEffect"/> granting +1/+1 to OTHER creatures the
    /// controller controls with effective Flying is registered against the
    /// layers service. The printed Flying keyword on the Eagle itself is
    /// always wired (consumed by
    /// <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/>).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// keyword-gated anthem against. May be null — no live bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying on Empyrean Eagle itself. KeywordAbility marker;
        // CombatAbilities.HasFlying reads it.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c — "Other creatures you control with flying get +1/+1."
            // Keyword-gated anthem: matchingKeyword "Flying" filters on the
            // candidate's EFFECTIVE (post-Layer-6) keyword set, so granted
            // flying counts (CR 613.8). includeSelf: false honours "Other".
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingKeyword: "Flying",
                power: 1,
                toughness: 1,
                includeSelf: false,
                opponentsOnly: false));
        }

        return card;
    }
}
