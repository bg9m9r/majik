using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fear of Isolation (Duskmourn, {1}{U}).
///
/// Enchantment Creature — Nightmare 2/3. Oracle text (verified against
/// Scryfall 2026-06-24):
///   "As an additional cost to cast this spell, return a permanent you
///    control to its owner's hand.
///    Flying"
///
/// The base shape (name, Creature + Enchantment types, Nightmare subtype,
/// {1}{U}, 2/3, blue) is materialised from the embedded JSON definition
/// (<c>fear-of-isolation.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="FearOfTheDarkFactory"/>). Flying is layered on as a
/// <see cref="KeywordAbility"/> marker; the additional-cost self-bounce is
/// built on demand via <see cref="BuildAdditionalCostPayment"/> (the JSON
/// <c>AbilityDefinition</c> schema doesn't express a cast-time additional cost).
///
/// ## Implemented (v1)
/// - <b>{1}{U} Enchantment Creature — Nightmare 2/3, blue</b> (CR 301.1 /
///   302.1 — dual Creature + Enchantment type), from the JSON def.
/// - <b>Flying</b> (CR 702.9) — a <see cref="KeywordAbility"/> marker, same
///   shape as <see cref="KorSkyfisherFactory"/> / Air Elemental, so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> surfaces the
///   evasion property.
/// - <b>Additional cost — "return a permanent you control to its owner's
///   hand"</b> (CR 601.2f / 601.2g): built via
///   <see cref="BuildAdditionalCostPayment"/>. The caster picks a permanent
///   they control (this spell isn't yet on the battlefield, so it can't be the
///   returned permanent — CR 601.2g requires an already-existing permanent) and
///   returns it to its owner's hand. The pick uses the same agent-or-fallback
///   policy as Kor Skyfisher's bounce (CR 109.5 — "a permanent you control"):
///   <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> with
///   <see cref="BotIntent.Bounce"/>; null agent / null pick falls back to the
///   first eligible permanent.
///
/// ## Deviation from printed text (documented)
///
/// Printed text says "As an additional cost to cast this spell, return a
/// permanent you control to its owner's hand" (CR 601.2f) — the return is
/// announced + paid as the spell is put on the stack, and the cast is illegal
/// if the caster controls no permanent to return. v1 models the return as a
/// resolve-time payment (<see cref="BuildAdditionalCostPayment"/> run from the
/// resolve flow), mirroring the documented deviation in
/// <see cref="ThrillOfPossibilityFactory"/> /
/// <see cref="CatharticReunionFactory"/>:
///
/// 1. <b>Counter interactions</b>: if Fear of Isolation is countered, no
///    permanent was returned in v1 (printed: the bounce already happened at
///    announcement, so the countered spell still cost a permanent). v1 treats
///    countering as a full no-op.
/// 2. <b>Legality gate</b>: printed text forbids casting with no permanent to
///    return; v1's resolve-side payment is a no-op when the caster controls no
///    eligible permanent (the spell still resolves into a 2/3 flyer).
///
/// A future PR can promote the return to a real
/// <see cref="Majik.Core.Costs.IAdditionalCost"/> once the engine grows a
/// "choose a permanent you control to return" cast-time prompt — same queue as
/// Thrill of Possibility's deferred additional-cost shape.
///
/// ## Deferred (v1 gaps)
/// - Real additional-cost shape + cast-time legality gate (see above).
/// </summary>
[CardName("Fear of Isolation")]
public static class FearOfIsolationFactory
{
    public const string CardName = "Fear of Isolation";
    public const string Slug = "fear-of-isolation";
    public const string GrantedFlying = "Flying";

    /// <summary>
    /// Construct Fear of Isolation. Flying is attached to the card shape; the
    /// additional-cost self-bounce is built separately via
    /// <see cref="BuildAdditionalCostPayment"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment, Nightmare subtype, {1}{U}, 2/3, blue). The JSON carries
        // no abilities — Flying + the additional-cost bounce are layered on.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. KeywordAbility marker so CombatAbilities surfaces
        // evasion / block-legality.
        card.AddAbility(new KeywordAbility(GrantedFlying, card, owner));

        return card;
    }

    /// <summary>
    /// Pay Fear of Isolation's additional cost — "return a permanent you
    /// control to its owner's hand" (CR 601.2f / 701.10). See the factory XML
    /// docs for the documented deviation from the printed cast-time additional
    /// cost (the return runs at resolve here, not at announcement).
    /// </summary>
    /// <param name="caster">The player paying the cost (the one returning a
    /// permanent they control).</param>
    /// <param name="self">The Fear of Isolation spell itself — excluded from
    /// the candidate set: it isn't on the battlefield while being cast, so it
    /// can't be the permanent returned (CR 601.2g — the cost is paid before the
    /// spell exists as a permanent).</param>
    /// <param name="zoneService">Zone service for replacement-bus-aware moves.
    /// May be null — raw zone move is used as fallback (shape / unit tests).</param>
    /// <param name="agent">Optional agent for the return choice. When null, the
    /// deterministic v1 picker (first eligible permanent) is used. Mirrors Kor
    /// Skyfisher's bounce policy.</param>
    public static IReadOnlyList<IEffect> BuildAdditionalCostPayment(
        Player caster,
        ICard? self = null,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        return new IEffect[]
        {
            new Effect(
                "Fear of Isolation: as an additional cost, return a permanent you control to its owner's hand.",
                async ctx =>
                {
                    // CR 109.5 / 601.2f — "a permanent you control." Eligible
                    // candidates are the permanents the caster controls on the
                    // battlefield, excluding the spell itself (it isn't a
                    // permanent yet — CR 601.2g).
                    var candidates = caster.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(p => !ReferenceEquals(p, self))
                        .Cast<ICard>()
                        .ToList();

                    // Printed text forbids casting with no permanent to return.
                    // v1's resolve-side payment is a no-op when none is eligible
                    // (documented deviation).
                    if (candidates.Count == 0) return;

                    ICard? pick;
                    if (agent != null)
                    {
                        pick = await agent
                            .ChooseFromBattlefieldAsync(caster, candidates, BotIntent.Bounce)
                            .ConfigureAwait(false);
                        // The return is mandatory (not "may") once the spell is
                        // being cast — fall back to a deterministic pick so the
                        // cost is always paid. Same posture as Kor Skyfisher /
                        // Thrill of Possibility's fallback.
                        if (pick is not Permanent || !candidates.Contains(pick))
                            pick = candidates[0];
                    }
                    else
                    {
                        pick = candidates[0];
                    }

                    if (pick is not Permanent target) return;

                    var targetOwner = target.Owner;
                    if (targetOwner == null) return;

                    // CR 701.10 — return to its owner's hand.
                    if (zoneService != null)
                    {
                        // Full path: replacement bus fires, CardMovedEvent
                        // published for downstream triggers.
                        zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
                    }
                    else
                    {
                        // Raw fallback: direct zone manipulation (shape / unit
                        // tests with no ZoneService).
                        var fromController = target.Controller ?? targetOwner;
                        fromController.Zones.Battlefield.RemoveCard(target);
                        targetOwner.Zones.Hand.AddCard(target);
                        target.SetZone(ZoneType.Hand);
                        target.SetController(targetOwner);
                    }
                }),
        };
    }
}
