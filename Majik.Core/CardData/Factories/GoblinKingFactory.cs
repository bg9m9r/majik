using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Goblin King (Limited Edition Alpha / many
/// reprints, Creature — Goblin {R}{R}).
///
/// Oracle text (Scryfall, verified):
///   "Other Goblin creatures get +1/+1 and have mountainwalk. (They can't
///    be blocked as long as defending player controls a Mountain.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Goblin at {R}{R}, owner/controller wired.
/// - <b>Static "Other Goblin creatures get +1/+1 and have mountainwalk"</b>
///   wired via <see cref="LordStaticEffect"/>:
///   <c>matchingSubtype: Goblin</c>, <c>power: 1, toughness: 1</c>,
///   <c>grantedKeywords: ["Mountainwalk"]</c>, <c>includeSelf: false</c>,
///   <c>allPlayers: true</c>. Layer 7c for P/T and Layer 6 for the
///   granted keyword (LordStaticEffect MVP places both at
///   <see cref="Layer.PT_Modify"/>).
/// - <c>allPlayers: true</c> is the Lord-of-Atlantis shape: the printed
///   oracle says "Other Goblin creatures" with NO "you control" rider, so
///   every Goblin on the battlefield (controlled by anyone) gets +1/+1 +
///   Mountainwalk while a Goblin King is on the battlefield. <c>includeSelf:
///   false</c> honours the "Other" qualifier — a Goblin King doesn't buff
///   itself (its own static doesn't pump it), but a SECOND Goblin King
///   does pump the first (and vice-versa), so two Kings produce a 4/4 +
///   Mountainwalk Goblin King pair (1+1+1+1 +1/+1 each from the other's
///   static stacks with the +1/+1 + Mountainwalk that flows from the same
///   static onto every other Goblin in play).
///
/// Mountainwalk is registered as a keyword string in
/// <see cref="Majik.Core.CardData.Parsing.KeywordRegistry"/> ("mountainwalk");
/// the data-layer grant via <c>grantedKeywords</c> stamps it on the buffed
/// Goblins' <see cref="CreatureCharacteristics.Keywords"/> set. Combat
/// enforcement of the unblockable rider (CR 702.14b — "can't be blocked
/// as long as defending player controls a Mountain") is the same shape
/// as the other landwalk variants (Islandwalk / Swampwalk / etc.) — keyword
/// enforcement in the combat layer is a downstream gap, same posture as
/// Haste from Goblin Chieftain / Goblin Warchief (the marker is stamped;
/// the combat helper reads it when ready).
///
/// ## Deferred (v1 gaps)
/// - <b>LTB unregister</b>: the registered <see cref="LordStaticEffect"/>
///   stays on the <see cref="ContinuousEffectsService"/> across zone
///   changes; <see cref="ContinuousEffect.IsActive"/> short-circuits when
///   Goblin King isn't on the battlefield so the bonus lifts correctly,
///   but a future Prune pass could drop the entry. Same shape as Plague
///   Engineer / Goblin Chieftain / Goblin Rabblemaster.
/// - <b>Mountainwalk combat enforcement</b>: the granted keyword is
///   visible on the affected creatures' keyword set but
///   <see cref="Majik.Core.Combat"/> declare-blockers doesn't yet consult
///   landwalk markers to reject blocks (CR 702.14b). Same gap as every
///   landwalk variant. Buff half (P/T) is fully observable through
///   <see cref="Creature.GetPower"/> / <see cref="Creature.GetToughness"/>.
/// </summary>
[CardName("Goblin King")]
public static class GoblinKingFactory
{
    public const string CardName = "Goblin King";
    public const string PrintedManaCost = "{R}{R}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Goblin King with no live continuous-effects service.
    /// Suitable for shape / dispatcher tests — the lord static effect is
    /// not registered. Other Goblins don't yet receive +1/+1 + Mountainwalk
    /// because there's no layers service to register the effect against.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, continuousEffects: null);

    /// <summary>
    /// Construct a fully-wired Goblin King. When
    /// <paramref name="continuousEffects"/> is supplied, a
    /// <see cref="LordStaticEffect"/> granting +1/+1 and Mountainwalk to
    /// every OTHER Goblin in play (controlled by any player — Lord of
    /// Atlantis posture) is registered against the layers service.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="continuousEffects">Layers service to register the
    /// +1/+1 + Mountainwalk static effect against. May be null — no live
    /// bonus.</param>
    public static Creature Create(Player owner, ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Goblin });

        card.SetOwner(owner);
        card.SetController(owner);

        if (continuousEffects != null)
        {
            // CR 613.7c (P/T) + CR 613.1f (granted keywords) — "Other Goblin
            // creatures get +1/+1 and have mountainwalk." The printed text
            // has no "you control" rider so the buff applies to every
            // Goblin in play regardless of controller (allPlayers: true).
            // includeSelf: false honours the "Other" qualifier — a single
            // Goblin King doesn't buff itself; two Goblin Kings buff each
            // other (each is "Other" relative to the other's static).
            continuousEffects.Register(new LordStaticEffect(
                source: card,
                matchingSubtype: CardSubtype.Goblin,
                power: 1,
                toughness: 1,
                grantedKeywords: new[] { "Mountainwalk" },
                includeSelf: false,
                opponentsOnly: false,
                allPlayers: true));
        }

        return card;
    }
}
