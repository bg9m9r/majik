using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Loran of the Third Path (The Brothers' War,
/// {2}{W}).
///
/// Legendary Creature — Human Artificer 2/1. Oracle text (verified against
/// Scryfall):
///   "Vigilance
///    When Loran enters, destroy up to one target artifact or enchantment.
///    {T}: You and target opponent each draw a card."
///
/// The base shape (name, Legendary supertype, Creature, Human / Artificer
/// subtypes, {2}{W}, 2/1) is materialised from the embedded JSON definition
/// (<c>loran-of-the-third-path.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="IngotChewerFactory"/>. The JSON <c>AbilityDefinition</c>
/// schema doesn't express keyword markers, targeted destroy effects, or
/// player-targeting draw abilities, so Vigilance + the ETB destroy trigger
/// + the {T} symmetric-draw activated ability are layered on top here.
///
/// ## Implemented (v1)
/// - 2/1 Legendary Human Artificer at {2}{W}.
/// - <b>Vigilance (CR 702.20)</b> — wired as a <see cref="KeywordAbility"/>
///   marker (read by CombatAbilities.HasVigilance). The NamedCardFactory /
///   direct-test path doesn't run KeywordBinder, so attach the marker here
///   for parity with the data-driven load (same posture as
///   <see cref="IngotChewerFactory"/>'s Evoke marker).
/// - <b>ETB destroy trigger (CR 603.6a)</b>: "destroy up to one target
///   artifact or enchantment." Modelled on <see cref="ReclamationSageFactory"/>
///   but with <c>MinTargets = 0</c> (CR 115.1a — "up to one" is optional, so
///   zero chosen targets resolves as a clean no-op). Resolution reads
///   <see cref="TriggeredAbility.ChosenTargets"/>; validates the chosen
///   target is still an artifact OR enchantment on the battlefield
///   (CR 608.2b — illegal / absent target → clean no-op); destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible cancels
///   per CR 702.12, active regeneration shield consumed per CR 701.15).
/// - <b>{T}: You and target opponent each draw a card</b> — an ordinary
///   <see cref="ActivatedAbility"/> (CR 602) whose only cost is
///   <see cref="AdditionalCost.Tap(Permanent)"/> on Loran, declaring a single
///   1..1 "target opponent" <see cref="TargetRequest"/> (player target,
///   Intent: <see cref="BotIntent.Draw"/> — same player-target posture as
///   <see cref="ArchiveTrapFactory"/>'s "target opponent"). On resolution the
///   controller draws one card and, if the chosen target resolves to an
///   opponent <see cref="Player"/> (CR 109.1 — "opponent" excludes the
///   controller), that opponent draws one card too, both routed through
///   <see cref="Fx.DrawCards"/> so DrawCardIntent replacements (Dredge,
///   Narset, etc.) participate.
///
/// ## Deferred (v1 gaps — same posture as the other "target opponent" /
/// ETB-destroy v1 factories)
/// - <b>Real agent-driven target prompt</b>: production callers wire
///   <see cref="TriggeredAbility.SetChosenTargets"/> /
///   <see cref="ActivatedAbility.SetChosenTargets"/> from an agent prompt
///   before resolution. The ETB destroy falls back to the first legal
///   artifact/enchantment on the controller's battlefield when no agent
///   picked; the symmetric draw skips the opponent draw when no opponent was
///   chosen (the controller's own draw always happens — it is not a target).
/// - <b>Target legality in ActionValidator</b>: the validator does not filter
///   the picks to "artifact or enchantment" / "opponent" at announcement;
///   the resolution-time guards handle illegal picks (CR 608.2b / CR 109.1).
/// </summary>
[CardName("Loran of the Third Path")]
public static class LoranOfTheThirdPathFactory
{
    public const string CardName = "Loran of the Third Path";
    public const string Slug = "loran-of-the-third-path";

    /// <summary>CR 702.20 — Vigilance marker keyword.</summary>
    private const string VigilanceKeyword = "Vigilance";

    /// <summary>
    /// Construct Loran with no live trigger-manager wiring. Produces the
    /// correct card identity + Vigilance marker + ETB destroy trigger shape +
    /// {T} symmetric-draw activated ability; the ETB trigger is NOT registered
    /// with any <see cref="TriggerManager"/>. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Loran with optional <see cref="TriggerManager"/> wiring. When
    /// <paramref name="triggers"/> is supplied the ETB destroy trigger is
    /// registered for bus-driven firing (CR 603.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // supertype, Creature, Human/Artificer subtypes, {2}{W}, 2/1). The
        // JSON carries no abilities — they're layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // Vigilance — CR 702.20. KeywordBinder isn't run on the
        // NamedCardFactory / direct-test path, so attach the marker here for
        // parity with the data-driven load.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility(VigilanceKeyword, card, owner));

        // ----------------------------------------------------------------
        // ETB destroy trigger — CR 603.6a.
        //   "When Loran enters, destroy up to one target artifact or
        //    enchantment."
        // MinTargets = 0 (CR 115.1a — "up to one" is optional). Live gatherer
        // enumerates the battlefield across every player so the agent's target
        // picker sees an up-to-date legal set at resolution.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;

        var etbEffect = new Effect(
            $"{CardName}: destroy up to one target artifact or enchantment",
            () => ResolveDestroy(owner, etb));

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "up to one target artifact or enchantment",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: GatherDestroyTargets(owner).Cast<object>().ToList(),
                    Intent: BotIntent.Removal,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(etb);
        triggers?.RegisterTriggeredAbility(etb);

        // ----------------------------------------------------------------
        // {T}: You and target opponent each draw a card. — CR 602.
        // Cost: tap Loran (CR 602.1 / 118.3). Declares a single 1..1
        // "target opponent" TargetRequest (player target — same posture as
        // ArchiveTrapFactory). On resolution the controller draws 1 and the
        // chosen opponent draws 1, both via Fx.DrawCards so DrawCardIntent
        // replacements participate (CR 614).
        // ----------------------------------------------------------------
        ActivatedAbility? draw = null;

        var drawEffect = new Effect(
            $"{CardName}: you and target opponent each draw a card",
            () => ResolveDraw(card, owner, draw));

        draw = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { AdditionalCost.Tap(card) },
            effects: new IEffect[] { drawEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target opponent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Draw,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, ctx.Self))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(draw);

        return card;
    }

    /// <summary>
    /// Snapshot the controller-visible legal-target set for the ETB destroy at
    /// trigger-creation time. Production callers refresh via
    /// <see cref="TargetRequest.CandidateGatherer"/> at resolution.
    /// </summary>
    private static IReadOnlyList<ICard> GatherDestroyTargets(Player owner) =>
        owner.Zones.Battlefield.GetCards()
            .Where(c => c.HasType(CardType.Artifact)
                     || c.HasType(CardType.Enchantment))
            .ToList();

    /// <summary>
    /// Resolve the ETB destroy. "Up to one" — zero chosen targets is a clean
    /// no-op (CR 115.1a). Honours <see cref="TriggeredAbility.ChosenTargets"/>
    /// when set by the agent; the deterministic single-arg dispatcher path
    /// (no agent) does NOT auto-pick a target, because the destroy is optional.
    /// Validates the chosen target is still a legal artifact / enchantment on
    /// the battlefield (CR 608.2b) before destroying (CR 701.7).
    /// </summary>
    private static void ResolveDestroy(Player owner, TriggeredAbility? etb)
    {
        // "Up to one" is optional (CR 115.1a) — only act when the agent
        // actually chose a target. No deterministic fallback (unlike the
        // mandatory "destroy target artifact" of Ingot Chewer).
        if (etb == null
            || etb.ChosenTargets.Count == 0
            || etb.ChosenTargets[0].Count == 0
            || etb.ChosenTargets[0][0] is not Permanent picked)
        {
            return;
        }

        // CR 608.2b — illegal-on-resolution check.
        if (picked.Zone != ZoneType.Battlefield) return;
        if (!(picked.HasType(CardType.Artifact)
              || picked.HasType(CardType.Enchantment))) return;

        // CR 701.7 — destroy. Indestructible (CR 702.12) cancels; active
        // regeneration shield (CR 701.15) is consumed.
        OracleSpellBinder.MoveToGraveyard(picked, ZoneMoveReason.Destroy);
    }

    /// <summary>
    /// Resolve "You and target opponent each draw a card." The controller's
    /// own draw is not a target and always happens (CR 602). The opponent
    /// draw fires only when the agent chose an opponent <see cref="Player"/>
    /// (CR 109.1 — "opponent" excludes the controller). Both draws route
    /// through <see cref="Fx.DrawCards"/> so replacement effects participate
    /// (CR 614).
    /// </summary>
    private static void ResolveDraw(Creature loran, Player owner, ActivatedAbility? draw)
    {
        var controller = loran.Controller ?? owner;

        // CR 102.3 / sequencing: APNAP — the controller draws first, then the
        // targeted opponent. Single-player resolution order is otherwise
        // immaterial here.
        Fx.DrawCards(controller, 1);

        if (draw != null
            && draw.ChosenTargets.Count > 0
            && draw.ChosenTargets[0].Count > 0
            && draw.ChosenTargets[0][0] is Player target
            && !ReferenceEquals(target, controller))
        {
            Fx.DrawCards(target, 1);
        }
    }
}
