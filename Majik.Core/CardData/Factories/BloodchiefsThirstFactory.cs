using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodchief's Thirst (Zendikar Rising, {B}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Kicker {2}{B} (You may pay an additional {2}{B} as you cast this spell.)
///    Destroy target creature or planeswalker with mana value 2 or less. If
///    this spell was kicked, instead destroy target creature or planeswalker."
///
/// ## Implementation
///
/// - <b>Sorcery</b> shape, mana cost {B}, black. The card shape (name, type,
///   cost) is data-driven: loaded from
///   <c>Majik.Core/CardData/Cards/bloodchiefs-thirst.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built by
///   <see cref="CardDefinitionFactory"/> (mirrors <see cref="RoastFactory"/>).
///   The kicker-conditional destroy body is supplied in code via
///   <see cref="BuildSpellDefinition"/> because the JSON ability schema does
///   not yet model a targeted destroy spell or kicker.
///
/// - <b>Kicker {2}{B}</b> (CR 702.33) is a real <see cref="IAdditionalCost"/>
///   primitive — <see cref="KickerAdditionalCost"/>. <see cref="BuildAdditionalCost"/>
///   constructs the kicker rider for a specific card instance; production
///   casts layer it onto <see cref="SpellCastFlow.CastAsync"/>'s
///   <c>additionalCosts</c> parameter. The kicked-vs-unkicked branch is
///   selected when <see cref="BuildSpellDefinition"/> is constructed at cast
///   time from <see cref="Card.WasKicked"/> — set by
///   <see cref="KickerAdditionalCost.Pay"/> during the cast flow's
///   additional-cost loop and cleared by the post-resolve cleanup effect
///   (mirrors <see cref="BurstLightningFactory"/>). CR 702.33b — "if [spell]
///   was kicked" is the locked-in cast-time decision.
///
/// - <b>Destroy target creature or planeswalker</b> — single 1..1 target
///   request, exactly the shape of <see cref="HerosDownfallFactory"/>. The
///   <c>CandidateGatherer</c> walks every player's battlefield for cards with
///   <see cref="CardType.Creature"/> or <see cref="CardType.Planeswalker"/>
///   (CR 700.4 — a permanent may have multiple card types).
///
/// - <b>Unkicked mana-value gate (≤ 2)</b> — when not kicked, the resolution
///   destroys the target only if its mana value is 2 or less (Rule 202.3 —
///   mana value is computed from the printed mana cost; matches
///   <see cref="FatalPushFactory"/>'s use of <c>ManaCostValue.TotalValue</c>).
///   The mana-value clause is part of the printed targeting wording, but the
///   unkicked spell legally targets any creature/planeswalker and simply does
///   nothing on resolution if the mana value exceeds 2 (CR 608.2b posture).
///   When kicked, the gate is removed entirely ("instead destroy target
///   creature or planeswalker").
///
/// - On resolution: re-checks the target is still a Creature or Planeswalker
///   on the Battlefield (CR 608.2b illegal-target gate), applies the unkicked
///   mana-value gate, then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) and regeneration shields (CR 701.15) are honoured.
///
/// ## v1 posture
/// - Mana-value normalization via a CDA-aware <c>ManaValue</c> helper (split /
///   adventure / MDFC back face) is deferred — matches the rest of the
///   destroy-mana-value-limit surface (<see cref="FatalPushFactory"/>).
/// </summary>
[CardName("Bloodchief's Thirst")]
public static class BloodchiefsThirstFactory
{
    public const string CardName = "Bloodchief's Thirst";
    public const string Slug = "bloodchiefs-thirst";
    public const string PrintedManaCost = "{B}";
    public const string KickerCostText = "{2}{B}";

    /// <summary>Unkicked "destroy if mana value ≤ N" threshold (Rule 202.3).</summary>
    public const int UnkickedManaValueLimit = 2;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Card shape only (Sorcery, {B}). The kicker-conditional destroy
    /// body is supplied at cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bloodchief's Thirst is
    /// cast. Single 1..1 "target creature or planeswalker" request. On resolve:
    /// validates the target is still a Creature or Planeswalker on the
    /// Battlefield (CR 608.2b); when unkicked, additionally requires the
    /// target's mana value be ≤ 2 (Rule 202.3); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7).
    /// </summary>
    /// <param name="wasKicked">The cast-time kicker decision (CR 702.33b).
    /// Production reads <see cref="Card.WasKicked"/> off the cast card instance
    /// when constructing the definition; <c>true</c> removes the mana-value
    /// gate ("instead destroy target creature or planeswalker").</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        bool wasKicked,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gather every creature / planeswalker on any
                    // battlefield. Removal intent pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                var label = wasKicked
                    ? $"{CardName}: (kicked) destroy target creature or planeswalker"
                    : $"{CardName}: destroy target creature or planeswalker with mana value {UnkickedManaValueLimit} or less";
                return new IEffect[]
                {
                    new Effect(label, () =>
                    {
                        // CR 608.2b — resolution-time legality re-check.
                        if (resolved is not Permanent target) return;
                        if (target.Zone != ZoneType.Battlefield) return;
                        if (!target.HasType(CardType.Creature)
                            && !target.HasType(CardType.Planeswalker)) return;

                        // Unkicked: destroy only if mana value ≤ 2 (Rule 202.3 —
                        // mana value from the printed mana cost). Kicked removes
                        // the restriction (CR 702.33b "instead destroy").
                        if (!wasKicked
                            && target.ManaCostValue.TotalValue > UnkickedManaValueLimit)
                        {
                            return;
                        }

                        // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                        // regeneration (CR 701.15) handled via the Destroy-reason
                        // gate in MoveToGraveyard.
                        OracleSpellBinder.MoveToGraveyard(
                            target,
                            ZoneMoveReason.Destroy);
                    }),
                };
            });
    }

    /// <summary>
    /// Construct Bloodchief's Thirst's kicker <see cref="IAdditionalCost"/> for
    /// the supplied <paramref name="card"/> instance. Convenience builder for
    /// callers (tests, bot decision layer) that have decided to pay the kicker;
    /// layer the returned cost onto the cast via
    /// <see cref="SpellCastFlow.CastAsync"/>'s <c>additionalCosts</c>
    /// parameter (CR 702.33). Mirrors <see cref="BurstLightningFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
