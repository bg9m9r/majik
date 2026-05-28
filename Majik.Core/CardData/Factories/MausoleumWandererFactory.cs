using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mausoleum Wanderer (Shadows over Innistrad, {U}).
///
/// Creature — Spirit 1/1. Oracle text:
///   "Flying.
///    Whenever another Spirit enters under your control, Mausoleum Wanderer
///    gets +1/+1 until end of turn.
///    Sacrifice Mausoleum Wanderer: Counter target instant or sorcery spell
///    unless its controller pays {X}, where X is Mausoleum Wanderer's power."
///
/// ## Implemented (v1)
/// - 1/1 Spirit at {U} (CR 205.3m), Flying (CR 702.9) wired via
///   <see cref="KeywordAbility"/> marker so the combat validator denies
///   non-flying / non-reach blockers.
/// - <b>"Another Spirit enters under your control" trigger</b> (CR 603.6a):
///   fires on <see cref="CardMovedEvent"/> → Battlefield where the entering
///   card is a Creature with the Spirit subtype, is NOT Mausoleum Wanderer
///   itself ("another"), and its controller matches Mausoleum Wanderer's
///   controller (CR 109.5). On resolution registers a
///   <see cref="PumpUntilEndOfTurnEffect"/> for +1/+1 EOT against the
///   supplied <see cref="ContinuousEffectsService"/> (CR 613.1f, Layer 7c).
///   When effects is null the pump is a no-op (shape-only path).
/// - <b>Sac-self → counter-unless-pay-X activated ability</b> (CR 113.3b /
///   CR 701.5): cost is <see cref="AdditionalCost.Sacrifice"/>(self); same
///   stub posture as <see cref="CursecatcherFactory"/> — the effect body
///   moves the card to its owner's graveyard since
///   <see cref="AdditionalCost.Sacrifice"/>.Pay is a TODO stub. Target is
///   a 1..1 "target instant or sorcery spell"
///   <see cref="TargetRequest"/>. CR 202.3b — X = Wanderer's power
///   captured at activation time (BEFORE the sac fires) via
///   <see cref="Creature.GetPower"/>; this matches the printed wording
///   "where X is Mausoleum Wanderer's power" (sampled at the moment the
///   ability is activated — same shape as Pump-then-sac creatures the
///   compendium / Comp Rules describe). v1 auto-resolves the "pay {X}"
///   check by consulting the target spell's controller's mana pool
///   (mirrors <see cref="CursecatcherFactory"/> / <see cref="ManaLeakFactory"/>).
///   If the controller can pay {X}, payment is auto-deducted and the spell
///   is NOT countered. Otherwise the spell is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> and moved to the
///   graveyard.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Flying + both abilities
///   attached for dispatcher / shape tests; no live counter / pump.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?, TriggerManager?, ContinuousEffectsService?)"/>
///   — fully wired. Stack handle drives the counter; TriggerManager
///   registers the Spirit-ETB pump trigger; ContinuousEffectsService
///   carries the EOT pump.
///
/// ## Power-capture timing (CR 202.3b)
/// X is read at activation time, BEFORE the sacrifice moves Wanderer to
/// the graveyard. We capture <c>card.GetPower()</c> in the effect closure
/// at the top of the effect body (still on the battlefield, EOT pumps from
/// prior Spirit-ETB triggers still applied). If the activation timing ever
/// shifts to capturing X at cost-payment, only the snapshot order has to
/// change — the math is identical.
/// </summary>
[CardName("Mausoleum Wanderer")]
public static class MausoleumWandererFactory
{
    public const string CardName = "Mausoleum Wanderer";
    public const string PrintedManaCost = "{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Mausoleum Wanderer with no runtime services. Flying +
    /// both abilities are attached to the card shape; the Spirit-ETB
    /// pump body is a no-op (no effects service) and the sac/counter
    /// activated ability's counter half is also a no-op (no stack).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null, continuousEffects: null);

    /// <summary>
    /// Construct Mausoleum Wanderer with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Active stack — when supplied the sac/counter
    /// activated ability removes the targeted spell from the stack on
    /// resolution via <see cref="OracleSpellBinder.RemoveFromStack"/>.
    /// May be null for shape tests.</param>
    /// <param name="triggers">TriggerManager — when supplied the Spirit-
    /// ETB pump trigger is registered so a "Spirit enters" CardMovedEvent
    /// lands the pump on the stack automatically. May be null — the
    /// trigger is still attached to the card shape.</param>
    /// <param name="continuousEffects">Layers service — required for the
    /// +1/+1 EOT pump (<see cref="PumpUntilEndOfTurnEffect"/>). May be
    /// null — the trigger fires but the pump is a no-op.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Spirit });

        card.SetOwner(owner);
        card.SetController(owner);

