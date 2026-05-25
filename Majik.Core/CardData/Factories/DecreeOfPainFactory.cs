using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Decree of Pain (Scourge, {6}{B}{B}).
///
/// Sorcery. Oracle text (Scryfall):
///   "Destroy all creatures. They can't be regenerated. For each creature
///    destroyed this way, its controller discards a card.
///    Cycling {3}{B}{B} ({3}{B}{B}, Discard this card: Draw a card.)
///    When you cycle this card, all creatures get -2/-2 until end of turn."
///
/// ## Implemented (v1)
/// - <b>Sorcery {6}{B}{B}</b>.
/// - <b>Resolve body</b> (CR 701.7 + CR 119.5):
///   <see cref="BuildResolveEffect"/> is built on demand and scans every
///   supplied player's battlefield (typically <c>Game.Players</c> since
///   the wrath is symmetric — CR 109.5). Each <see cref="Creature"/>'s
///   controller is snapshotted BEFORE the destroy (CR 112.7a / CR 608.2g
///   — last-known-information once the card leaves the battlefield), the
///   creature is routed to its owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///   <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
///   (CR 701.15 — "They can't be regenerated." rider bypasses the
///   regen-shield consume; indestructible per CR 702.12b still cancels
///   the destroy), and the post-move zone is verified — actually-destroyed
///   creatures whose controller is non-null queue a one-card discard
///   against the snapshotted controller (CR 701.7 + CR 701.12 — "For each
///   creature destroyed this way, its controller discards a card";
///   indestructible / regen-bypass survivors do NOT count, matching the
///   "destroyed this way" gate). Discard implementation deterministically
///   takes the front of the controller's hand (same posture as
///   <c>ResourceSpellFactory.DiscardCards</c>; agent-driven discard-choice
///   prompt deferred behind the discard-prompt pipeline).
/// - <b>Cycling {3}{B}{B}</b> (CR 702.32) — routed through the shared
///   <see cref="CyclingFactory.Build"/> primitive with cycle cost
///   <see cref="ManaCostCost"/>("{3}{B}{B}"). The primitive attaches the
///   <see cref="ActivatedAbility"/> + a <see cref="KeywordAbility"/>
///   "Cycling" marker, layers <see cref="DiscardSelfCost"/> (CR 702.32a
///   hand-zone gate) on the cost stack, and on resolve publishes
///   <see cref="CardCycledEvent"/> for CR 702.32d subscribers.
/// - <b>"When you cycle this card" trigger</b> (CR 702.32d / CR 603.6):
///   wired as a <see cref="TriggeredAbility"/> over
///   <see cref="EventTriggerCondition{CardCycledEvent}"/> gated to
///   <c>ReferenceEquals(e.Card, card)</c> (the printed self-cycle gate —
///   the trigger only fires when THIS Decree is the cycled card,
///   distinct from Curator of Mysteries' "another card" gate).
///   <see cref="TriggeredAbility.ActiveZones"/> = {Graveyard} so the
///   trigger evaluates against the post-cycle zone (the cycling resolve
///   body moves Decree of Pain hand→graveyard before publishing the
///   event). Resolution registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(c, -2, -2) per creature on
///   every supplied player's battlefield against the engine's
///   per-creature <see cref="ContinuousEffectsService"/>
///   (<see cref="Card.ActiveEffects"/>). EOT cleanup runs through the
///   shared layer-system expiry (CR 514.2). Sign-agnostic shape — same
///   mechanism the existing <c>AllCreaturesPumpTemplate</c> uses for the
///   Infest / Nausea / Toxic Deluge family.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Cycling ability
///   attached without an event bus (no <see cref="CardCycledEvent"/>
///   publication); on-cycle trigger attached structurally without
///   <see cref="TriggerManager"/> registration.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. Cycling resolve publishes <see cref="CardCycledEvent"/>
///   against the bus; on-cycle trigger is registered for bus-driven
///   firing. Pair with <see cref="BuildResolveEffect"/> at spell
///   resolution time (caller-supplied <c>allPlayers</c>) and
///   <see cref="BuildCycleEffect"/> at trigger resolution time.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent-driven discard choice</b> — current deterministic
///   front-of-hand discard mirrors every other discard-N primitive
///   today (Mind Rot template, Cabal Therapy without name-match, etc.).
///   The discard-prompt pipeline will route through the controller's
///   <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> once the
///   choose-card-from-hand surface ships.
/// - <b>"When you cycle" stack-on-top ordering</b> (CR 702.32d second
///   sentence — "put on the stack on top of the cycling activated
///   ability") — the engine currently publishes the event AFTER the
///   cycling resolve body runs, so the on-cycle trigger queues onto an
///   empty / nearly-empty stack; mechanically equivalent to "trigger
///   resolves first" for every cycle card in the Modern pool. Same
///   surface every CR 702.32d consumer documents (Lightning Rift,
///   Astral Slide).
///
/// CR rule references: 109.5 (symmetric sweep), 117.5 (mana cost),
/// 514.2 (EOT cleanup), 603.6 (triggered ability), 701.7 (destroy
/// permanent), 701.15 (regenerate / no-regen rider), 702.12b
/// (indestructible), 702.32 (Cycling), 702.32d ("When you cycle" trigger).
/// </summary>
[CardName("Decree of Pain")]
public static class DecreeOfPainFactory
{
    public const string CardName = "Decree of Pain";
    public const string PrintedManaCost = "{6}{B}{B}";
    public const string CyclingCost = "{3}{B}{B}";
    public const int CyclePumpAmount = -2;

