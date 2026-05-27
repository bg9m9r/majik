using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Exclude (Judgment, {2}{U}).
///
/// Instant. Oracle text:
///   "Counter target creature spell. Draw a card."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{U}, blue, CMC 3.
/// - 1..1 target spell request. At resolution the engine checks whether
///   the target spell has <see cref="CardType.Creature"/>; if not, the
///   whole spell does nothing — no counter, no draw (CR 608.2b illegal-
///   target check, same defensive-resolve posture as
///   <see cref="AnnulFactory"/> / <see cref="NegateFactory"/>).
/// - Counter: <see cref="OracleSpellBinder.RemoveFromStack"/> + graveyard
///   zone-move (CR 701.5).
/// - Draw: the spell's controller draws 1 card from the top of their
///   library (CR 121.1). Empty library mid-draw flags the player for the
///   SBA-driven loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>. The draw only
///   fires when the counter resolves against a legal creature-spell target;
///   if the target is illegal at resolution (CR 608.2b) the entire effect
///   does nothing.
/// </summary>
[CardName("Exclude")]
public static class ExcludeFactory
{
    public const string CardName = "Exclude";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>CardDef DSL — card shape only. The creature-spell counter +
    /// draw SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "counter target creature spell; draw a card"
    /// SpellDefinition.
    ///
    /// CR 608.2b: if the chosen target is no longer a creature spell at
    /// resolution time the entire effect does nothing — the counter does
    /// not fire and the caster does not draw. This mirrors the defensive
    /// posture of <see cref="AnnulFactory"/> and <see cref="NegateFactory"/>.
    ///
    /// CR 121.1: drawing is a simple top-of-library move. Empty library
    /// flags <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> and the
    /// SBA-driven loss (CR 704.5b).
    /// </summary>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    /// <param name="caster">Spell controller; draws the card on successful resolution.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack,
        Player caster)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Exclude — counter target creature spell; draw a card", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — if the target is no longer a creature
                        // spell at resolution, the entire effect does nothing
                        // (no counter, no draw).
                        if (!spell.Card.HasType(CardType.Creature))
                            return;

                        // CR 701.5 — counter the spell: remove from stack
                        // and move the card to graveyard.
                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);

                        // CR 121.1 — "Draw a card." Top-of-library move.
                        // Empty library flags the SBA-driven loss (CR 704.5b).
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null)
                        {
                            caster.MarkTriedToDrawFromEmptyLibrary();
                            return;
                        }
                        caster.Zones.Library.RemoveCard(top);
                        caster.Zones.Hand.AddCard(top);
                        top.SetZone(ZoneType.Hand);
                    }),
                };
            });
    }
}
