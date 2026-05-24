using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Abrupt Decay (Return to Ravnica, {B}{G}).
///
/// Instant. Oracle text:
///   "This spell can't be countered.
///    Destroy target nonland permanent with mana value 3 or less."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}{G}, owner / controller.
/// - <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker
///   "Can't Be Countered" is attached to the card shape (structural;
///   actual enforcement via SpellCaster / StackResolver is deferred —
///   same posture as Veil of Summer's turn-scoped uncounterable rider
///   and Force of Will's text interaction). See
///   <see cref="CantBeCounteredMarker"/>.
/// - <b>Destroy target nonland permanent with mana value 3 or less</b>
///   — <see cref="BuildSpellDefinition"/> builds a
///   <see cref="SpellDefinition"/> with a single 1..1
///   "target nonland permanent with mana value 3 or less"
///   <see cref="TargetRequest"/>. On resolution:
///   <list type="number">
///     <item>Target is still on the battlefield (CR 608.2b).</item>
///     <item>Target is not a land (nonland predicate).</item>
///     <item>Target's mana value is ≤ 3 at resolution (CR 202.3).</item>
///     <item>If all three pass: destroy via
///       <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7).</item>
///     <item>If any fails: no-op.</item>
///   </list>
///
/// Indestructible (CR 702.12) and regeneration (CR 701.15) are handled
/// at the destroy site via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Can't-be-countered enforcement</b>: the keyword marker is attached
///   but counter effects (Force of Negation, Mana Leak, etc.) do not yet
///   consult it at the StackResolver / SpellCaster layer. See
///   <c>CastingRestrictions.SpellsCannotBeCountered</c> as the existing
///   precedent for the wiring pattern.
/// </summary>
[CardName("Abrupt Decay")]
public static class AbruptDecayFactory
{
    public const string CardName = "Abrupt Decay";
    public const string PrintedManaCost = "{B}{G}";

    /// <summary>
    /// Keyword name used for the "this spell can't be countered" marker.
    /// Attached to the card shape as a <see cref="KeywordAbility"/>
    /// for structural observability (same pattern as Flash / Deathtouch /
    /// other keyword markers). Enforcement is deferred — see xmldoc.
    /// </summary>
    public const string CantBeCounteredMarker = "Can't Be Countered";

    /// <summary>CardDef DSL — card shape + "Can't Be Countered" marker.
    /// Resolve behaviour is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword(CantBeCounteredMarker);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Abrupt Decay is
    /// cast. Single 1..1 "target nonland permanent with mana value 3 or
    /// less" request; on resolution the targeted permanent is destroyed
    /// (CR 701.7) iff it is still on the battlefield, is not a land, and
    /// its mana value is ≤ 3 at resolution (CR 608.2b / CR 202.3).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a
    /// live engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target nonland permanent with mana value 3 or less",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target nonland permanent with mv ≤ 3",
                        () =>
                        {
                            if (raw is not Permanent target) return;

                            // CR 608.2b — resolution-time legality check.
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (target.HasType(CardType.Land)) return;

                            // CR 202.3 — mana value is checked at resolution.
                            if (target.ManaCostValue.TotalValue > 3) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via
                            // MoveToGraveyard's Destroy-reason gate; note
                            // Abrupt Decay reads "can't be countered", not
                            // "can't be regenerated", so the regen shield
                            // is honoured normally.
                            OracleSpellBinder.MoveToGraveyard(target, Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
