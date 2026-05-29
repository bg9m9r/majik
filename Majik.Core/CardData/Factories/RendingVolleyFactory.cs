using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Rending Volley (Dragons of Tarkir, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "This spell can't be countered.
///    Rending Volley deals 4 damage to target white or blue creature."
///
/// ## Implemented (v1)
/// - Instant {R} (Red) card shape — loaded from the embedded JSON definition
///   (<c>rending-volley.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/> (same data-driven shape path as
///   <see cref="PlayWithFireFactory"/>).
/// - <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker
///   "Can't Be Countered" is attached to the built card (same structural
///   posture as <see cref="AbruptDecayFactory"/>; the JSON schema cannot
///   carry keywords, so the marker is stamped in <see cref="Create"/>).
///   CR 701.5b — an uncounterable spell can't be countered. The marker is
///   structural / observable; enforcement at the StackResolver / SpellCaster
///   layer is deferred (same posture as Abrupt Decay / Veil of Summer).
/// - <b>4 damage to target white or blue creature</b> —
///   <see cref="BuildSpellDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target white or blue creature"
///   <see cref="TargetRequest"/>. On resolution (CR 608.2b — resolution-time
///   legality check):
///   <list type="number">
///     <item>The resolved target is a <see cref="Creature"/>.</item>
///     <item>It is still on the battlefield.</item>
///     <item>It is white or blue (<see cref="CardColors.GetColors(ICard)"/>
///       reads the printed mana cost per CR 105).</item>
///     <item>If all pass: deal <see cref="Damage"/> (4) via
///       <see cref="Fx.DealDamageAny(object, int)"/> (CR 119 / CR 120.3).</item>
///     <item>If any fails: no-op (illegal target → effect does nothing).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Can't-be-countered enforcement</b>: the keyword marker is attached but
///   counter effects do not yet consult it at the StackResolver / SpellCaster
///   layer (same deferral as <see cref="AbruptDecayFactory"/>).
/// - <b>Target colour filtering</b>: v1 <c>ActionValidator</c> does not pre-
///   filter the agent's target list by colour; the resolve-time guard catches
///   illegal picks and no-ops the effect (same pattern as
///   <see cref="AetherGustFactory"/>).
/// </summary>
[CardName("Rending Volley")]
public static class RendingVolleyFactory
{
    public const string CardName = "Rending Volley";
    public const string Slug = "rending-volley";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 4 damage to the target creature.</summary>
    public const int Damage = 4;

    /// <summary>
    /// Keyword name used for the "this spell can't be countered" marker
    /// (CR 701.5b). Attached to the card shape as a
    /// <see cref="KeywordAbility"/> for structural observability — same
    /// pattern / deferral as <see cref="AbruptDecayFactory.CantBeCounteredMarker"/>.
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
        // enforcement deferred (see xmldoc / AbruptDecayFactory).
        card.AddAbility(new KeywordAbility(CantBeCounteredMarker, source: card, controller: owner));

        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Rending Volley is
    /// cast. Single 1..1 "target white or blue creature" request, no X. On
    /// resolution deals <see cref="Damage"/> (4) to the chosen creature iff it
    /// is still on the battlefield and is white or blue (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target white or blue creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: 4 damage to target white or blue creature", () =>
                    {
                        // CR 608.2b — resolution-time legality check.
                        if (raw is not Creature target) return;
                        if (target.Zone != ZoneType.Battlefield) return;

                        // CR 105 — colour from the printed mana cost. The
                        // target predicate is "white or blue".
                        var colors = CardColors.GetColors(target);
                        if (!colors.Contains(ManaColor.White) && !colors.Contains(ManaColor.Blue))
                        {
                            // Illegal target at resolution → effect does nothing.
                            return;
                        }

                        // CR 119 / CR 120.3 — deal 4 damage to the creature.
                        Fx.DealDamageAny(target, Damage);
                    }),
                };
            });
    }
}
