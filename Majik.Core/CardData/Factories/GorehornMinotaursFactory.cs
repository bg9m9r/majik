using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gorehorn Minotaurs (Magic 2012, {2}{R}{R}).
///
/// Creature — Minotaur Warrior 3/3. Oracle text:
///   "Bloodthirst 2 (If an opponent was dealt damage this turn, this creature
///    enters with two +1/+1 counters on it.)"
///
/// ## Implemented (v1)
/// - 3/3 Creature — Minotaur Warrior at {2}{R}{R}.
/// - <b>Bloodthirst 2 (CR 702.54)</b> — wired through the shared
///   <see cref="BloodthirstReplacement"/>. When an opponent was dealt damage
///   this turn, the creature enters with two +1/+1 counters (a 5/5); otherwise
///   vanilla 3/3. Proves the reusable Bloodthirst mechanic at N=2 (Bloodrage
///   Vampire covers N=1).
/// </summary>
[CardName("Gorehorn Minotaurs")]
public static class GorehornMinotaursFactory
{
    public const string CardName = "Gorehorn Minotaurs";
    public const string PrintedManaCost = "{2}{R}{R}";
    public const int Power = 3;
    public const int Toughness = 3;
    public const int BloodthirstAmount = 2;

    /// <summary>Construct with no replacement wiring (shape-only — enters vanilla).</summary>
    public static Creature Create(Player owner) =>
        Create(owner, replacements: null, opponentResolver: null);

    /// <summary>Construct Gorehorn Minotaurs with optional Bloodthirst wiring.</summary>
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
            subtypes: new[] { CardSubtype.Minotaur, CardSubtype.Warrior });

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
