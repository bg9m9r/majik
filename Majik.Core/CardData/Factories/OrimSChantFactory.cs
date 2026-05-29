using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Primitives;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Rules;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Orim's Chant (Planeshift, {W}).
///
/// Instant. Oracle text:
///   "Kicker {W} (You may pay an additional {W} as you cast this spell.)
///    Target player can't cast spells this turn.
///    If this spell was kicked, creatures can't attack this turn."
///
/// ## Implemented (v1)
/// - Instant {W} (white) card shape, mana value 1, owner / controller wired.
/// - <b>Kicker {W} (CR 702.33)</b> — real <see cref="KickerAdditionalCost"/>
///   primitive, identical to the <see cref="BurstLightningFactory"/> pattern.
///   <see cref="Card.WasKicked"/> is stamped at cast time and read at
///   resolution (CR 702.33b).
/// - <b>"Target player can't cast spells this turn" (CR 601.3)</b> — at
///   resolution, <see cref="CastingRestrictions.AddCannotCastAnySpell"/> is
///   called with a unique sentinel token (the card's object reference) and
///   the chosen player. The caller or end-of-turn machinery clears the entry
///   via <see cref="CastingRestrictions.RemoveCannotCastAnySpell(object)"/>
///   (or the test fixture calls <see cref="CastingRestrictions.Clear()"/>).
///   Queried by <see cref="Majik.Core.Rules.ActionValidator.ValidateCastSpell"/>
///   — same rail as Voice of Victory / Grand Abolisher's opponent lock.
/// - <b>"Creatures can't attack this turn" (CR 508.1c, kicked only)</b> —
///   mass <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotAttack"/>, no predicate, no target,
///   <c>expiresAtEndOfTurn = true</c>. Registered on the supplied
///   <see cref="ContinuousEffectsService"/> when the spell resolves kicked;
///   skipped when not kicked or when no service is supplied.
///
/// ## Analogues
/// - Kicker pattern: <see cref="BurstLightningFactory"/>.
/// - Cast restriction: <see cref="VoiceOfVictoryFactory"/> /
///   <see cref="RangerCaptainOfEosFactory"/>.
/// - Mass can't-attack: <see cref="SunderingEruptionFactory"/>'s
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> expanded to CannotAttack.
/// </summary>
[CardName("Orim's Chant")]
public static class OrimSChantFactory
{
    public const string CardName = "Orim's Chant";
    public const string PrintedManaCost = "{W}";
    public const string KickerCostText = "{W}";

    /// <summary>CardDef DSL — card shape only.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="IAdditionalCost"/> for Orim's Chant's kicker {W}.
    /// Mirrors <see cref="BurstLightningFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for Orim's Chant.
    ///
    /// <para>CR 608.2b — the target player restriction is applied only when
    /// the chosen target is a valid <see cref="Player"/> at resolution time.
    /// </para>
    /// <para>CR 702.33b — the kicked "creatures can't attack" effect reads
    /// <see cref="Card.WasKicked"/> off <paramref name="card"/> at resolution
    /// time; the flag was stamped by <see cref="KickerAdditionalCost.Pay"/>
    /// during the cast and will be cleared by <see cref="SpellCastFlow"/>'s
    /// post-resolve cleanup effect.</para>
    /// </summary>
    /// <param name="card">The cast card instance — read for
    /// <see cref="Card.WasKicked"/> at resolution (CR 702.33b).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="continuousEffects">Optional <see cref="ContinuousEffectsService"/>
    /// on which the mass "creatures can't attack this turn"
    /// <see cref="CombatRestrictionEffect"/> will be registered when kicked.
    /// When null the combat restriction is skipped (shape-only tests).</param>
    public static SpellDefinition BuildDefinition(
        ICard card,
        Func<object, object> targetResolver,
        ContinuousEffectsService? continuousEffects = null)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target player", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: restrict target player + kicked combat block", () =>
                        Resolve(card, resolved, continuousEffects)),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body
    // -------------------------------------------------------------------------

    private static void Resolve(
        ICard card,
        object resolved,
        ContinuousEffectsService? continuousEffects)
    {
        // CR 608.2b — guard: resolved target must be a Player.
        if (resolved is not Player targetPlayer) return;

        // "Target player can't cast spells this turn." (CR 601.3)
        // Use the card's object reference as the unique token so the caller
        // can remove the restriction via RemoveCannotCastAnySpell(card).
        CastingRestrictions.AddCannotCastAnySpell(card, targetPlayer);

        // "If this spell was kicked, creatures can't attack this turn."
        // (CR 702.33b / CR 508.1c)
        bool wasKicked = card is Card concrete && concrete.WasKicked;
        if (wasKicked && continuousEffects != null)
        {
            // Mass CannotAttack: target = null → applies to every creature.
            continuousEffects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotAttack,
                target: null,
                expiresAtEndOfTurn: true));
        }
    }
}
