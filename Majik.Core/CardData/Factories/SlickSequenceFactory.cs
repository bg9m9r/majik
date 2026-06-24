using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slick Sequence (Outlaws of Thunder Junction,
/// {U}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Slick Sequence deals 2 damage to any target. If you've cast another
///    spell this turn, draw a card."
///
/// ## Implementation
///
/// Same <see cref="Fx.DealDamageAny"/> "2 damage to any target" core as
/// <see cref="PlayWithFireFactory"/>, but the conditional rider is a card
/// draw gated on the spells-cast-this-turn tally instead of a scry gated on
/// the target type.
///
/// The "If you've cast another spell this turn" clause (CR 608.2 conditional)
/// is read LIVE at resolution from
/// <see cref="Game.TurnState.SpellsCastByPlayer(Player)"/> off the resolution
/// context (<c>ctx.Game.TurnState</c>) — the same per-player tally that
/// underpins Storm (CR 702.40) and Surge. By the time Slick Sequence resolves
/// its own cast has already been tallied into that count (TurnDriver's typed
/// <c>SpellCastEvent</c> handler runs at cast time, before resolution), so
/// "ANOTHER spell this turn" means the caster's tally is &gt;= 2 (this spell
/// plus at least one other). A tally of exactly 1 (only this spell) means no
/// other spell was cast, so the draw is skipped.
///
/// Card shape comes from the embedded JSON (<c>slick-sequence.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema).
///
/// On resolution (CR 608.2e — left-to-right clause ordering):
///   1. Deal 2 damage to the chosen target (creature, player, planeswalker,
///      battle) via <see cref="Fx.DealDamageAny(object, int)"/>
///      (CR 119 / CR 120.3).
///   2. CR 608.2 conditional — only if the caster has cast another spell this
///      turn (tally &gt;= 2), the caster draws a card (CR 120.2) via
///      <see cref="Fx.DrawCards(Player, int)"/>.
/// </summary>
[CardName("Slick Sequence")]
public static class SlickSequenceFactory
{
    public const string CardName = "Slick Sequence";
    public const string Slug = "slick-sequence";
    public const string PrintedManaCost = "{U}{R}";

    /// <summary>CR 119 — fixed 2 damage to any target.</summary>
    public const int Damage = 2;

    /// <summary>CR 120.2 — one card drawn when the rider is satisfied.</summary>
    private const int DrawCount = 1;

    /// <summary>
    /// CR 608.2 rider threshold. Slick Sequence's own cast is already tallied
    /// into <see cref="Game.TurnState.SpellsCastByPlayer"/> by resolution, so
    /// "another spell this turn" requires the caster's tally to be at least 2
    /// (this spell + at least one other).
    /// </summary>
    private const int AnotherSpellThreshold = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Slick Sequence is
    /// cast. Single 1..1 "any target" request, no X. On resolution:
    ///   1. Deals <see cref="Damage"/> (2) damage to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3).
    ///   2. If the caster has cast another spell this turn (CR 608.2 — tally
    ///      &gt;= <see cref="AnotherSpellThreshold"/>), the caster draws a card.
    /// </summary>
    /// <param name="caster">The player who cast Slick Sequence; draws the card
    /// when the rider is satisfied.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Slick Sequence: 2 damage to any target, draw a card if you've cast another spell this turn", ctx =>
                    {
                        // CR 120.3 / CR 608.2e step 1 — deal 2 damage.
                        Fx.DealDamageAny(target, Damage);

                        // CR 608.2 conditional — "If you've cast another spell
                        // this turn, draw a card." The tally read off the live
                        // resolution context already includes this spell, so
                        // >= 2 means at least one OTHER spell was cast. A null
                        // TurnState (legacy / context-free path) reads as 0 and
                        // skips the draw.
                        var spellsCast = ctx.Game?.TurnState?.SpellsCastByPlayer(caster) ?? 0;
                        if (spellsCast >= AnotherSpellThreshold)
                        {
                            // CR 120.2 — caster draws one card.
                            Fx.DrawCards(caster, DrawCount);
                        }

                        return ValueTask.CompletedTask;
                    }),
                };
            });
    }
}
