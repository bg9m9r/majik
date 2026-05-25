using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hard Evidence (Innistrad: Midnight Hunt, {U}).
///
/// Sorcery. Oracle text:
///   "Return target creature to its owner's hand.
///    Investigate. (Create a Clue token. It's an artifact with
///    \"{2}, Sacrifice this artifact: Draw a card.\")"
///
/// ## Why it gets its own factory
/// One-mana bounce stapled to a Clue is rare — every comparable
/// "bounce + token" rider (Vapor Snag's life-loss, Snap's untap-lands)
/// either shifts the rate or shifts the colour. Hard Evidence pays
/// at the same rate as Unsummon while producing a card-draw spring on
/// top, which makes it a Pauper Modern dimir staple in dredge-style
/// graveyard shells. Both halves are already cleanly modelled — the
/// spell template binder covers raw "Investigate." sorceries via
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Tokens.InvestigateSingleTemplate"/>
/// and the bounce idiom is templated in
/// <see cref="IntoTheFloodMawFactory"/> — so the named factory is a
/// thin composition.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {U}.
/// - <b>Return target creature to its owner's hand</b> —
///   <see cref="BuildSpellDefinition"/> declares a single 1..1 "target
///   creature" <see cref="TargetRequest"/> (Intent:
///   <see cref="BotIntent.Bounce"/>) with a live
///   <see cref="TargetRequest.CandidateGatherer"/> that enumerates every
///   creature on the battlefield across every player. On resolution the
///   targeted creature returns to its OWNER'S hand (CR 701.20) via
///   <see cref="ZoneService.MoveCard"/> when supplied (so any
///   leave-battlefield triggers fire — Bridge from Below, Skullclamp's
///   "creature you control dies", etc.) or direct-zone mutation
///   otherwise.
/// - <b>Investigate</b> (CR 701.30) — creates one Clue token under the
///   caster via <see cref="TokenFactory.CreateClue"/>. Resolution order
///   matches printed text: bounce first, then investigate (Clues seen
///   by post-bounce ETB-tracking triggers like Tireless Tracker).
/// - Self-target bounce is legal at v1 (Hard Evidence has no "an
///   opponent controls" gate — printed "target creature" with no
///   ownership clause). Resolution-time legality check enforces
///   creature-on-battlefield only.
///
/// ## Deferred (v1 gaps)
/// - <b>Token target re-removal</b>: tokens bounced to their owner's
///   hand cease to exist via SBA 704.5d on the next state-based action
///   pass (handled by <see cref="Majik.Core.Rules.StateBasedActions"/>'s
///   <c>TokensCeaseToExistCheck</c>). The factory does no special-case
///   short-circuit — same posture as <see cref="IntoTheFloodMawFactory"/>.
/// - <b>Target legality in ActionValidator</b>: validator does not yet
///   filter to "creature" at announcement; resolution-time guard handles
///   illegal targets (CR 608.2b). Same posture as the rest of the
///   factory pool.
/// </summary>
[CardName("Hard Evidence")]
public static class HardEvidenceFactory
{
    public const string CardName = "Hard Evidence";
    public const string PrintedManaCost = "{U}";

    /// <summary>Printed oracle text — informational.</summary>
    public const string OracleText =
        "Return target creature to its owner's hand.\n"
        + "Investigate. (Create a Clue token. It's an artifact with "
        + "\"{2}, Sacrifice this artifact: Draw a card.\")";

    /// <summary>
    /// Build the card shape only. The resolve-time target request +
    /// bounce + investigate effect is built on demand via
    /// <see cref="BuildSpellDefinition"/> (mirrors
    /// <see cref="BoneShardsFactory"/>'s spell-def shape).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Hard Evidence is
    /// cast. Declares a single 1..1 "target creature" <see cref="TargetRequest"/>;
    /// on resolution the targeted creature returns to its owner's hand
    /// (CR 701.20) and a Clue token is created under the caster
    /// (CR 701.30).
    /// </summary>
    /// <param name="caster">The player casting Hard Evidence. Required —
    /// the investigate clause creates the Clue token under the caster
    /// (CR 701.30 — "you create a Clue token").</param>
    /// <param name="resolver">Resolves the chosen target token to the live
    /// engine object (chosen target → live game object).</param>
    /// <param name="zoneService">Optional <see cref="ZoneService"/> —
    /// when supplied the bounce routes through
    /// <see cref="ZoneService.MoveCard"/> so any leave-battlefield
    /// triggers fire (CR 603.6a / CR 701.20).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
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
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Bounce,
                    // Live gatherer: every creature on the battlefield
                    // across all players. HeuristicBotAgent's Bounce
                    // intent prefers opponent-controlled targets so
                    // self-bounce is a last-resort fallback.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: bounce target creature",
                        () => ResolveBounce(raw, zoneService)),
                    new Effect(
                        $"{CardName}: investigate (create a Clue)",
                        () => TokenFactory.CreateClue(caster)),
                };
            });
    }

    /// <summary>
    /// CR 701.20 — return <paramref name="raw"/> to its owner's hand.
    /// Routes through <see cref="ZoneService.MoveCard"/> when supplied
    /// so leave-battlefield triggers fire; otherwise raw-zone mutation.
    /// Resolution-time guard (CR 608.2b) ensures the target is still a
    /// creature on the battlefield.
    /// </summary>
    private static void ResolveBounce(object raw, ZoneService? zoneService)
    {
        if (raw is not Card target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // CR 608.2b — resolution-time type check. Cards that lost their
        // creature type between announcement and resolution fizzle.
        if (!target.HasType(CardType.Creature)) return;

        var owner = target.Owner;
        if (owner == null) return;

        var controller = target.Controller ?? owner;

        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            owner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(owner);
        }
        // CR 111.7 / SBA 704.5d — token targets briefly exist in their
        // owner's Hand and are cleared by the next SBA pass.
    }
}
