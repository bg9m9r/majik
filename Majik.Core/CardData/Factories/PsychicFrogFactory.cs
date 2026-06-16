using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Psychic Frog (Modern Horizons 3, {U}{B}).
///
/// Creature — Frog 1/2. Oracle text (verified against Scryfall 2026-06-16):
///   "Whenever this creature deals combat damage to a player or
///    planeswalker, draw a card.
///    Discard a card: Put a +1/+1 counter on this creature.
///    Exile three cards from your graveyard: This creature gains flying
///    until end of turn."
///
/// ## Implemented (v1)
///
/// - <b>1/2 Creature — Frog at {U}{B}</b> (CR 205.3m). No printed Flying —
///   the only Flying access is the third activated ability below.
/// - <b>Combat-damage "draw a card" trigger (CR 510 / CR 603.1)</b> — fires
///   on a <see cref="CombatDamageDealtEvent"/> whose
///   <see cref="CombatDamageDealtEvent.Source"/> is Psychic Frog AND whose
///   target is a player (<see cref="DamageDealtEvent.TargetPlayer"/> non-null)
///   OR an (effective) planeswalker
///   (<see cref="Permanent.IsEffectivePlaneswalker"/>). On resolution the
///   controller draws one card; empty library stamps the loss condition via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> (CR 704.5b / 120.3).
/// - <b>"Discard a card" activated ability — +1/+1 counter (CR 602)</b> —
///   <see cref="DiscardACardCost"/> (any card from hand) followed by an
///   effect that places a <see cref="CounterType.PlusOnePlusOne"/> counter on
///   the live <see cref="ResolutionContext.Source"/>. Repeatable while the
///   controller has a card to discard. No mana cost.
/// - <b>"Exile three cards from your graveyard" activated ability — gains
///   flying until end of turn (CR 602)</b> — the exile-three-from-graveyard
///   cost is performed inside the resolve closure (the generic
///   <see cref="AdditionalCost"/> surface has no exile-from-graveyard payment
///   type — same posture as <see cref="GrimLavamancerFactory"/> /
///   <see cref="ClingToDustFactory"/>). The closure short-circuits if fewer
///   than three cards are available (CR 601.2g). When a
///   <see cref="ContinuousEffectsService"/> is supplied, a Layer-6
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> grants the live source
///   "Flying" until end of turn (CR 514.2).
///
/// ## Re-source safety (Agatha's Soul Cauldron)
///
/// Both activated abilities read the live <see cref="ResolutionContext.Source"/>
/// (and its controller's graveyard) rather than capturing <c>card</c> /
/// <c>owner</c>, falling back only on the context-less legacy sync path
/// (<see cref="ResolutionContext.Legacy"/>, Source = null). Both are marked
/// <c>rebindSafe: true</c> so Agatha's Soul Cauldron re-homes the REAL
/// abilities — the +1/+1 counter lands on the BEARER, and the flying grant /
/// graveyard-exile cost read the BEARER's controller (CR 707.2 / 613.1f).
/// <see cref="DiscardACardCost"/> is a player-resource cost (no captured
/// source) and is passed through unchanged by RebindTo.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Trigger + both activated
///   abilities are attached; no <see cref="TriggerManager"/> registration and
///   no flying-grant <see cref="ContinuousEffectsService"/>. Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, ReplacementBus?, ContinuousEffectsService?)"/>
///   — runtime-wired.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Discard prompt</b> on the discard activation cost (CR 701.16a — the
///   discarding player chooses) — v1 deterministically picks the first card
///   in hand. Same queue as Liliana of the Veil + Faithless Looting.
/// - <b>Which three graveyard cards are exiled</b>: the closure exiles the
///   three front-most graveyard cards (insertion order); an agent-driven pick
///   is a future refinement.
/// </summary>
[CardName("Psychic Frog")]
public static class PsychicFrogFactory
{
    public const string CardName = "Psychic Frog";
    public const string Cost = "{U}{B}";
    public const int GraveyardExileCount = 3;
    public const string GrantedKeyword = "Flying";

    /// <summary>
    /// Constructs Psychic Frog with no live wiring. Combat-damage trigger +
    /// both activated abilities are attached for shape; the trigger is NOT
    /// registered and the flying grant has no continuous-effects service.
    /// Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, replacements: null, effects: null);

