using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin Chieftain (Magic 2010 / many reprints,
/// Creature — Goblin Warrior {1}{R}{R}).
///
/// Oracle text:
///   "Haste.
///    Other Goblin creatures you control have haste and get +1/+1."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin Warrior, mana cost {1}{R}{R}, owner/controller wired.
/// - <b>Haste</b> on Goblin Chieftain itself (CR 702.10) — wired as a
///   <see cref="KeywordAbility"/> marker.
/// - <b>Static "Other Goblin creatures you control have haste and get
///   +1/+1"</b> wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Goblin</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Haste"]</c>, <c>includeSelf: false</c>. Layer 7c
///   for P/T and Layer 6 for the granted keyword (LordStaticEffect MVP
///   places both at <see cref="Layer.PT_Modify"/>). The "other" clause is
///   honoured by <c>includeSelf: false</c> — Chieftain itself doesn't get
///   the +1/+1 from its own static (its own Haste comes from the printed
///   keyword above). Scoped to the controller's battlefield (default
///   filter — not <c>opponentsOnly</c>).
///
/// Multiple copies stack: two Goblin Chieftains give Other Goblins +2/+2
/// (and each grants Haste — the keyword set is idempotent so the second
/// Haste is a no-op via <c>HashSet</c> semantics in
/// <see cref="CreatureCharacteristics.Keywords"/>). Symmetric across all
/// Goblin creatures the controller controls — Goblin Warriors, Goblin
/// Wizards (Goblin Electromancer), Goblin Artificers (Goblin Engineer),
/// etc., all benefit equally.
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Chieftain isn't on the battlefield so the bonus lifts correctly, but
///   a future Prune pass could drop the entry. Same shape as Plague
///   Engineer / Colossus Hammer.
/// </summary>
[CardName("Goblin Chieftain")]
public static class GoblinChieftainFactory
{
    public const string CardName = "Goblin Chieftain";
    public const string PrintedManaCost = "{1}{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin Chieftain with the printed Haste keyword wired but
    /// no live continuous-effects service. Suitable for shape / dispatcher
    /// tests — the lord static effect is not registered. Other Goblins
    /// you control don't yet receive +1/+1 + Haste because there's no
    /// layers service to register the effect against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Goblin Chieftain. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Haste to other
    /// Goblin creatures the controller controls is registered against the
    /// layers service. The printed Haste keyword on Chieftain itself is
    /// always wired (consumed by
    /// <see cref="Majik.Core.Combat.CombatAbilities.HasHaste"/>).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Haste static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste on Goblin Chieftain itself. KeywordAbility
        // marker; CombatAbilities.HasHaste reads it.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords) — "Other Goblin
            // creatures you control have haste and get +1/+1." includeSelf
            // is false so the Chieftain itself doesn't double-stack +1/+1
            // from its own static (its own Haste comes from the printed
            // keyword above). Controller capture is at register time —
            // control-change re-eval is a follow-up (same caveat as Plague
            // Engineer).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Haste" },
                includeSelf: false,
                opponentsOnly: false));
        }

        return card;
    }
}
