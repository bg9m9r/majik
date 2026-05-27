using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Night's Whisper (Fifth Dawn, {1}{B}).
///
/// Sorcery. Oracle text:
///   "You draw two cards and you lose 2 life."
///
/// ## Rules
///
/// Night's Whisper targets no one — the caster draws two cards and loses
/// 2 life on resolution. CR 119.3 — "a player loses life" is an
/// event-based life-loss (not damage, not payment); CR 121.1 — the draw
/// happens in the printed order (draw then lose, though both are a single
/// instruction — Oracle "and" = simultaneous for state tracking purposes).
///
/// ## Implementation
///
/// - Sorcery shape, mana cost {1}{B}, black (derived from mana cost).
/// - <see cref="BuildResolveEffect"/> loops 2 draws in the same pattern
///   as <see cref="TormentingVoiceFactory"/> / <see cref="MulldrifterFactory"/>:
///   pull top of library to hand, flag the SBA if the library is empty
///   (CR 704.5b), then call <see cref="Player.LoseLife"/>(2). Life loss
///   runs regardless of whether the draws succeeded — the printed text
///   doesn't gate the loss on drawing.
///
/// ## Deferred (v1 gaps)
///
/// - No interaction with Spectacle / life-loss-matters triggers beyond
///   what <see cref="Player.LoseLife"/> already tracks via
///   <see cref="Player.LifeLostThisTurn"/>.
/// </summary>
[CardName("Night's Whisper")]
public static class NightsWhisperFactory
{
    public const string CardName = "Night's Whisper";
    public const string PrintedManaCost = "{1}{B}";
    public const int DrawCount = 2;
    public const int LifeLost = 2;

    /// <summary>
    /// Build a Night's Whisper sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/>.
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
    /// Build Night's Whisper's resolve effect — draw two cards, then lose
    /// 2 life. Both happen to the caster; no target is involved (CR 114.1).
    /// </summary>
    /// <param name="caster">The player drawing and losing life.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(Player caster)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect("Night's Whisper: draw two cards and lose 2 life.", () =>
            {
                // CR 121.1 — draw two cards. Empty library mid-draw stamps
                // the SBA loss flag (CR 704.5b) and short-circuits remaining
                // draws — same pattern as TormentingVoiceFactory.
                for (var i = 0; i < DrawCount; i++)
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

                // CR 119.3 — lose 2 life. Runs regardless of draw outcome;
                // printed oracle does not gate life-loss on drawing.
                caster.LoseLife(LifeLost);
            }),
        };
    }
}
