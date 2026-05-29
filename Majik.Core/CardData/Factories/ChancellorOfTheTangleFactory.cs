using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Chancellor of the Tangle (New Phyrexia,
/// {4}{G}{G}{G}).
///
/// Creature — Phyrexian Beast 6/7. Oracle text (Scryfall, verified):
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first main phase of the game, add {G}."
///   "Vigilance, reach"
///
/// ## Implemented
///
/// - 6/7 Creature — Phyrexian Beast, mana cost {4}{G}{G}{G} (MV 7, green).
///
/// - <b>Opening-hand reveal rider</b> (CR 103.6 / CR 603.7) — implemented
///   as the <see cref="KeywordAbility"/> marker
///   <c>"OpeningHandRevealAddMana:{G}"</c>.
///   The shared <see cref="OpeningHandRevealAddManaTrigger"/> subscriber
///   (wired by <see cref="GameDriver"/> at game start) prompts via
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseYesNoAsync"/>
///   on the <see cref="Majik.Core.Events.OpeningHandCheckEvent"/>; on yes
///   it registers a <see cref="DelayedTriggeredAbility"/> with the supplied
///   <see cref="TriggerManager"/> that fires once on the revealer's first
///   <see cref="Majik.Core.StateMachine.PhaseStateType.PreCombatMain"/>.
///   The delayed trigger adds {G} to the revealer's mana pool (CR 605.1a)
///   then auto-unregisters (CR 603.7d).
///
/// - <b>Vigilance (CR 702.20)</b> — <see cref="KeywordAbility"/> marker.
///   Combat-abilities subsystem reads the marker to prevent tapping when
///   the creature is declared as an attacker.
///
/// - <b>Reach (CR 702.17)</b> — <see cref="KeywordAbility"/> marker.
///   Lets Chancellor block creatures with flying.
///
/// ## Wiring
/// Single <see cref="Create(Player)"/> overload — no cast trigger, no
/// zone service wiring; the opening-hand effect is handled entirely by
/// the shared subscriber registered at game start by
/// <see cref="GameDriver"/>.
/// </summary>
[CardName("Chancellor of the Tangle")]
public static class ChancellorOfTheTangleFactory
{
    public const string CardName = "Chancellor of the Tangle";
    public const string PrintedManaCost = "{4}{G}{G}{G}";
    public const int Power = 6;
    public const int Toughness = 7;

    /// <summary>Mana added at the revealer's first main phase when this
    /// card is revealed from the opening hand. Encoded as the suffix of
    /// the <see cref="OpeningHandRevealAddManaTrigger.RevealKeywordPrefix"/>
    /// marker.</summary>
    public const string ManaProduced = "{G}";

    /// <summary>Full keyword string used as the opening-hand reveal marker.
    /// Stored as a constant so the factory and tests can reference it
    /// without string-building.</summary>
    public const string RevealMarkerKeyword =
        OpeningHandRevealAddManaTrigger.RevealKeywordPrefix + ManaProduced;

    /// <summary>Construct Chancellor of the Tangle.</summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Phyrexian, CardSubtype.Beast });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Opening-hand reveal rider (CR 103.6 / CR 603.7):
        //   "You may reveal this card from your opening hand. If you do,
        //    at the beginning of your first main phase of the game, add {G}."
        //
        // The marker keyword encodes the mana payload as its suffix so
        // the shared OpeningHandRevealAddManaTrigger subscriber can serve
        // any future "reveal → add {X}" Chancellor without per-card wiring.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(RevealMarkerKeyword, card, owner));

        // ----------------------------------------------------------------
        // Vigilance (CR 702.20) — attacker does not tap.
        // Same marker shape as StandingTroopsFactory / KessDissidentMage.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Vigilance", card, owner));

        // ----------------------------------------------------------------
        // Reach (CR 702.17) — can block flying creatures.
        // Same marker shape as HitchclawRecluseFactory / CanopySpider.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        return card;
    }
}