    /// <summary>
    /// Construct Decree of Pain with no live wiring. Card shape only —
    /// cycling ability attached without an event bus (no
    /// <see cref="CardCycledEvent"/> publication); on-cycle trigger
    /// attached structurally without <see cref="TriggerManager"/>
    /// registration. Use <see cref="BuildResolveEffect"/> for the
    /// cast-resolve body and <see cref="BuildCycleEffect"/> for the
    /// on-cycle -2/-2 rider.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Decree of Pain. When <paramref name="triggers"/> is
    /// supplied the on-cycle trigger is registered so a self-cycle
    /// <see cref="CardCycledEvent"/> queues the -2/-2 sweep. When
    /// <paramref name="eventBus"/> is supplied the cycling resolve body
    /// publishes <see cref="CardCycledEvent"/> against the bus so the
    /// trigger fires automatically end-to-end.
    /// </summary>
    public static Sorcery Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cycling {3}{B}{B} — CR 702.32. Routed through the shared
        // CyclingFactory primitive; the primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a) and the
        // CardCycledEvent publish (CR 702.32d) automatically.
        // ----------------------------------------------------------------
        CyclingFactory.Build(card, new ManaCostCost(CyclingCost), eventBus);

        // ----------------------------------------------------------------
        // "When you cycle this card, all creatures get -2/-2 until end of
        // turn." (CR 702.32d / CR 603.6)
        //
        // EventTriggerCondition<CardCycledEvent> gated to
        // ReferenceEquals(e.Card, card) — printed self-cycle gate.
        // ActiveZones = {Graveyard} because the cycling resolve body has
        // already routed the Decree hand → graveyard by the time the
        // event is published (CR 702.32a — discard self happens before
        // the post-resolve event publish). Resolution applies the -2/-2
        // sweep to every creature on every supplied player's battlefield
        // via BuildCycleEffect (caller-supplied allPlayers — typically
        // Game.Players since the rider is symmetric, CR 109.5).
        // ----------------------------------------------------------------
        var cycleCondition = new EventTriggerCondition<CardCycledEvent>(
            (e, _) => ReferenceEquals(e.Card, card));

