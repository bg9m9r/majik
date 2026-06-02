using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lord of the Unreal (Magic 2012, {U}{U}).
///
/// Creature — Human Wizard 2/2. Oracle text:
///   "Illusion creatures you control get +1/+1 and have hexproof. (They
///    can't be the targets of spells or abilities your opponents control.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Wizard at {U}{U}, owner / controller wired.
/// - <b>Static "Illusion creatures you control get +1/+1 and have
///   hexproof"</b> wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Illusion</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Hexproof"]</c>, <c>includeSelf: true</c>.
///   Layer 7c for P/T (CR 613.7c) + Layer 6 for the granted keyword
///   (CR 613.1f). The "you control" scope is honoured by the default
///   controller filter (no <c>allPlayers</c>, no <c>opponentsOnly</c>).
///   Unlike Drogskol Captain / Goblin Chieftain, the printed text has no
///   "Other" rider, so <c>includeSelf: true</c> — but Lord of the Unreal
///   is itself a Human Wizard (not an Illusion), so the subtype gate keeps
///   it out of its own buff regardless. The lord has no printed keyword of
///   its own.
///
/// ## Hexproof grant (CR 702.11)
/// The "Hexproof" string is added to each affected Illusion's keyword set
/// via <see cref="CreatureCharacteristics.Keywords"/>. The targeting
/// validator consults the same keyword set (same wiring as Drogskol
/// Captain / Striped Riverwinder / Lumbering Falls), so spells / abilities
/// controlled by any player other than the Illusion's controller can't
/// target the affected Illusion while Lord of the Unreal is on the
/// battlefield.
///
/// ## Multi-Lord stacking
/// Two Lords of the Unreal give Illusions +2/+2 (and each grants Hexproof
/// — the keyword set is idempotent so the second Hexproof is a no-op via
/// <c>HashSet</c> semantics in <see cref="CreatureCharacteristics.Keywords"/>).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   the Lord isn't on the battlefield so the bonus + hexproof lift
///   correctly. Same posture as Drogskol Captain / Goblin Chieftain.
/// </summary>
[CardName("Lord of the Unreal")]
public static class LordOfTheUnrealFactory
{
    public const string CardName = "Lord of the Unreal";
    public const string PrintedManaCost = "{U}{U}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Lord of the Unreal with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effect is not
    /// registered, so Illusions you control don't yet receive +1/+1 +
    /// Hexproof (there's no layers service to register the effect against).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Lord of the Unreal. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Hexproof to
    /// Illusion creatures the controller controls is registered against the
    /// layers service.
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords) — "Illusion
            // creatures you control get +1/+1 and have hexproof." Default
            // controller filter (no allPlayers, no opponentsOnly) honours the
            // "you control" scope. No "Other" rider, so includeSelf: true —
            // though Lord of the Unreal is a Human Wizard, not an Illusion,
            // so the subtype gate excludes it from its own buff anyway.
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Illusion,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Hexproof" },
                includeSelf: true,
                opponentsOnly: false));
        }

        return card;
    }
}
