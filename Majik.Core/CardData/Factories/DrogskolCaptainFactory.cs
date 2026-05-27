using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Drogskol Captain (Innistrad, {1}{W}{U}).
///
/// Creature — Spirit Soldier 2/2. Oracle text:
///   "Flying."
///   "Other Spirit creatures you control get +1/+1 and have hexproof."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Spirit Soldier at {1}{W}{U}, owner / controller wired.
/// - <b>Flying</b> on Drogskol Captain itself (CR 702.9) — wired as a
///   <see cref="KeywordAbility"/> marker consumed by the combat-validator
///   block restrictions. Drogskol Captain is itself a Spirit, but the
///   "Other" rider on the static effect means Captain doesn't benefit
///   from its own +1/+1 + hexproof buff — printed Flying still applies.
/// - <b>Static "Other Spirit creatures you control get +1/+1 and have
///   hexproof"</b> wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Spirit</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Hexproof"]</c>, <c>includeSelf: false</c>.
///   Layer 7c for P/T (CR 613.7c) + Layer 6 for the granted keyword
///   (CR 613.1f). The "you control" scope is honoured by the default
///   controller filter (no <c>allPlayers</c>, no <c>opponentsOnly</c>);
///   <c>includeSelf: false</c> honours the printed "Other" rider so
///   Captain itself does NOT get the buff or hexproof from its own
///   static (its own Flying comes from the printed keyword above).
///
/// ## Hexproof grant (CR 702.11)
/// The "Hexproof" string is added to each affected Spirit's keyword set
/// via <see cref="CreatureCharacteristics.Keywords"/>. The targeting
/// validator consults the same keyword set (same wiring as Striped
/// Riverwinder / Lumbering Falls), so spells / abilities controlled by
/// any player other than the Spirit's controller can't target the
/// affected Spirit while Drogskol Captain is on the battlefield.
///
/// ## Multi-Captain stacking
/// Two Drogskol Captains give other Spirits +2/+2 (and each grants
/// Hexproof — the keyword set is idempotent so the second Hexproof is a
/// no-op via <c>HashSet</c> semantics in
/// <see cref="CreatureCharacteristics.Keywords"/>). The Captains
/// themselves still don't buff each other because each Captain excludes
/// itself from its own static — but Captain A's static buffs Captain B
/// (and vice versa), so two Captains in play are each effectively 3/3
/// with hexproof + flying (printed Flying on each, hexproof + +1/+1
/// from the other's static).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits
///   when Captain isn't on the battlefield so the bonus + hexproof
///   lift correctly. Same posture as Goblin Chieftain / Plague Engineer.
/// </summary>
[CardName("Drogskol Captain")]
public static class DrogskolCaptainFactory
{
    public const string CardName = "Drogskol Captain";
    public const string PrintedManaCost = "{1}{W}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Drogskol Captain with the printed Flying keyword wired
    /// but no live continuous-effects service. Suitable for shape /
    /// dispatcher tests — the lord static effect is not registered.
    /// Other Spirits you control don't yet receive +1/+1 + Hexproof
    /// because there's no layers service to register the effect against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Drogskol Captain. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Hexproof to
    /// other Spirit creatures the controller controls is registered
    /// against the layers service. The printed Flying keyword on
    /// Captain itself is always wired (consumed by the combat-validator
    /// block restrictions).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Hexproof static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying on Drogskol Captain itself. KeywordAbility
        // marker consumed by the combat-validator block restrictions.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords) — "Other
            // Spirit creatures you control get +1/+1 and have hexproof."
            // includeSelf: false honours the printed "Other" rider.
            // Default controller filter (no allPlayers, no opponentsOnly)
            // honours the "you control" scope. Captain itself doesn't
            // double-buff from its own static; printed Flying above is
            // its only intrinsic keyword.
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Spirit,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Hexproof" },
                includeSelf: false,
                opponentsOnly: false));
        }

        return card;
    }
}