        var cycleTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cycleCondition,
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: all creatures {CyclePumpAmount:+#;-#;0}/{CyclePumpAmount:+#;-#;0} EOT (caller wires via BuildCycleEffect)",
                    () => { /* shape-only; live effect built via BuildCycleEffect */ }),
            },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(cycleTrigger);
        triggers?.RegisterTriggeredAbility(cycleTrigger);

        return card;
    }

    /// <summary>
    /// Build Decree of Pain's cast-resolve body.
    ///
    /// CR 701.7 — destroy every <see cref="Creature"/> on every supplied
    /// player's battlefield with the "can't be regenerated" rider
    /// (<see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    /// — indestructible per CR 702.12b still cancels, regen shield per
    /// CR 701.15c is bypassed). For each creature actually destroyed,
    /// its snapshotted controller discards one card from the front of
    /// their hand (deterministic v1 pick — agent-driven discard-choice
    /// deferred).
    ///
    /// Snapshotting the controller BEFORE the destroy is required per
    /// CR 112.7a — the destroyed creature has no controller once it's
    /// in the graveyard, so the discard target must be captured at
    /// announcement time.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (off-oracle).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: destroy all creatures (no regen) + each controller discards 1 per kill",
                () =>
                {
                    // Snapshot every battlefield up front + capture each
                    // creature's controller (CR 112.7a — controller is
                    // null once the card hits the graveyard, so it has
                    // to be captured before the move).
                    var pairs = new List<(Creature creature, Player controller)>();
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>())
                        {
                            var ctrl = c.Controller ?? c.Owner;
                            if (ctrl == null) continue;
                            pairs.Add((c, ctrl));
                        }
                    }

                    // Destroy with no-regen rider (CR 701.15 — indestructible
                    // still cancels per CR 702.12b; regen shield is bypassed).
                    foreach (var (creature, _) in pairs)
                    {
                        OracleSpellBinder.MoveToGraveyard(
                            creature,
                            Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                    }

                    // Per-kill discard. Only count creatures that
                    // actually moved to the graveyard — indestructible
                    // survivors and any regen-bypassed-but-untouched
                    // edge cases are filtered by the post-move zone
                    // check (CR 701.7 + CR 701.12 "destroyed this way"
                    // gate — the trigger / discard only counts genuine
                    // kills).
                    foreach (var (creature, controller) in pairs)
                    {
                        if (creature.Zone != ZoneType.Graveyard) continue;
                        DiscardOne(controller);
                    }
                }),
        };
    }

    /// <summary>
    /// Build the on-cycle -2/-2 sweep effect.
    ///
    /// CR 702.32d — "When you cycle this card, all creatures get -2/-2
    /// until end of turn." Registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(c, -2, -2) per creature on
    /// every supplied player's battlefield against the engine's
    /// per-creature continuous-effects service (<see cref="Card.ActiveEffects"/>).
    /// Layer 7c modify with EOT expiry (CR 613.4 / CR 514.2 — cleanup
    /// step ends the effect). Same shape every -X/-X sweep uses
    /// (<see cref="Majik.Core.CardData.SpellTemplates.Templates.Counters.AllCreaturesPumpTemplate"/>).
    /// Sign-agnostic — the layer system handles toughness ≤ 0 SBA death
    /// via the standard creature-death check.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the rider
    /// should cover. Typically <c>Game.Players</c> since the printed
    /// rider is symmetric (CR 109.5).</param>
    public static IReadOnlyList<IEffect> BuildCycleEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: all creatures {CyclePumpAmount:+#;-#;0}/{CyclePumpAmount:+#;-#;0} EOT",
                () =>
                {
                    foreach (var pl in allPlayers)
                    {
                        foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>())
                        {
                            if (c.ActiveEffects != null)
                            {
                                c.ActiveEffects.Register(
                                    new PumpUntilEndOfTurnEffect(
                                        c, CyclePumpAmount, CyclePumpAmount));
                            }
                        }
                    }
                }),
        };
    }

    // ----------------------------------------------------------------
    // Internal — front-of-hand discard. Same deterministic shape used
    // by every existing discard-N primitive (Mind Rot template, etc.);
    // agent-driven choice deferred.
    // ----------------------------------------------------------------
    private static void DiscardOne(Player player)
    {
        var top = player.Zones.Hand.GetCards().FirstOrDefault();
        if (top == null) return;
        player.Zones.Hand.RemoveCard(top);
        player.Zones.Graveyard.AddCard(top);
        top.SetZone(ZoneType.Graveyard);
    }
}
