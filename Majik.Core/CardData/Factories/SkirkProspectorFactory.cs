using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skirk Prospector (Onslaught / many reprints,
/// {R}).
///
/// Creature — Goblin 1/1. Oracle text:
///   "Sacrifice a Goblin: Add {R}."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Goblin, mana cost {R}, owner/controller wired.
/// - <b>Mana ability (CR 605.1)</b>: "Sacrifice a Goblin: Add {R}." Wired
///   as a <see cref="ManaAbility"/> with <c>tapsAsCost: false</c> (the
///   printed cost is a single sacrifice — no {T}). The activation gate
///   (<c>canActivate</c>) checks that the controller has at least one
///   Goblin on their battlefield (including Skirk Prospector itself —
///   "Sacrifice a Goblin" has no "other" qualifier per oracle). The
///   additional-cost payer picks the first available Goblin
///   deterministically (matching <see cref="Majik.Core.Costs.SacrificeAnotherCreatureCost"/>
///   v1 behaviour) and routes it to the graveyard via raw zone
///   manipulation. The {R} output is added to the controller's mana pool
///   by <see cref="ManaAbility.Activate"/> as for any other mana source.
///
/// ## "Sacrifice a Goblin" — includes self
/// The oracle has no "another" qualifier, so Skirk Prospector itself is a
/// valid sacrifice. This is the canonical Goblin combo line — chain
/// Prospector with itself as the last sacrifice to convert a board of N
/// Goblins into N mana (with one Goblin remaining once Prospector eats
/// itself). The gate uses controller-side Battlefield Goblin presence;
/// the picker prefers other Goblins first but will sacrifice self when
/// it's the only candidate.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven sacrifice picker</b>: v1 chooses deterministically
///   (other Goblins before self), so optimal play (save Prospector for
///   last) is approximated but not agent-driven. A real "choose a Goblin
///   to sacrifice" prompt is queued behind the same agent-prompt MVP that
///   <see cref="Majik.Core.Costs.SacrificeAnotherCreatureCost.Target"/>
///   uses as a manual setter.
/// - <b>Per-slot mana provenance</b>: produced mana is plain {R} with no
///   <see cref="Majik.Core.Mana.SpendRestriction"/> rider — this matches
///   the printed oracle (no "Spend this mana only…" clause).
/// </summary>
[CardName("Skirk Prospector")]
public static class SkirkProspectorFactory
{
    public const string CardName = "Skirk Prospector";
    public const string PrintedManaCost = "{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Output: add one red mana per sacrifice.
    /// </summary>
    public const string ManaProduced = "R";

    /// <summary>
    /// Construct Skirk Prospector with the sacrifice-a-Goblin mana ability
    /// wired. Single-arg dispatcher path — suitable for shape, dispatcher,
    /// and unit-test usage. The mana ability is a non-tap mana ability per
    /// oracle text.
    /// </summary>
    public static Creature Create(Player owner)
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

        // ----------------------------------------------------------------
        // "Sacrifice a Goblin: Add {R}." — CR 605.1 mana ability (doesn't
        // use the stack). Printed cost is a single sacrifice; there is no
        // {T} in the printed activation cost, so tapsAsCost = false.
        //
        // CanActivate: at least one Goblin on the controller's battlefield
        // (includes self — oracle has no "another" qualifier).
        // AdditionalCostPayer: sacrifice one Goblin. Prefers other Goblins
        // (so the line "tap a row of Goblins for red, finish on
        // Prospector" plays out naturally with a deterministic picker);
        // falls back to self when self is the only candidate.
        // ----------------------------------------------------------------
        bool CanSacAGoblin()
        {
            var ctrl = card.Controller ?? owner;
            return ctrl.Zones.Battlefield.GetCards()
                .Any(c => c.HasSubtype(CardSubtype.Goblin));
        }

        void SacrificeAGoblin(Player ctrl)
        {
            ArgumentNullException.ThrowIfNull(ctrl);

            // Deterministic v1: prefer sacrificing another Goblin first
            // (saves Prospector for the chain-end), fall back to self when
            // self is the only Goblin on the battlefield.
            ICard? pick = ctrl.Zones.Battlefield.GetCards()
                .FirstOrDefault(c =>
                    c.HasSubtype(CardSubtype.Goblin) && !ReferenceEquals(c, card))
                ?? ctrl.Zones.Battlefield.GetCards()
                    .FirstOrDefault(c => c.HasSubtype(CardSubtype.Goblin));

            if (pick == null)
            {
                throw new InvalidOperationException(
                    "Skirk Prospector activation requires at least one Goblin to sacrifice.");
            }

            ctrl.Zones.Battlefield.RemoveCard(pick);
            ctrl.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        }

        var manaAbility = new ManaAbility(
            source: card,
            controller: owner,
            manaGenerated: ManaCost.Parse(ManaProduced),
            canActivateCheck: CanSacAGoblin,
            additionalCostPayer: SacrificeAGoblin,
            tapsAsCost: false);

        card.AddAbility(manaAbility);

        return card;
    }
}