        // Wire the effects service onto the card so card.Power /
        // card.Toughness reads flow through layers (CR 613 — Layer 7c
        // applies PumpUntilEndOfTurnEffect).
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
        }

        // CR 702.9 — Flying. KeywordAbility marker consumed by the combat
        // validator's flying / reach gate.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Triggered ability — "Whenever another Spirit enters under your
        // control, Mausoleum Wanderer gets +1/+1 until end of turn."
        // CR 603.6a. Predicate:
        //   - ToZone == Battlefield (the entering side of CardMovedEvent)
        //   - Card is a Creature with the Spirit subtype
        //   - NOT Mausoleum Wanderer itself ("another" qualifier)
        //   - Card.Controller == Wanderer's controller (CR 109.5 — "you")
        // Effect: register a PumpUntilEndOfTurnEffect for +1/+1 EOT on
        // Wanderer (CR 613.1f, Layer 7c).
        // ----------------------------------------------------------------
        var pumpCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => e.ToZone == ZoneType.Battlefield
                      && e.Card.HasType(CardType.Creature)
                      && e.Card.HasSubtype(CardSubtype.Spirit)
                      && !ReferenceEquals(e.Card, card)
                      && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var pumpEffect = new Effect(
            $"{CardName}: +1/+1 EOT (another Spirit you control entered) — CR 613.1f Layer 7c",
            () =>
            {
                if (card.ActiveEffects == null) return;
                card.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(card, 1, 1));
            });

        var pumpTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: pumpCondition,
            effects: new IEffect[] { pumpEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(pumpTrigger);
        triggers?.RegisterTriggeredAbility(pumpTrigger);

        // ----------------------------------------------------------------
        // Activated ability — "Sacrifice Mausoleum Wanderer: Counter
        // target instant or sorcery spell unless its controller pays {X},
        // where X is Mausoleum Wanderer's power." CR 113.3b / CR 701.5.
        //
        // - Cost: AdditionalCost.Sacrifice(self) — TODO-stub Pay; effect
        //   body performs the zone move (mirrors CursecatcherFactory).
        // - Target: 1..1 TargetRequest "target instant or sorcery spell".
        // - X: card.GetPower() captured at activation (CR 202.3b /
        //   class xmldoc — captured BEFORE the sac so any prior pumps
        //   still apply).
        // - v1 auto-pay: if the target spell's controller can pay {X},
        //   payment is deducted and the spell is NOT countered. Otherwise
        //   the spell is removed from the stack and moved to graveyard.
        // ----------------------------------------------------------------
        ActivatedAbility? ability = null;

        var counterEffect = new Effect(
            "Mausoleum Wanderer — sac self, then counter target instant or sorcery unless its controller pays {X}",
            () => ResolveCounterActivation(ability, card, owner, stack));

        ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { counterEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target instant or sorcery spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Counter,
                    // CR 601.2c — choose-time legality. Enumerate the
                    // live stack and filter to spells whose card is
                    // an Instant or Sorcery. Counter intent in the bot
                    // ranker picks the most expensive eligible spell.
                    CandidateGatherer: ctx => ctx.Stack.GetAll()
                        .OfType<ISpell>()
                        .Where(s => s.Card != null
                                    && (s.Card.HasType(CardType.Instant)
                                        || s.Card.HasType(CardType.Sorcery)))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(ability);

        return card;
    }

    // --- Sac + counter-unless-pay-X (CR 113.3b / 701.5 / 202.3b) ---------

    private static void ResolveCounterActivation(
        ActivatedAbility? ability,
        Creature card,
        Player owner,
        Majik.Core.Stack.Stack? stack)
    {
        // CR 202.3b — capture X = Wanderer's power BEFORE the sac fires so
        // any prior Spirit-ETB pumps still apply.
        int x = card.GetPower();

        SacrificeSelf(card, owner);

        if (ability == null || stack == null) return;
        var spell = ResolveTargetSpell(ability);
        if (spell == null) return;

        if (ControllerPaidX(spell, x))
        {
            // Controller paid — spell is NOT countered.
            return;
        }

        // CR 701.5 — counter: remove from stack + send to graveyard.
        OracleSpellBinder.RemoveFromStack(stack, spell);
        spell.Card.SetZone(ZoneType.Graveyard);
    }

    private static void SacrificeSelf(Creature card, Player owner)
    {
        // Sacrifice (zone-move stub — see class xmldoc).
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        var sacOwner = card.Owner ?? owner;
        sacOwner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }

    private static ISpell? ResolveTargetSpell(ActivatedAbility ability)
    {
        var chosen = ability.ChosenTargets;
        if (chosen.Count == 0 || chosen[0].Count == 0) return null;
        if (chosen[0][0] is not ISpell spell) return null;

        // Re-check legality at resolution (CR 608.2b).
        var targetCard = spell.Card as Card;
        if (targetCard == null) return null;
        if (targetCard.Zone != ZoneType.Stack) return null;
        if (!targetCard.HasType(CardType.Instant) && !targetCard.HasType(CardType.Sorcery)) return null;

        return spell;
    }

    private static bool ControllerPaidX(ISpell spell, int x)
    {
        // CR 118.4 — controller may pay {X}. v1 auto-pays when mana is
        // available (same posture as Cursecatcher / Daze / Mana Leak).
        if (x <= 0) return false;
        if (spell.Controller is null) return false;
        return spell.Controller.PayMana(ManaCost.Zero.AddGenericCost(x));
    }
}
