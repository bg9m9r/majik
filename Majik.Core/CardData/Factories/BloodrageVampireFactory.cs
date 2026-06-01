using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodrage Vampire (Magic 2012, {2}{B}).
///
/// Creature — Vampire 3/1. Oracle text:
///   "Bloodthirst 1 (If an opponent was dealt damage this turn, this creature
///    enters with a +1/+1 counter on it.)"
///
/// ## Implemented (v1)
/// - 3/1 Creature — Vampire at {2}{B}.
/// - <b>Bloodthirst 1 (CR 702.54)</b> — wired through the shared
///   <see cref="BloodthirstReplacement"/> ETB-counter replacement. When a
///   <see cref="ReplacementBus"/> + opponent resolver are supplied, the card
///   registers a <see cref="ZoneMoveIntent"/> replacement that, only when an
///   opponent has <see cref="Player.WasDealtDamageThisTurn"/> set at ETB time,
///   stamps one +1/+1 counter (entering as a 4/2). With no opponent damaged it
///   enters vanilla 3/1.
///
/// Without a replacement bus the card enters vanilla (shape-only) — matching
/// the Soul-Scar Mage / Steppe Lynx replacement-opt-in posture.
/// </summary>
[CardName("Bloodrage Vampire")]
public static class BloodrageVampireFactory
{
    public const string CardName = "Bloodrage Vampire";
    public const string PrintedManaCost = "{2}{B}";
    public const int Power = 3;
    public const int Toughness = 1;
    public const int BloodthirstAmount = 1;

    /// <summary>Construct with no replacement wiring (shape-only — enters vanilla).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, opponentResolver: null);

    /// <summary>
    /// Construct Bloodrage Vampire with optional Bloodthirst wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="replacements">When supplied alongside an opponent resolver,
    /// the Bloodthirst ETB-counter replacement is registered.</param>
    /// <param name="opponentResolver">Resolver for the controller's opponents,
    /// checked for "was dealt damage this turn" at ETB.</param>
    public static Creature Create(
        Player owner,
        ReplacementBus? replacements,
        Func<IReadOnlyList<Player>>? opponentResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Vampire });

        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null && opponentResolver != null)
        {
            replacements.Register<ZoneMoveIntent>(
                new BloodthirstReplacement(card, BloodthirstAmount, opponentResolver));
        }

        return card;
    }
}
