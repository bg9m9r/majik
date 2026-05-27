using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Counsel of the Soratami (Champions of Kamigawa, {2}{U}).
///
/// Sorcery. Oracle text:
///   "Draw two cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{U}.
/// - <b>Resolve effect (via <see cref="BuildResolveEffect"/>)</b>: draws two
///   cards from the top of the caster's library (CR 121.1). Empty library
///   mid-draw flags the player for the SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Replacement-effect draws</b>: draws are performed via direct
///   top-of-library zone moves (same posture as
///   <see cref="ThoughtcastFactory"/>), not through a centralised
///   "Player.DrawCard" pipeline. Draw-replacement effects (e.g. Dredge,
///   Maralen of the Mornsong) won't see these draws until a unified draw
///   API lands — engine-wide gap, not card-specific.
/// </summary>
[CardName("Counsel of the Soratami")]
public static class CounselOfTheSoratamiFactory
{
    public const string CardName = "Counsel of the Soratami";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Build a Counsel of the Soratami sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can splice
    /// it into a <see cref="Majik.Core.Game.SpellDefinition"/> or pass it
    /// directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Counsel of the Soratami's resolve effect — draw two cards from
    /// the top of the caster's library (CR 121.1). Empty library mid-draw
    /// flags the SBA loss (CR 704.5b) and short-circuits the remaining draws.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Counsel of the Soratami: draw two cards.", () =>
            {
                // CR 121.1 — two simple top-of-library draws. Empty
                // library mid-draw flags the SBA loss (CR 704.5b) and
                // short-circuits the remaining draws.
                for (var i = 0; i < 2; i++)
                {
                    var top = caster.Zones.Library.GetCards().FirstOrDefault();
                    if (top == null)
                    {
                        caster.MarkTriedToDrawFromEmptyLibrary();
                        break;
                    }
                    caster.Zones.Library.RemoveCard(top);
                    caster.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            }),
        };
    }
}
