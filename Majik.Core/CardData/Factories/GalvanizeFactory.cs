using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Galvanize (Murders at Karlov Manor, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Galvanize deals 3 damage to target creature. If you've drawn two or more
///    cards this turn, Galvanize deals 5 damage to that creature instead."
///
/// ## Implementation
///
/// Same single-creature targeted-burn core as <see cref="MagmaSprayFactory"/>
/// (deal damage to one targeted creature via <see cref="Fx.DealDamage"/> —
/// CR 119.2 marks non-combat damage; SBA 704.5f handles lethal), but the fixed
/// amount is replaced by a CR 608.2 conditional that reads the caster's
/// cards-drawn-this-turn tally LIVE at resolution.
///
/// The "If you've drawn two or more cards this turn" clause is read off the
/// resolution context's <see cref="Game.TurnState.CardsDrawnByPlayer(Player)"/>
/// — the same per-player draw tally that underpins Slick Sequence's draw rider
/// (<see cref="SlickSequenceFactory"/>). Unlike Slick Sequence (whose own cast
/// inflates the spells-cast tally), Galvanize never draws cards itself, so its
/// own resolution does not perturb the draw tally: "drawn two or more cards
/// this turn" maps directly to a tally of <see cref="DrawnThreshold"/> (2) or
/// more. CR 608.2g — the spell uses the value as it exists when it resolves, so
/// the boost is decided at resolution, not on cast. A null TurnState
/// (legacy / context-free path) reads as 0 draws and yields the base 3 damage.
///
/// Card shape comes from the embedded JSON (<c>galvanize.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema).
/// </summary>
[CardName("Galvanize")]
public static class GalvanizeFactory
{
    public const string CardName = "Galvanize";
    public const string Slug = "galvanize";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>CR 119 — base damage to the targeted creature.</summary>
    public const int BaseDamage = 3;

    /// <summary>CR 119 — boosted damage when the rider is satisfied.</summary>
    public const int BoostedDamage = 5;

    /// <summary>
    /// CR 608.2 rider threshold. Galvanize never draws cards itself, so its own
    /// resolution does not change the caster's draw tally; "drawn two or more
    /// cards this turn" maps directly to a tally of 2+.
    /// </summary>
    public const int DrawnThreshold = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Galvanize is cast.
    /// Single 1..1 "target creature" request, no X. On resolution the chosen
    /// creature is dealt <see cref="BaseDamage"/> (3) — or
    /// <see cref="BoostedDamage"/> (5) when the caster has drawn two or more
    /// cards this turn (CR 608.2 — tally read live off
    /// <c>ctx.Game.TurnState</c>).
    /// </summary>
    /// <param name="caster">The player who cast Galvanize; their
    /// cards-drawn-this-turn tally decides the boosted mode.</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(Player caster, Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal,
                    // Agent-prompt: every creature on the battlefield across all
                    // players (CR 302).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"Galvanize: {BaseDamage} damage to target creature ({BoostedDamage} if you've drawn two or more cards this turn).",
                        ctx =>
                        {
                            // CR 608.2b — only a creature is a legal target;
                            // anything else (left the battlefield / changed
                            // type) is a no-op.
                            if (target is not Creature creature) return ValueTask.CompletedTask;

                            // CR 608.2 / CR 608.2g — read the caster's draw tally
                            // live at resolution. Galvanize itself never draws,
                            // so a tally >= 2 means "you've drawn two or more
                            // cards this turn" → boosted 5 damage. A null
                            // TurnState reads as 0 → base 3 damage.
                            var drawn = ctx.Game?.TurnState?.CardsDrawnByPlayer(caster) ?? 0;
                            var amount = drawn >= DrawnThreshold ? BoostedDamage : BaseDamage;

                            // CR 119.2 — non-combat damage is marked on the
                            // creature; SBA 704.5f moves a lethally-damaged
                            // creature to its graveyard on the next pass.
                            Fx.DealDamage(creature, amount);

                            return ValueTask.CompletedTask;
                        }),
                };
            });
    }
}
