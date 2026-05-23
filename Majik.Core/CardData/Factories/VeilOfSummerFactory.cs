using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Veil of Summer (Core Set 2020, {G}).
///
/// Instant. Oracle text:
///   "Draw a card if an opponent has cast a blue or black spell this turn.
///    Spells you control can't be countered this turn, and you and
///    permanents you control gain hexproof from blue and from black until
///    end of turn."
///
/// ## Implemented (v1)
/// - Instant {G} (Green) card shape with owner / controller wired.
/// - <b>Conditional draw</b> — on resolve, consults
///   <see cref="TurnState.OpponentCastSpellOfColor"/> for any opposing
///   blue/black cast this turn. If found, the controller draws 1 card
///   (top of library → hand).
/// - <b>Uncounterable rider (structural)</b> — registers a turn-scoped
///   "spells this controller cast this turn can't be countered" flag via
///   <see cref="CastingRestrictions.AddUncounterableForTurn"/>. The flag
///   is structural for v1: counter primitives (Spell Snare, Force of
///   Negation, Counterspell, …) do not yet consult it. Wiring is a
///   follow-up; see <c>CastingRestrictions.SpellsCannotBeCountered</c>.
/// - <b>Hexproof from blue/black (structural)</b> — for each creature
///   currently on the battlefield under the controller's control, the
///   resolver registers two
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/>s: one granting
///   "Hexproof from Blue" and one granting "Hexproof from Black".
///   <see cref="TargetLegality"/> v1 only checks the bare "Hexproof"
///   keyword; the colour-qualified variants land on the creature's
///   keyword set so the structural test passes, but full targeting-time
///   enforcement of "Hexproof from X" awaits a dedicated check in
///   <c>TargetLegality</c>. Player-side hexproof (the "you" half of the
///   clause) requires player-keyword infrastructure not in the engine
///   today and is deferred.
///
/// ## Notes on CR citations
/// - CR 105 — spell colour read from the card's mana cost via
///   <see cref="CardColors.GetColors(ICard)"/>.
/// - CR 514.2 — until-end-of-turn effects expire at cleanup.
/// - CR 702.11 — Hexproof (and its colour-qualified variants).
/// - CR 701.5 — Counter; uncounterable spells can't be countered.
/// </summary>
public static class VeilOfSummerFactory
{
    public const string CardName = "Veil of Summer";

    /// <summary>Card shape. Instant {G}.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, "{G}");
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. No targets,
    /// no modes. Pass the live <paramref name="turnState"/> so the draw
    /// clause can consult opponent spell-cast colours; pass
    /// <paramref name="continuousEffects"/> to register hexproof
    /// grants. Either may be null in test paths that only exercise the
    /// uncounterable rider.
    /// </summary>
    public static SpellDefinition BuildDefinition(
        Player caster,
        TurnState? turnState,
        ContinuousEffectsService? continuousEffects) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: _ => new IEffect[]
            {
                new Effect("Veil of Summer — conditional draw + uncounterable + hexproof UB", () =>
                {
                    Resolve(caster, turnState, continuousEffects);
                }),
            });

    /// <summary>
    /// Apply all three resolution clauses for Veil of Summer. Exposed
    /// (internal-only) so tests can drive resolution without standing up
    /// the full <see cref="SpellCastFlow"/>.
    /// </summary>
    internal static void Resolve(
        Player caster,
        TurnState? turnState,
        ContinuousEffectsService? continuousEffects)
    {
        if (caster == null) return;

        // 1. Conditional draw — opponent has cast a blue OR black spell.
        if (turnState != null
            && turnState.OpponentCastSpellOfColor(caster, ManaColor.Blue, ManaColor.Black))
        {
            DrawOne(caster);
        }

        // 2. Uncounterable rider for the turn (structural — see class doc).
        CastingRestrictions.AddUncounterableForTurn(caster);

        // 3. Hexproof from Blue + Hexproof from Black on every creature
        //    the controller currently controls, until end of turn.
        //    Structural for v1 (TargetLegality only checks bare "Hexproof").
        if (continuousEffects != null)
        {
            foreach (var perm in caster.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
            {
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(perm, "Hexproof from Blue"));
                continuousEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(perm, "Hexproof from Black"));
            }
        }
    }

    private static void DrawOne(Player p)
    {
        var top = p.Zones.Library.GetCards().FirstOrDefault();
        if (top == null)
        {
            // CR 704.5b — drawing from an empty library flags for SBA loss.
            p.TriedToDrawFromEmptyLibrary = true;
            return;
        }
        p.Zones.Library.RemoveCard(top);
        p.Zones.Hand.AddCard(top);
        top.SetZone(ZoneType.Hand);
    }
}
