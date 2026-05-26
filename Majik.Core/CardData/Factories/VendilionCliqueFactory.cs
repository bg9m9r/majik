using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vendilion Clique (Morningtide, {1}{U}{U}).
///
/// Legendary Creature — Faerie Wizard 3/1. Oracle text:
///   "Flash.
///    Flying.
///    When Vendilion Clique enters, look at target player's hand. You may
///    choose a nonland card from it. If you do, that player reveals the
///    chosen card, puts it on the bottom of their library, then draws a
///    card."
///
/// ## Implemented (v1)
/// - 3/1 Legendary Creature — Faerie Wizard, mana cost {1}{U}{U}.
/// - <b>Flash</b> (CR 702.8) — wired as a <see cref="KeywordAbility"/>;
///   same shape as <see cref="SpellQuellerFactory"/>'s Flash grant. Lets
///   the Clique be cast at instant speed (CR 116.1c gates target-decided
///   triggers off the agent's priority loop, which this factory does not
///   touch).
/// - <b>Flying</b> (CR 702.9) — wired as a <see cref="KeywordAbility"/>;
///   evasion check is enforced by the combat damage step
///   (CR 509.1b / CR 702.9b).
/// - <b>ETB triggered ability</b> (CR 603.6a) — declares a single 1..1
///   <see cref="TargetRequest"/> for "target player". The candidate
///   gatherer enumerates EVERY player (the printed text is "target player",
///   not "target opponent" — Vendilion Clique can target its own
///   controller for a self-cantrip-with-bottom-tuck, same printed
///   semantics as Cabal Therapy's "target player"). On resolve:
///   <list type="bullet">
///     <item>"Look at target player's hand" (CR 701.16) — the engine's
///       hand state is publicly observable to every agent, so the look is
///       a no-op for state but is documented as the prompt anchor.</item>
///     <item>"You may choose a nonland card from it" — v1 deterministic
///       first-nonland pick under <c>!HasType(Land)</c> filter, mirroring
///       <see cref="BrainMaggotFactory"/>'s pick (caster-choice prompt
///       deferred — same agent-prompt gap shared with Grief / Brain
///       Maggot).</item>
///     <item>"If you do, that player reveals the chosen card, puts it on
///       the bottom of their library, then draws a card." On a successful
///       pick: route the card Hand → bottom of Library (CR 701.16 reveal
///       is implicit), then draw 1 card from the top of the same library
///       (CR 121). When the target's hand is empty / land-only, the "you
///       may" half declines cleanly and the draw is skipped (printed
///       "If you do" — the rider is conditional on the choice happening,
///       CR 117.6b).</item>
///   </list>
///
/// ## Self-targeting
/// CR 109.5 — "target player" includes the controller. Vendilion Clique
/// can target its own controller to bottom one of their own cards and
/// draw a fresh one — observationally a Brainstorm-style smooth out. v1
/// supports this path because the candidate gatherer enumerates every
/// player; no controller-side scoping is applied.
///
/// ## Deferred (v1 gaps)
/// - <b>Caster's choice prompt</b>: CR 701.16 — "you choose a nonland
///   card". v1 picks the first nonland card deterministically; an
///   agent-driven prompt for the caster to pick any nonland (or decline)
///   from the revealed hand is deferred. Same posture as Brain Maggot /
///   Grief.
/// - <b>"You may"</b>: when the target's hand has at least one nonland
///   card, v1 auto-accepts (mirrors Brain Maggot's auto-pick). The
///   printed text allows decline — a future agent prompt would gate this
///   on a <see cref="IPlayerAgent.ChooseYesNoAsync"/> probe with intent
///   <see cref="BotIntent.HandHate"/>.
/// - <b>Public reveal event</b>: a dedicated <c>CardRevealedEvent</c> for
///   UI fan-out is not synthesised; the target's hand state is already
///   publicly inspectable when a live event bus is wired at the game
///   level. Same posture as Brain Maggot.
/// </summary>
[CardName("Vendilion Clique")]
public static class VendilionCliqueFactory
{
    public const string CardName = "Vendilion Clique";
    public const string PrintedManaCost = "{1}{U}{U}";
    public const int Power = 3;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Vendilion Clique with no live TriggerManager wiring. The
    /// ETB ability is attached to the card shape but not registered with a
    /// bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Vendilion Clique with optional TriggerManager wiring.
    /// When <paramref name="triggers"/> is supplied the ETB ability is
    /// registered so a self-ETB CardMovedEvent automatically queues the
    /// ability.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Faerie, CardSubtype.Wizard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.8 — Flash. Allows casting at instant speed.
        card.AddAbility(new KeywordAbility("Flash", card, owner));

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a / CR 701.16.
        //   "When Vendilion Clique enters, look at target player's hand.
        //    You may choose a nonland card from it. If you do, that player
        //    reveals the chosen card, puts it on the bottom of their
        //    library, then draws a card."
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: look at target player's hand, may bottom nonland; if you do, they draw",
            () =>
            {
                if (etb == null) return;
                var chosen = etb.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                if (chosen[0][0] is not Player target) return;

                // CR 701.16 — "look at target player's hand" is a public
                // information transition for the casting controller; the
                // engine already exposes hands to every agent, so the look
                // is observationally a no-op. Predicate-driven flow below.

                // v1 deterministic pick — first nonland card in the
                // target's hand (caster-choice prompt deferred; same
                // posture as BrainMaggotFactory / GriefFactory).
                var pick = target.Zones.Hand.GetCards()
                    .FirstOrDefault(c => !c.HasType(CardType.Land));

                if (pick == null)
                {
                    // CR 117.6b — "If you do" gates the draw on the
                    // choice having been made. Empty / land-only hand →
                    // no bottom, no draw.
                    return;
                }

                // Hand → bottom of library. CR 701.16 — "reveals the
                // chosen card" is implicit in the public-information
                // posture; the move itself is what matters.
                target.Zones.Hand.RemoveCard(pick);
                target.Zones.Library.AddCard(pick); // AddCard appends — that's the bottom.
                pick.SetZone(ZoneType.Library);

                // "then draws a card" — CR 121.2. Top of library → hand.
                // This is the OTHER end of the library from where we just
                // bottomed pick (Library.AddCard appends, so the previous
                // top is unchanged).
                var top = target.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                target.Zones.Library.RemoveCard(top);
                target.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.HandHate),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        return card;
    }
}
