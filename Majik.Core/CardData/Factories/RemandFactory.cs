using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Remand (Ravnica: City of Guilds, {1}{U}).
///
/// Instant. Oracle text:
///   "Counter target spell. If that spell is countered this way, put it into
///    its owner's hand instead of into that player's graveyard.
///    Draw a card."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, blue.
/// - <b>Counter target spell + return to owner's hand</b> — <see cref="BuildDefinition"/>
///   counters the target via <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5)
///   and then routes the card to the owner's hand rather than the graveyard (raw zone
///   mutation via <c>Zones.Library.RemoveCard</c> / <c>Zones.Hand.AddCard</c> so no
///   ETB events fire — CR 608.2b applies: if the target is no longer on the stack at
///   resolution the entire effect is skipped, including the draw).
/// - <b>Draw a card</b> — caster draws one card from their library.
///
/// ## Deferred
/// - Real player prompt for the "put into owner's hand" choice (the oracle text implies
///   it is mandatory — always goes to hand, never to graveyard, when countered by Remand).
/// - "Countered this way" tracking for interactions where the spell was already gone.
/// </summary>
[CardName("Remand")]
public static class RemandFactory
{
    public const string CardName = "Remand";
    public const string PrintedManaCost = "{1}{U}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (counter + return-to-hand + draw rider) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Targets a single
    /// spell; on resolution counters it (CR 701.5), redirects the card to the
    /// owner's hand (instead of the graveyard), and causes the caster to draw
    /// a card. If the target is no longer on the stack (CR 608.2b) the entire
    /// effect — including the draw — is skipped.
    /// </summary>
    /// <param name="caster">Controller of Remand — draws the card on resolution.</param>
    /// <param name="targetResolver">Resolves the raw target token to a live engine object.</param>
    /// <param name="stack">Active stack; required to remove the countered spell.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[] { new TargetRequest("target spell", 1, 1, Array.Empty<object>()) },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Remand — counter target spell, return to owner's hand, draw a card", () =>
                    {
                        // CR 608.2b — if the target is no longer on the stack,
                        // the entire effect (including the draw) is skipped.
                        if (stack == null || resolved is not ISpell spell) return;
                        if (!stack.GetAll().Contains(spell)) return;

                        // CR 701.5 — counter the spell (removes it from the stack).
                        OracleSpellBinder.RemoveFromStack(stack, spell);

                        // Instead of going to the graveyard, the card goes to its
                        // owner's hand. Use raw zone mutation to avoid firing ETB
                        // events for the hand zone.
                        var owner = spell.Card.Owner ?? spell.Controller;
                        if (owner != null)
                        {
                            // Remove from wherever the card currently thinks it is
                            // (the RemoveFromStack implementation may leave its zone
                            // marker in limbo; normalise here).
                            owner.Zones.Graveyard.RemoveCard(spell.Card);
                            owner.Zones.Hand.AddCard(spell.Card);
                            spell.Card.SetZone(ZoneType.Hand);
                        }
                        else
                        {
                            // Fallback: no owner resolved — just set zone flag.
                            spell.Card.SetZone(ZoneType.Hand);
                        }

                        // Draw a card for the Remand caster.
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top != null)
                        {
                            caster.Zones.Library.RemoveCard(top);
                            caster.Zones.Hand.AddCard(top);
                            top.SetZone(ZoneType.Hand);
                        }
                        else
                        {
                            // CR 704.5b — tried to draw from empty library.
                            caster.MarkTriedToDrawFromEmptyLibrary();
                        }
                    }),
                };
            });
    }
}
