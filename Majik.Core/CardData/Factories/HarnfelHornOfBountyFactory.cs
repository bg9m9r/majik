using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Birgi, God of Storytelling // Harnfel, Horn of Bounty (Kaldheim).
///
/// Harnfel, Horn of Bounty ({4}{R}):
///   Legendary Artifact.
///   "Discard a card: Exile the top two cards of your library. You may play
///    those cards this turn."
///
/// This is the materialized back-face permanent the MDFC cast flow builds when
/// the controller chooses the Harnfel face (deferral #3 residual / #2 — modal
/// permanent backs). It is its own complete card: a Legendary Artifact that
/// goes on the stack as a spell and, on resolution, enters the battlefield as
/// the Artifact (CR 608.3). The front face (Birgi) simply isn't there
/// (CR 712.4 — no transform). Its <see cref="MdfcState.IsBackFace"/> reads true
/// so it never offers a further cast-either-face choice.
///
/// ## Implemented (v1)
/// - Legendary Artifact identity at {4}{R}, owner / controller wired.
/// - <see cref="MdfcState"/> attached on the back face (front =
///   "Birgi, God of Storytelling", back = "Harnfel, Horn of Bounty",
///   <see cref="MdfcState.IsBackFace"/> = true) so the card is observably the
///   back face.
/// - <b>"Discard a card: Exile the top two cards of your library. You may play
///   those cards this turn."</b> — a <see cref="DiscardACardCost"/>-gated
///   <see cref="ActivatedAbility"/> (CR 602.1). The effect exiles the top two
///   library cards and grants the controller a cast-from-exile permission for
///   each (CR 118.7 — "may play"), using the printed mana cost of each.
///
/// ## Deferred (v1 gaps)
/// - <b>"You may play those cards this turn" expiry</b> — the cast-from-exile
///   grant is stamped but its "until end of turn" expiry is the shared
///   duration-tracking gap; the permission persists for the turn / test.
/// - <b>Discard prompt</b> — the embedded <see cref="DiscardACardCost"/> uses
///   its deterministic first-card-in-hand picker (shared v1 discard gap).
/// </summary>
[CardName("Harnfel, Horn of Bounty")]
public static class HarnfelHornOfBountyFactory
{
    public const string CardName = "Harnfel, Horn of Bounty";
    public const string FrontName = "Birgi, God of Storytelling";
    public const string PrintedManaCost = "{4}{R}";

    /// <summary>Construct Harnfel with no live ReplacementBus wiring
    /// (shape / activation tests).</summary>
    public static Artifact Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Harnfel, Horn of Bounty as a back-face Legendary Artifact.
    /// <paramref name="replacements"/> is accepted to match the
    /// <see cref="MdfcFace.Permanent"/> builder signature (Harnfel has no ETB
    /// replacement, so it is currently unused — present for parity with land /
    /// permanent backs that do).
    /// </summary>
    public static Artifact Create(Player owner, Effects.ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            CardName,
            PrintedManaCost,
            supertypes: new[] { CardSupertype.Legendary });
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 712 — back-face card: its MdfcState reads as the back face so it
        // never offers a further cast-either-face choice (CanCastEitherFace is
        // false because IsBackFace is true).
        card.MdfcState = new MdfcState(FrontName, CardName);
        card.MdfcState.Transform(); // flip to the back face (Harnfel)

        // CR 602.1 — "Discard a card: Exile the top two cards of your library.
        // You may play those cards this turn."
        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardACardCost() },
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: exile the top two cards of your library; you may play them this turn",
                    () => ResolveExileTopTwo(card, owner)),
            });
        card.AddAbility(ability);

        return card;
    }

    /// <summary>
    /// CR 701.10 / 118.7 — exile the top two cards of the controller's library
    /// and grant a cast-from-exile permission for each (at its printed cost),
    /// modelling "you may play those cards this turn".
    /// </summary>
    private static void ResolveExileTopTwo(Artifact source, Player owner)
    {
        var controller = source.Controller ?? owner;

        var topTwo = controller.Zones.Library.GetCards().Take(2).ToList();
        foreach (var card in topTwo)
        {
            Fx.MoveToExile(card);

            // CR 118.7 — "you may play those cards this turn." Grant the
            // controller a cast-from-exile permission at the card's printed
            // cost (same probe surface Ragavan / Cascade / Adventure use).
            if (card is Card concrete)
            {
                concrete.GrantRuntimeExileCast(controller, concrete.ManaCostValue);
            }
        }
    }
}
