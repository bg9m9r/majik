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
/// Named-card factory for Saw It Coming (Kaldheim, {1}{U}{U}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Counter target spell.
///    Foretell {1}{U} (During your turn, you may pay {2} and exile this card
///    from your hand face down. Cast it on a later turn for its foretell cost.)"
///
/// The counter body is the archetypal hard counter — byte-for-byte the
/// <see cref="CounterspellFactory"/> shape (no type filter, any spell is a
/// legal target). At resolution the target spell is removed from the stack via
/// <see cref="OracleSpellBinder.RemoveFromStack"/> and its card moves to its
/// owner's graveyard (CR 701.5, CR 608.2b). Differs from
/// <see cref="NegateFactory"/> in that there is no noncreature restriction —
/// creature spells are countered too.
///
/// ## Foretell (CR 702.143) — not yet implemented
///
/// This engine does not yet model the Foretell alternative cost (CR 702.143);
/// see <see cref="DoomskarFactory"/> for the shared deferral rationale.
/// Foretell needs three cast-pipeline primitives that do not yet exist:
///   1. An activated-from-hand alt-cost binding: pay {2}, exile this card
///      face-down with a "foretold" marker, sorcery-speed only on your turn
///      (CR 702.143b).
///   2. A cast-from-exile pipeline that consumes the foretold marker and bills
///      the printed foretell cost rather than the printed mana cost on a
///      subsequent turn (CR 702.143c — sibling of Suspend's last-counter cast).
///   3. <c>MechanicPrimitiveRegistry</c> already covers the <c>foretell</c>
///      primitive (CR 702.143) for deferral-sweep discovery — the registry
///      entry is in place, just no factories wire it yet.
///
/// Until those land, this factory ships <b>without the foretell alt cost</b>:
/// callers can only cast Saw It Coming for its printed {1}{U}{U} mana cost. The
/// resolve body (counter target spell) is identical to what the foretold cast
/// would produce, so once Foretell is wired the only thing to add is the
/// alt-cost surface — the counter SpellDefinition stays put.
///
/// (defer: foretell alternative cost — CR 702.143. Today the factory only
/// exposes the printed mana-cost path; the foretold {1}{U} cast path is not yet
/// available because the cast pipeline lacks the foretell exile-face-down
/// primitive.)
///
/// CR rule references: 117.5 (mana cost), 608.2b (illegal-target check),
/// 701.5 (counter), 702.143 (foretell — not yet implemented).
/// </summary>
[CardName("Saw It Coming")]
public static class SawItComingFactory
{
    public const string CardName = "Saw It Coming";
    public const string Slug = "saw-it-coming";
    public const string PrintedManaCost = "{1}{U}{U}";

    /// <summary>Foretell cost (CR 702.143) — not yet implemented. Held as a
    /// constant for the future cast-pipeline binding.</summary>
    public const string ForetellPrintedCost = "{1}{U}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Materialise the Saw It Coming card shape from the embedded JSON
    /// definition (<c>saw-it-coming.json</c>) via
    /// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
    /// <see cref="CardDefinitionFactory.Build"/>. The counter
    /// <see cref="SpellDefinition"/> is built separately via
    /// <see cref="BuildSpellDefinition"/> (the JSON schema doesn't express the
    /// counter target-and-resolve shape) — same posture as
    /// <see cref="BorrowedTimeFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var built = CardDefinitionFactory.Build(Definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "counter target spell" SpellDefinition. Declares a single
    /// 1..1 "target spell" request (no type filter); on resolution removes the
    /// target from the stack and sends its card to the graveyard (CR 701.5).
    /// Mirrors <see cref="CounterspellFactory.BuildSpellDefinition"/>.
    /// </summary>
    /// <param name="targetResolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> targetResolver,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Saw It Coming — counter target spell", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
