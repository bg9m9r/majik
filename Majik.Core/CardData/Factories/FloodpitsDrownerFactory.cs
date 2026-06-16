using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Floodpits Drowner (Duskmourn, {1}{U}).
///
/// Creature — Merfolk 2/1. Oracle text (Scryfall, verified 2026-06-02):
///   "Flash
///    Vigilance
///    When this creature enters, tap target creature an opponent controls and
///    put a stun counter on it.
///    {1}{U}, {T}: Shuffle this creature and target creature with a stun counter
///    on it into their owners' libraries."
///
/// The base shape (name / Creature — Merfolk / {1}{U} / 2/1) is materialised
/// from the embedded JSON definition (<c>floodpits-drowner.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. Flash + Vigilance keyword markers,
/// the ETB triggered ability, and the {1}{U}, {T} activated ability are layered
/// on here — the JSON ability schema expresses none of these (same posture as
/// <see cref="ShacklegeistFactory"/> / <see cref="FrostLynxFactory"/>).
///
/// ## Implemented (v1)
/// - <b>2/1 Creature — Merfolk, {1}{U}</b>, owner / controller wired.
/// - <b>Flash (CR 702.8) + Vigilance (CR 702.20)</b> attached as
///   <see cref="KeywordAbility"/> markers (the NamedCardFactory path doesn't run
///   <see cref="Majik.Core.CardData.Parsing.KeywordBinder"/>, so attach inline —
///   same wiring as Endurance's Flash / Reach markers). Vigilance is read
///   combat-side via <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>ETB triggered ability (CR 603.6a)</b>, fired by
///   <see cref="CardMovedEvent"/> into <see cref="ZoneType.Battlefield"/>:
///   "tap target creature an opponent controls and put a stun counter on it."
///   - 1..1 TargetRequest "target creature an opponent controls" (mandatory —
///     no printed "may"). CandidateGatherer enumerates creatures controlled by
///     opponents (CR 109.5), mirroring <see cref="FrostLynxFactory"/>.
///   - On resolution (CR 608.2b legality re-check): if the target is still a
///     creature on the battlefield, it is tapped (CR 701.20) and one
///     <see cref="CounterType.Stun"/> counter is placed on it (CR 122.1c). The
///     stun counter is honoured by the untap-step replacement in
///     <c>TurnDriver.UntapStep</c> (CR 122.1g — same source of truth Kaito's
///     stun counters read).
/// - <b>{1}{U}, {T} activated ability (CR 602)</b>: "Shuffle this creature and
///   target creature with a stun counter on it into their owners' libraries."
///   - Cost is <see cref="ManaCostCost"/> {1}{U} + <see cref="AdditionalCost.Tap"/>
///     on Floodpits itself (the printed {T} symbol — CR 302.6 summoning sickness
///     applies via the central tap gate).
///   - 1..1 TargetRequest "target creature with a stun counter on it"; the
///     CandidateGatherer offers only creatures whose
///     <see cref="CounterCollection.Count"/> of <see cref="CounterType.Stun"/>
///     is &gt; 0 (any controller — the printed text is not opponent-scoped).
///   - Resolution (CR 608.2b) re-checks the target still has a stun counter,
///     then moves BOTH Floodpits and the target to THEIR OWNERS' libraries
///     (CR 701.19 — "owner's", not controller's) and shuffles those libraries.
///     If Floodpits has already left the battlefield (e.g. it was the cost's
///     tap source but was since removed), only the target is shuffled.
///
/// ## Deferred (v1 gaps)
/// - <b>True random shuffle hook</b>: <see cref="IZone"/> has no central
///   <c>Shuffle</c> entry point yet, so the activated ability mirrors
///   <see cref="EnduranceFactory"/>'s "remove all → Fisher-Yates → re-add"
///   helper. The observable contract (both cards end in the right libraries,
///   off the battlefield) is preserved.
/// </summary>
[CardName("Floodpits Drowner")]
public static class FloodpitsDrownerFactory
{
    public const string CardName = "Floodpits Drowner";
    public const string Slug = "floodpits-drowner";
    public const string ActivatedManaCost = "{1}{U}";
    public const int StunCountersPlaced = 1;

    private const string FlashKeyword = "Flash";
    private const string VigilanceKeyword = "Vigilance";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Floodpits Drowner owned and controlled by <paramref name="owner"/>.
    /// The base shape is materialised from the embedded JSON definition; the
    /// keyword markers, ETB trigger, and {1}{U}, {T} activated ability are
    /// layered on here. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.8 (Flash) + CR 702.20 (Vigilance) keyword markers.
        card.AddAbility(new KeywordAbility(FlashKeyword, card, owner));
        card.AddAbility(new KeywordAbility(VigilanceKeyword, card, owner));

        // ETB trigger (CR 603.6a).
        card.AddAbility(BuildEtbTrigger(card, owner));

        // {1}{U}, {T} activated ability (CR 602).
        card.AddAbility(BuildShuffleAbility(card, owner));

