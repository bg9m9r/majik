using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heated Debate (Outlaws of Thunder Junction, {2}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "This spell can't be countered. (This includes by the ward ability.)
///    Heated Debate deals 4 damage to target creature or planeswalker."
///
/// ## Implemented (v1)
/// - Instant {2}{R} (Red) card shape — loaded from the embedded JSON definition
///   (<c>heated-debate.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same data-driven shape path as
///   <see cref="RendingVolleyFactory"/>).
/// - <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker
///   "Can't Be Countered" is attached to the built card (same structural
///   posture as <see cref="RendingVolleyFactory"/> / <see cref="AbruptDecayFactory"/>;
///   the JSON schema cannot carry keywords, so the marker is stamped in
///   <see cref="Create"/>). CR 701.5b — an uncounterable spell can't be
///   countered; the parenthetical "(This includes by the ward ability.)" is
///   reminder text (CR 207.2) and adds no separate behaviour. The marker is
///   structural / observable; enforcement at the StackResolver / SpellCaster
///   layer is deferred (same posture as Rending Volley / Abrupt Decay).
/// - <b>4 damage to target creature or planeswalker</b> —
///   <see cref="BuildSpellDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or planeswalker"
///   <see cref="TargetRequest"/> (same target shape as
///   <see cref="ScorchingDragonfireFactory"/>). On resolution (CR 608.2b —
///   resolution-time legality check):
///   <list type="number">
///     <item>The resolved target is still a <see cref="Creature"/> or
///       <see cref="Planeswalker"/>.</item>
///     <item>If so: deal <see cref="Damage"/> (4) via
///       <see cref="Fx.DealDamageAny(object, int)"/> — a planeswalker target
///       loses that much loyalty (CR 119 / CR 306.7).</item>
///     <item>Otherwise (target left the battlefield / changed type): no-op.</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Can't-be-countered enforcement</b>: the keyword marker is attached but
///   counter effects do not yet consult it at the StackResolver / SpellCaster
///   layer (same deferral as <see cref="RendingVolleyFactory"/>).
/// </summary>
[CardName("Heated Debate")]
public static class HeatedDebateFactory
{
    public const string CardName = "Heated Debate";
    public const string Slug = "heated-debate";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>CR 119 — fixed 4 damage to the target creature or planeswalker.</summary>
    public const int Damage = 4;

    /// <summary>
    /// Keyword name used for the "this spell can't be countered" marker
    /// (CR 701.5b). Attached to the card shape as a <see cref="KeywordAbility"/>
    /// for structural observability — same pattern / deferral as
    /// <see cref="RendingVolleyFactory.CantBeCounteredMarker"/>.
    /// </summary>
    public const string CantBeCounteredMarker = "Can't Be Countered";

    /// <summary>
    /// Build the card shape from the embedded JSON definition, then stamp the
    /// "Can't Be Countered" keyword marker (the JSON schema cannot carry
    /// keywords). Resolve behaviour is built via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 701.5b — "This spell can't be countered." Structural marker;
        // enforcement deferred (see xmldoc / RendingVolleyFactory).
        card.AddAbility(new KeywordAbility(CantBeCounteredMarker, source: card, controller: owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Heated Debate is cast.
    /// Single 1..1 "target creature or planeswalker" request, no X. On
    /// resolution deals <see cref="Damage"/> (4) to the chosen permanent iff it
    /// is still a creature or planeswalker (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature or planeswalker",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal,
                    // Agent-prompt: every creature + planeswalker on the
                    // battlefield across all players (CR 302 / CR 306).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                                 || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {Damage} damage to target creature or planeswalker.",
                        () =>
                        {
                            // CR 608.2b — only creatures and planeswalkers are
                            // legal targets; anything else (target left the
                            // battlefield / changed type) is a no-op.
                            if (target is not (Creature or Planeswalker)) return;

                            // CR 119 / CR 306.7 — deal 4 damage; a planeswalker
                            // target loses that much loyalty via DealDamageAny.
                            Fx.DealDamageAny(target, Damage);
                        }),
                };
            });
    }
}
