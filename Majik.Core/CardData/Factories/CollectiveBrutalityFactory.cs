using Majik.Core.Abilities;
using Majik.Core.CardData.SpellTemplates.Templates.Bespoke;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Collective Brutality (Eldritch Moon, {1}{B}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Escalate—Discard a card. (Pay this cost for each mode chosen beyond the first.)
///    Choose one or more —
///     • Target opponent reveals their hand. You choose an instant or sorcery
///       card from it. That player discards that card.
///     • Target creature gets -2/-2 until end of turn.
///     • Target opponent loses 2 life and you gain 2 life."
///
/// ## Mechanics
/// - <b>Choose one or more</b> (CR 700.2d) — the caster picks ≥1 distinct
///   modes; each chosen mode's effect resolves in printed order (CR 608.2c).
///   Modelled with <see cref="SpellDefinition.MinModes"/> = 1,
///   <see cref="SpellDefinition.MaxModes"/> = 3, and the multi-pick prompt
///   path in <see cref="SpellCastFlow"/> (which populates
///   <see cref="ChosenSpellParams.ModeIndexes"/>).
/// - <b>Escalate—Discard a card</b> (CR 702.121) — an additional cost
///   (CR 601.2f) paid once for EACH mode chosen beyond the first (CR 702.121a):
///   choosing two modes discards one extra card, three modes discards two.
///   Wired via <see cref="SpellDefinition.Escalate"/> as a per-extra-mode
///   <see cref="DiscardACardAdditionalCost"/>; the cast flow pays
///   (modesChosen − 1) of them and rejects the cast atomically (CR 601.2g)
///   when the hand can't cover the extra discards.
///
/// ## Per-mode effects (mapped to existing primitives)
///   Mode 0 — discard-from-opponent's-hand. CR 701.16 reveal +
///     <see cref="IPlayerAgent.ChooseFromHandAsync"/> pick (filtered to
///     instant/sorcery cards) + discard. Mirrors
///     <see cref="ThoughtseizeFactory"/>'s reveal/pick/discard, but the
///     filter is instant-or-sorcery (not "nonland") and there is no life
///     loss. Target slot: <see cref="ChosenSpellParams.Targets"/>[0].
///   Mode 1 — target creature gets -2/-2 until end of turn. CR 613.1g
///     Layer 7c via <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2), expiring
///     at cleanup (CR 514.2). Target slot: Targets[1].
///   Mode 2 — target opponent loses 2 life and you gain 2 life. CR 119.3 —
///     <c>victim.LoseLife(2)</c> + <c>caster.GainLife(2)</c>. Target slot:
///     Targets[2].
///
/// Target requests are emitted for every mode regardless of whether it was
/// chosen (MinTargets = 0 so unchosen modes don't gate the cast — same shape
/// as <see cref="CrypticCommandFactory"/> / <see cref="SimicCharmFactory"/>).
/// Each chosen mode reads its own slot by index.
/// </summary>
[CardName("Collective Brutality")]
public static class CollectiveBrutalityFactory
{
    public const string CardName = "Collective Brutality";
    public const string PrintedManaCost = "{1}{B}";

    public const int ModeDiscard = 0;
    public const int ModeMinusTwoMinusTwo = 1;
    public const int ModeDrain = 2;

    /// <summary>Total number of printed modes (CR 700.2d).</summary>
    public const int TotalModes = 3;

    /// <summary>CR 702.121 — Layer 7c magnitude for mode 1 (CR 613.1g).</summary>
    public const int MinusAmount = -2;

    /// <summary>CR 119.3 — life swing magnitude for mode 2.</summary>
    public const int DrainAmount = 2;

    /// <summary>The printed mode labels, in oracle order.</summary>
    public static IReadOnlyList<string> Modes => new[]
    {
        "Target opponent reveals their hand. You choose an instant or sorcery card from it. That player discards that card.",
        "Target creature gets -2/-2 until end of turn.",
        "Target opponent loses 2 life and you gain 2 life.",
    };

    /// <summary>Construct Collective Brutality as a Sorcery owned by <paramref name="owner"/>.</summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Collective Brutality. "Choose one or
    /// more" (CR 700.2d) over three modes, with Escalate—Discard a card
    /// (CR 702.121) wired as the per-extra-mode additional cost.
    /// </summary>
    /// <param name="caster">Cast-time controller — hosts the agent pick,
    /// the drain life gain, and the reveal reason string.</param>
    /// <param name="targetResolver">Resolver from the caller's GameContext
    /// (chosen target → live game object).</param>
    /// <param name="agent">Optional player-agent for the mode-0 hand pick.
    /// When null the pick falls back to the first instant/sorcery card
    /// (parity with <see cref="ThoughtseizeFactory"/>).</param>
    /// <param name="eventBus">Optional event bus for the mode-0 reveal
    /// (one <c>CardRevealedEvent</c> per card). No-op when null.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        IPlayerAgent? agent = null,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        // CR 601.2c — one target request per mode that takes a target. All
        // three modes are targeted; MinTargets = 0 so UNCHOSEN modes don't
        // gate the cast, but PrintedMinTargets = 1 means a CHOSEN mode demands
        // a legal target or the whole cast is illegal and rewinds (CR 601.2c).
        var targetRequests = new[]
        {
            // Mode 0 — target opponent (reveal + discard).
            new TargetRequest("target opponent", 0, 1, Array.Empty<object>(), BotIntent.HandHate, PrintedMinTargets: 1),
            // Mode 1 — target creature (-2/-2).
            new TargetRequest("target creature", 0, 1, Array.Empty<object>(), BotIntent.Removal, PrintedMinTargets: 1),
            // Mode 2 — target opponent (drain).
            new TargetRequest("target opponent", 0, 1, Array.Empty<object>(), BotIntent.LoseLife, PrintedMinTargets: 1),
        };

        return new SpellDefinition(
            Modes: Modes,
            HasVariableX: false,
            TargetRequests: targetRequests,
            ModeIntents: new[]
            {
                BotIntent.HandHate,
                BotIntent.Removal,
                BotIntent.LoseLife,
            },
            // CR 700.2d — "Choose one or more": between 1 and all 3 modes.
            MinModes: 1,
            MaxModes: TotalModes,
            // CR 702.121 — Escalate—Discard a card. One fresh discard cost per
            // extra mode (agent-driven pick, excluding Collective Brutality
            // itself). The aggregate probe rejects a cast whose extra-mode count
            // exceeds the OTHER cards available to discard (CR 601.2g).
            Escalate: new EscalateSpec(
                Description: "Discard a card",
                BuildPerModeCost: castCard => new EscalateDiscardAdditionalCost(castCard, agent),
                // CR 601.2a — the spell has already moved Hand → Stack (the
                // engine performs the strict-601.2a move for a hand cast before
                // cost determination), so the cast card is NO LONGER in hand and
                // every remaining hand card is a legal escalate discard. The
                // caster therefore needs `extra` cards in hand. (EscalateDiscard-
                // AdditionalCost also filters the cast card defensively, so this
                // stays correct even for a non-hand cast where the spell never
                // sat in hand.)
                CanPayExtra: (player, extra) =>
                    player.Zones.Hand.GetCards().Count() >= extra),
            EffectFactory: p =>
            {
                // CR 700.2d — resolve the chosen modes in PRINTED order
                // (CR 608.2c), de-duplicated, capped at the printed total.
                var indices = p.ModeIndexes is { Count: > 0 } list
                    ? list
                    : (p.ModeIndex.HasValue ? new[] { p.ModeIndex.Value } : Array.Empty<int>());

                var distinct = new SortedSet<int>();
                foreach (var raw in indices)
                {
                    if (raw < 0 || raw >= TotalModes) continue;
                    distinct.Add(raw); // SortedSet → printed order, deduped
                }

                var effects = new List<IEffect>();
                foreach (var raw in distinct) // ascending = printed order
                {
                    switch (raw)
                    {
                        case ModeDiscard:
                            effects.Add(BuildDiscardEffect(caster, p, targetResolver, agent, eventBus));
                            break;
                        case ModeMinusTwoMinusTwo:
                            effects.Add(BuildMinusEffect(p, targetResolver));
                            break;
                        case ModeDrain:
                            effects.Add(BuildDrainEffect(caster, p, targetResolver));
                            break;
                    }
                }
                return effects;
            });
    }

    // -----------------------------------------------------------------------
    // Mode 0: target opponent reveals their hand; you choose an instant or
    // sorcery card from it; that player discards it.
    // -----------------------------------------------------------------------
    private static IEffect BuildDiscardEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver,
        IPlayerAgent? agent,
        IEventBus? eventBus) =>
        new Effect("Collective Brutality — opponent reveals hand; discard an instant/sorcery", () =>
        {
            if (p.Targets.Count <= ModeDiscard) return;
            var slot = p.Targets[ModeDiscard];
            if (slot.Count == 0) return;
            if (resolver(slot[0]) is not Player victim) return;

            // CR 701.16 — reveal the target opponent's hand.
            RevealHelper.RevealHand(eventBus, victim, CardName);

            // CR 700.2 — "You choose an instant or sorcery card from it."
            var eligible = victim.Zones.Hand.GetCards()
                .Where(c => c.HasType(CardType.Instant) || c.HasType(CardType.Sorcery))
                .ToList();
            if (eligible.Count == 0) return; // lands/creatures only → no discard

            ICard? pick = null;
            if (agent != null)
            {
                pick = agent
                    .ChooseFromHandAsync(victim, eligible, BotIntent.HandHate)
                    .GetAwaiter().GetResult();
                if (pick == null
                    || pick.Zone != ZoneType.Hand
                    || !(pick.HasType(CardType.Instant) || pick.HasType(CardType.Sorcery))
                    || !ReferenceEquals(pick.Owner, victim))
                {
                    pick = eligible[0];
                }
            }
            else
            {
                pick = eligible[0];
            }

            // CR 701.16 — "That player discards that card."
            victim.Zones.Hand.RemoveCard(pick);
            victim.Zones.Graveyard.AddCard(pick);
            pick.SetZone(ZoneType.Graveyard);
        });

    // -----------------------------------------------------------------------
    // Mode 1: target creature gets -2/-2 until end of turn.
    // -----------------------------------------------------------------------
    private static IEffect BuildMinusEffect(
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Collective Brutality — target creature gets -2/-2 until end of turn", () =>
        {
            if (p.Targets.Count <= ModeMinusTwoMinusTwo) return;
            var slot = p.Targets[ModeMinusTwoMinusTwo];
            if (slot.Count == 0) return;

            // CR 608.2b — target must still be a creature on the battlefield.
            if (resolver(slot[0]) is not Creature target) return;
            if (target.Zone != ZoneType.Battlefield) return;
            if (target.ActiveEffects == null) return;

            // CR 613.1g Layer 7c — -2/-2 with EOT expiry (CR 514.2).
            target.ActiveEffects.Register(
                new PumpUntilEndOfTurnEffect(target, MinusAmount, MinusAmount));
        });

    // -----------------------------------------------------------------------
    // Mode 2: target opponent loses 2 life and you gain 2 life.
    // -----------------------------------------------------------------------
    private static IEffect BuildDrainEffect(
        Player caster,
        ChosenSpellParams p,
        Func<object, object> resolver) =>
        new Effect("Collective Brutality — target opponent loses 2 life, you gain 2 life", () =>
        {
            if (p.Targets.Count <= ModeDrain) return;
            var slot = p.Targets[ModeDrain];
            if (slot.Count == 0) return;

            // CR 608.2b — target must still be a player. The life gain is
            // tied to the same drain clause: on an illegal target neither
            // half fires (single-target "does nothing" — parity with
            // Thoughtseize's fizzle posture).
            if (resolver(slot[0]) is not Player victim) return;

            victim.LoseLife(DrainAmount);   // CR 119.3
            caster.GainLife(DrainAmount);   // CR 119.3
        });
}