        return card;
    }

    // --- ETB: tap target opponent creature + one stun counter --------------

    private static TriggeredAbility BuildEtbTrigger(Creature card, Player owner)
    {
        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Floodpits Drowner — tap target opponent creature and put a stun counter on it",
            () =>
            {
                if (etbTrigger == null) return;
                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;
                if (chosen[0][0] is not Permanent target) return;

                // CR 608.2b — illegal target at resolution = no effect.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Creature)) return;

                // CR 701.20 — tap. CR 122.1c — put one stun counter on it.
                Fx.Tap(target);
                target.Counters.Add(CounterType.Stun, StunCountersPlaced);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // CR 109.5 — creatures controlled by a player OTHER than
                    // Floodpits' controller.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .Where(p => !ReferenceEquals(p, card.Controller ?? owner))
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            });

        return etbTrigger;
    }

    // --- {1}{U}, {T}: shuffle self + stunned target into owners' libraries --

    private static ActivatedAbility BuildShuffleAbility(Creature card, Player owner)
    {
        // RE-SOURCE-SAFE (agatha-bespoke-factory-tail-source-migration-batch):
        // "Shuffle THIS CREATURE and target stunned creature into their owners'
        // libraries" — the "this creature" half is the ability's own source, so
        // the effect reads the live ResolutionContext.Source (the re-homed bearer
        // under Agatha) rather than capturing `card`, falling back to `card` only
        // on the context-less legacy sync path (ResolutionContext.Legacy, Source =
        // null). The {T} portion of the cost is AdditionalCost.Tap, which RebindTo
        // Stage 1 re-homes onto the new source automatically (AdditionalCost
        // .RebindSource). The target gatherer is NOT controller-scoped (any
        // creature with a stun counter), so RebindController no-ops on it.
        // Marked RebindSafe so Agatha's Soul Cauldron re-homes this REAL ability
        // to a counter-bearing bearer via ActivatedAbility.RebindTo (CR 707.2 /
        // 613.1f): the BEARER (not the exiled Floodpits) taps + is shuffled away.
        // "Shuffle self + stunned target into owners' libraries" is OUTSIDE the
        // OracleActivatedAbilityBinder reconstructable set, so RebindTo of the
        // real ability is the only sound re-home.
        var shuffleEffect = new Effect(
            "Floodpits Drowner — shuffle this and target stunned creature into owners' libraries",
            ctx =>
            {
                if (ctx.ChosenTargets.Count == 0) return ValueTask.CompletedTask;
                if (ctx.ChosenTargets[0].Count == 0) return ValueTask.CompletedTask;
                if (ctx.ChosenTargets[0][0] is not Permanent target) return ValueTask.CompletedTask;

                // The re-homed bearer under Agatha (ctx.Source), else this card.
                var self = (ctx.Source as Permanent) ?? card;

                // CR 608.2b — re-check legality: target must still be a creature
                // on the battlefield with a stun counter on it.
                var targetLegal = target.Zone == ZoneType.Battlefield
                                  && target.HasType(CardType.Creature)
                                  && target.Counters.Count(CounterType.Stun) > 0;
                if (!targetLegal) return ValueTask.CompletedTask;

                // CR 701.19 — both cards go to THEIR OWNERS' libraries, then
                // those libraries are shuffled. (The self permanent may already
                // be gone if something removed it after the cost was paid — guard
                // it. Don't double-shuffle if self IS the chosen target.)
                if (self.Zone == ZoneType.Battlefield && !ReferenceEquals(self, target))
                {
                    ShuffleIntoOwnersLibrary(self);
                }
                ShuffleIntoOwnersLibrary(target);

                return ValueTask.CompletedTask;
            });

        return new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { shuffleEffect },
            rebindSafe: true,
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature with a stun counter on it",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Any creature with a stun counter (CR 122.1) — the printed
                    // text is NOT opponent-scoped.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Counters.Count(CounterType.Stun) > 0)
                        .Cast<object>()
                        .ToList()),
            });
    }

    /// <summary>
    /// CR 701.19 — move <paramref name="permanent"/> from its current zone to
    /// its OWNER's library, then shuffle that library.
    /// <see cref="IZone"/> exposes no central shuffle hook yet, so mirror
    /// <see cref="EnduranceFactory"/>'s "remove all → Fisher-Yates → re-add"
    /// helper. The observable contract — the card lands in its owner's library,
    /// off the battlefield — is preserved.
    /// </summary>
    private static void ShuffleIntoOwnersLibrary(Permanent permanent)
    {
        var cardOwner = permanent.Owner;
        if (cardOwner == null) return;

        var controller = permanent.Controller;
        controller?.Zones.Battlefield.RemoveCard(permanent);

        cardOwner.Zones.Library.AddCard(permanent);
        permanent.SetZone(ZoneType.Library);

        ShuffleLibrary(cardOwner);
    }

    /// <summary>
    /// CR 701.19 shuffle. Mirrors <see cref="EnduranceFactory"/>'s helper:
    /// remove all → Fisher-Yates with a fresh RNG → re-add.
    /// </summary>
    private static void ShuffleLibrary(Player player)
    {
        var lib = player.Zones.Library.GetCards().ToList();
        foreach (var c in lib) player.Zones.Library.RemoveCard(c);

        var rng = new System.Random();
        for (var i = lib.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (lib[i], lib[j]) = (lib[j], lib[i]);
        }

        foreach (var c in lib) player.Zones.Library.AddCard(c);
    }
}