    /// <summary>
    /// Constructs Psychic Frog. When <paramref name="triggers"/> is supplied,
    /// the combat-damage trigger is registered. When <paramref name="replacements"/>
    /// is supplied, the discard-activated +1/+1 counter placement is routed
    /// through <see cref="CountersService.Add"/> so Hardened Scales / Doubling
    /// Season replacements can rewrite the count (CR 614). When
    /// <paramref name="effects"/> is supplied, the "gains flying until end of
    /// turn" grant registers with the continuous-effects service (CR 514.2).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements = null,
        ContinuousEffectsService? effects = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: 1,
            toughness: 2,
            subtypes: new[] { CardSubtype.Frog });

        card.SetOwner(owner);
        card.SetController(owner);

        if (effects != null)
        {
            card.ActiveEffects = effects;
        }

        // ----------------------------------------------------------------
        // Combat-damage "draw a card" trigger — CR 510, CR 603.1.
        //   "Whenever this creature deals combat damage to a player or
        //    planeswalker, draw a card."
        // Fires when Psychic Frog is the source AND the target is a player
        // OR an (effective) planeswalker.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () =>
            {
                var controller = card.Controller ?? owner;
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    controller.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Hand.AddCard(top);
            });

        var drawTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer != null) return true;
                return e.TargetCard is Permanent p && p.IsEffectivePlaneswalker();
            }),
            effects: new IEffect[] { drawEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(drawTrigger);
        triggers?.RegisterTriggeredAbility(drawTrigger);

        // ----------------------------------------------------------------
        // Activated ability — "Discard a card: Put a +1/+1 counter on this
        // creature." CR 602. RE-SOURCE-SAFE: the counter lands on the live
        // ctx.Source (the BEARER under Agatha), falling back to `card` on the
        // legacy sync path. DiscardACardCost is a player-resource cost.
        // ----------------------------------------------------------------
        var pumpEffect = new Effect(
            $"{CardName}: put a +1/+1 counter on it",
            ctx =>
            {
                var subject = (ctx.Source as Permanent) ?? card;
                CountersService.Add(subject, CounterType.PlusOnePlusOne, 1, replacements);
                return ValueTask.CompletedTask;
            });

        var pumpAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new DiscardACardCost() },
            effects: new IEffect[] { pumpEffect },
            rebindSafe: true);

        card.AddAbility(pumpAbility);

        // ----------------------------------------------------------------
        // Activated ability — "Exile three cards from your graveyard: This
        // creature gains flying until end of turn." CR 602.
        // The exile-three-from-graveyard cost is paid inside the resolve
        // closure (no AdditionalCost enum member for it — same posture as
        // Grim Lavamancer); the closure short-circuits if fewer than three
        // cards are available (CR 601.2g). RE-SOURCE-SAFE: the cost reads the
        // live ctx.Source's controller's graveyard and the flying grant lands
        // on ctx.Source (the BEARER under Agatha).
        // ----------------------------------------------------------------
        var flyingEffect = new Effect(
            $"{CardName}: exile three from graveyard, gain flying until end of turn",
            ctx =>
            {
                var subject = (ctx.Source as Creature) ?? card;
                var controller = subject.Controller ?? card.Controller ?? owner;

                var graveyard = controller.Zones.Graveyard.GetCards().ToList();

                // CR 601.2g — the exile-three cost can only be paid if 3+
                // cards are present. If not, the activation is illegal: no
                // exile, no flying grant.
                if (graveyard.Count < GraveyardExileCount)
                {
                    return ValueTask.CompletedTask;
                }

                for (var i = 0; i < GraveyardExileCount; i++)
                {
                    var toExile = graveyard[i];
                    controller.Zones.Graveyard.RemoveCard(toExile);
                    controller.Zones.Exile.AddCard(toExile);
                    if (toExile is Card concrete) concrete.SetZone(ZoneType.Exile);
                }

                // CR 514.2 — "gains flying until end of turn": a Layer-6 grant
                // that expires at cleanup. Registered only when a continuous-
                // effects service is wired; the shape-only path pays the cost
                // (proves the exile) but cannot mutate keywords.
                var fx = effects ?? subject.ActiveEffects;
                fx?.Register(new GrantKeywordUntilEndOfTurnEffect(subject, GrantedKeyword));

                return ValueTask.CompletedTask;
            });

        var flyingAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: System.Array.Empty<ICost>(),
            effects: new IEffect[] { flyingEffect },
            rebindSafe: true);

        card.AddAbility(flyingAbility);

        return card;
    }
}
