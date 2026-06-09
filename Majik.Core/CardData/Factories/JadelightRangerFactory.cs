using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jadelight Ranger (Rivals of Ixalan, {1}{G}).
/// Creature — Merfolk Scout Ranger 2/1.
///
/// Oracle text (verified against Scryfall):
///   "When this creature enters, it explores, then it explores again. (Reveal
///    the top card of your library. Put that card into your hand if it's a
///    land. Otherwise, put a +1/+1 counter on this creature, then put the card
///    back or put it into your graveyard. Then repeat this process.)"
///
/// ## Implemented (v1)
/// - 2/1 Merfolk Scout Ranger, mana cost {1}{G}, owner / controller wired.
/// - <b>ETB double-explore</b> (CR 603.6a + CR 701.40): an unconditional
///   self-ETB triggered ability — identical shape to
///   <see cref="SeekersSquireFactory"/> / <see cref="MerfolkBranchwalkerFactory"/>,
///   wired via the shared <see cref="ExploreEtb.Attach"/> helper with
///   <c>exploreCount: 2</c>. The two explores run in sequence (CR 701.40 —
///   "then it explores again"); the exploring permanent is Jadelight Ranger
///   itself, so each non-land reveal lands a +1/+1 counter on this creature
///   (CR 701.40c). The controller closure re-resolves at execute time so blink
///   / control-change scenarios explore for the correct player.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only (ETB trigger attached, not
///   registered with a <see cref="TriggerManager"/>).
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the ETB trigger
///   so the relevant <c>CardMovedEvent</c> stacks it (CR 603.3).
/// </summary>
[CardName("Jadelight Ranger")]
public static class JadelightRangerFactory
{
    public const string CardName = "Jadelight Ranger";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 2;
    public const int Toughness = 1;

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Scout, CardSubtype.Ranger });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 701.40 — "explores, then it explores again": two sequential
        // explores via the shared ETB helper.
        ExploreEtb.Attach(card, owner, triggers, exploreCount: 2);

        return card;
    }
}
