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
/// Named-card factory for Tear Asunder (Commander Legends, {1}{G}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Kicker {1}{B} (You may pay an additional {1}{B} as you cast this spell.)
///    Exile target artifact or enchantment. If this spell was kicked, exile
///    target nonland permanent instead."
///
/// ## Implementation
///
/// - <b>Instant</b> shape, mana cost {1}{G}, green. The card shape (name, type,
///   cost) is data-driven: loaded from
///   <c>Majik.Core/CardData/Cards/tear-asunder.json</c> via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built by
///   <see cref="CardDefinitionFactory"/> (mirrors <see cref="BloodchiefsThirstFactory"/>).
///   The kicker-conditional exile body is supplied in code via
///   <see cref="BuildSpellDefinition"/> because the JSON ability schema does
///   not yet model a targeted exile spell or kicker.
///
/// - <b>Kicker {1}{B}</b> (CR 702.33) is a real <see cref="IAdditionalCost"/>
///   primitive — <see cref="KickerAdditionalCost"/>. <see cref="BuildAdditionalCost"/>
///   constructs the kicker rider for a specific card instance; production casts
///   layer it onto <see cref="Majik.Core.Game.SpellCastFlow"/>'s
///   <c>additionalCosts</c> parameter. The kicked-vs-unkicked branch is selected
///   when <see cref="BuildSpellDefinition"/> is constructed at cast time from
///   <see cref="Card.WasKicked"/> — set by <see cref="KickerAdditionalCost.Pay"/>
///   during the cast flow's additional-cost loop and cleared by the post-resolve
///   cleanup effect (mirrors <see cref="BloodchiefsThirstFactory"/>). CR 702.33b
///   — "if [spell] was kicked" is the locked-in cast-time decision (CR 601.2b).
///
/// - <b>Unkicked: exile target artifact or enchantment</b> — single 1..1 target
///   request, exactly the type filter of <see cref="DisenchantFactory"/>. The
///   <c>CandidateGatherer</c> walks every player's battlefield for cards with
///   <see cref="CardType.Artifact"/> or <see cref="CardType.Enchantment"/>
///   (CR 700.4 — a permanent may have multiple card types).
///
/// - <b>Kicked: exile target nonland permanent instead</b> — the target request
///   widens to any nonland permanent (mirrors <see cref="PrismaticEndingFactory"/>'s
///   nonland-permanent filter). CR 702.33b — the "instead" rewrite replaces the
///   printed target restriction wholesale.
///
/// - On resolution: re-checks the target is still a <see cref="Permanent"/> on
///   the Battlefield (CR 608.2b illegal-target gate) and matches the
///   kicked/unkicked type constraint; then exiles (CR 701.21) by routing through
///   the owner's zones (mirrors <see cref="PrismaticEndingFactory"/> /
///   <see cref="PathToExileFactory"/>) so multi-player owner-of-zone bookkeeping
///   stays consistent. Exile is not "destroy", so indestructible (CR 702.12) and
///   regeneration (CR 701.15) do not protect the target.
/// </summary>
[CardName("Tear Asunder")]
public static class TearAsunderFactory
{
    public const string CardName = "Tear Asunder";
    public const string Slug = "tear-asunder";
    public const string PrintedManaCost = "{1}{G}";
    public const string KickerCostText = "{1}{B}";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>Card shape only (Instant, {1}{G}). The kicker-conditional exile
    /// body is supplied at cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static Instant Create(Player owner) =>
        (Instant)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Tear Asunder is cast.
    ///
    /// Unkicked: single 1..1 "target artifact or enchantment" request; on
    /// resolution exiles the target iff it is still on the battlefield and is an
    /// artifact or enchantment (CR 608.2b → no-op otherwise).
    ///
    /// Kicked (<paramref name="wasKicked"/> = true): the request widens to
    /// "target nonland permanent" and resolution exiles any nonland permanent
    /// still on the battlefield (CR 702.33b "instead").
    /// </summary>
    /// <param name="wasKicked">The cast-time kicker decision (CR 702.33b).
    /// Production reads <see cref="Card.WasKicked"/> off the cast card instance
    /// when constructing the definition; <c>true</c> swaps the target filter to
    /// "nonland permanent".</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        bool wasKicked,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        var description = wasKicked
            ? "target nonland permanent"
            : "target artifact or enchantment";

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: description,
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gather candidates from every battlefield. Removal
                    // intent pushes opponent permanents up in bot ranking.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => wasKicked
                            ? !c.HasType(CardType.Land)
                            : c.HasType(CardType.Artifact)
                                || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                var label = wasKicked
                    ? $"{CardName}: (kicked) exile target nonland permanent"
                    : $"{CardName}: exile target artifact or enchantment";
                return new IEffect[]
                {
                    new Effect(label, () =>
                    {
                        // CR 608.2b — resolution-time legality re-check.
                        if (resolved is not Permanent target) return;
                        if (target.Zone != ZoneType.Battlefield) return;

                        // Kicked: any nonland permanent. Unkicked: artifact or
                        // enchantment only (CR 608.2b — wrong type → no-op).
                        if (wasKicked)
                        {
                            if (target.HasType(CardType.Land)) return;
                        }
                        else if (!target.HasType(CardType.Artifact)
                            && !target.HasType(CardType.Enchantment))
                        {
                            return;
                        }

                        // Exile (CR 701.21). Routed through the owning player's
                        // zones so owner-of-zone bookkeeping stays consistent
                        // across multi-player games (mirrors PrismaticEnding /
                        // PathToExile). Exile is not "destroy", so indestructible
                        // (CR 702.12) / regeneration (CR 701.15) don't apply.
                        var fromOwner = target.Owner;
                        if (fromOwner != null)
                        {
                            fromOwner.Zones.Battlefield.RemoveCard(target);
                            fromOwner.Zones.Exile.AddCard(target);
                        }
                        target.SetZone(ZoneType.Exile);
                    }),
                };
            });
    }

    /// <summary>
    /// Construct Tear Asunder's kicker <see cref="IAdditionalCost"/> for the
    /// supplied <paramref name="card"/> instance. Convenience builder for callers
    /// (tests, bot decision layer) that have decided to pay the kicker; layer the
    /// returned cost onto the cast via
    /// <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>'s
    /// <c>additionalCosts</c> parameter (CR 702.33). Mirrors
    /// <see cref="BloodchiefsThirstFactory.BuildAdditionalCost"/>.
    /// </summary>
    public static IAdditionalCost BuildAdditionalCost(ICard card)
    {
        ArgumentNullException.ThrowIfNull(card);
        return new KickerAdditionalCost(card, ManaCost.Parse(KickerCostText));
    }
}
