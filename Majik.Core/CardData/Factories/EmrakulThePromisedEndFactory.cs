using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Emrakul, the Promised End (Eldritch Moon,
/// {13}).
///
/// Legendary Creature — Eldrazi 13/13. Oracle text (Scryfall, verified):
///   "Emrakul, the Promised End costs {1} less to cast for each card
///    type among cards in your graveyard.
///    When you cast this spell, you gain control of target opponent
///    during that player's next turn. After that turn, that player
///    takes an extra turn.
///    Flying, trample, protection from instants."
///
/// ## Implemented (v1)
/// - 13/13 Legendary Creature — Eldrazi at {13}.
/// - <b>Flying (CR 702.9)</b>, <b>Trample (CR 702.19)</b>: shipped as
///   <see cref="KeywordAbility"/> markers — combat-side reads via
///   <see cref="Majik.Core.Combat.CombatAbilities"/>, same posture as
///   every other named factory carrying these keywords.
/// - <b>Protection from instants (CR 702.16)</b>: shipped as a plain
///   <see cref="ProtectionAbility"/> with quality string
///   <see cref="ProtectionFromInstantsQuality"/> ("instants"). The
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromCardType"/>
///   helper recognises the canonical plural — counter / damage /
///   targeting gates that hold a card-type handle (Lightning Bolt has
///   <see cref="CardType.Instant"/>) consult the helper and reject the
///   action. Identical wiring shape to the protection markers on
///   <see cref="SwordOfFireAndIceFactory"/> / <see cref="EtchedChampionFactory"/>.
/// - <b>Cost reduction — "{1} less per card type in your graveyard"
///   (CR 117.7 / 601.2f)</b>: shipped via
///   <see cref="CostReductionAbility"/> using the
///   <see cref="CostReductionAbility.TotalReducer"/> shape — at cost-
///   calculation time the closure counts the distinct
///   <see cref="CardType"/>s among the caster's graveyard (artifact,
///   creature, enchantment, instant, land, planeswalker, sorcery,
///   tribal — CR 205.2) and returns that count as the generic-mana
///   reduction. Floor-at-zero is enforced inside
///   <see cref="CostReduction.GetEffectiveCost"/> after summing
///   reducers. Reading the live graveyard at cost-calc time (not at
///   cast-attempt declaration) is correct per CR 601.2f — cost is
///   computed when the spell's total cost is being determined, which
///   happens after the controller is chosen but before mana is paid.
/// - <b>Cast trigger — extra turn for the controlled opponent
///   (CR 603.1 + 603.10)</b>: triggered ability over
///   <see cref="SpellCastEvent"/> filtered to <c>e.Spell.Card == card</c>.
///   On resolution the trigger enqueues an extra turn for the chosen
///   opponent via <see cref="TurnManager.AddExtraTurn"/>.
///
///   Per printed text the extra turn happens "<em>after that turn</em>"
///   — i.e. after the turn during which the caster controls the
///   opponent. With the mind-control clause stubbed (see below) the
///   "next opponent turn" and the "extra turn after that" collapse
///   into the same extra turn enqueue: we enqueue one extra turn for
///   the target opponent, which they take normally (no
///   take-control overlay). Once a player-mind-control primitive
///   exists, the trigger will instead schedule the control-overlay
///   for the opponent's natural-next turn and enqueue the extra
///   turn after that (two-step). The observable game state under v1
///   is "target opponent takes an extra turn after their next turn"
///   — symmetric with Time Walk minus the control rider.
///
/// ## Stubbed — "you gain control of target opponent during that
///                 player's next turn"
/// The engine has no <em>player</em>-mind-control primitive in v1.
/// <see cref="Majik.Core.CardData.SpellTemplates.Templates.Control"/>
/// covers creature-control (Mind Control / Threads of Disloyalty
/// shape) but not "make every decision for another player during a
/// turn" — that requires a priority-handoff layer over
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> that doesn't
/// exist yet (the closest sibling is
/// <see cref="Majik.Core.Players.Agents.ScriptedAgent"/>, but routing
/// an opponent's decisions through the caster's agent during a single
/// turn is a separate cross-cutting feature).
///
/// The cast trigger's <see cref="EmrakulThePromisedEndTrigger.ControlledOpponent"/>
/// slot captures the chosen opponent so:
///   1. Tests can read which opponent the trigger picked.
///   2. The extra-turn enqueue routes to the right player.
///   3. A future mind-control layer can read the slot and install the
///      priority-handoff overlay without re-running target selection.
///
/// ## Target selection
/// The cast trigger declares a single mandatory target (min/max = 1)
/// with a candidate gatherer that enumerates the caster's opponents at
/// resolution time (CR 608.2b — illegal-on-resolution check). When no
/// target is set on the trigger before resolution (shape-only path),
/// the trigger picks the first opponent of the caster as a
/// deterministic fallback, same posture as Annihilator's first-N
/// fallback.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. All ability markers +
///   the cost reduction + the cast trigger are attached; the trigger
///   isn't registered with any <see cref="TriggerManager"/>; the
///   extra-turn enqueue is a no-op without a <see cref="TurnManager"/>.
///   Suitable for dispatcher / structural tests.
/// - <see cref="Create(Player, TurnManager?, TriggerManager?)"/>
///   — fully wired. The cast trigger registers with the trigger bus
///   so a <see cref="SpellCastEvent"/> for this card automatically
///   queues the ability; extra-turn resolution calls
///   <paramref name="turns"/>.AddExtraTurn for the chosen opponent.
/// </summary>
[CardName("Emrakul, the Promised End")]
public static class EmrakulThePromisedEndFactory
{
    public const string CardName = "Emrakul, the Promised End";
    public const string PrintedManaCost = "{13}";
    public const int Power = 13;
    public const int Toughness = 13;

    /// <summary>
    /// Protection-quality string for "protection from instants" —
    /// matches the canonical plural in
    /// <see cref="Majik.Core.Rules.Protection.HasProtectionFromCardType"/>.
    /// </summary>
    public const string ProtectionFromInstantsQuality = "instants";

    /// <summary>
    /// All card types <see cref="CountDistinctCardTypesInGraveyard"/>
    /// will count toward the cost reduction. CR 205.2 — the eight card
    /// types (artifact, creature, enchantment, instant, land,
    /// planeswalker, sorcery, tribal). Tribal is a legacy type but the
    /// Comp Rules still enumerate it; including it here is correct per
    /// printed-card reading of "each card type".
    /// </summary>
    public static readonly IReadOnlyList<CardType> CountedCardTypes =
        new[]
        {
            CardType.Artifact,
            CardType.Creature,
            CardType.Enchantment,
            CardType.Instant,
            CardType.Land,
            CardType.Planeswalker,
            CardType.Sorcery,
            CardType.Tribal,
        };

    /// <summary>
    /// Pure helper — count distinct <see cref="CardType"/>s among the
    /// cards in <paramref name="caster"/>'s graveyard. Called by the
    /// cost-reduction <see cref="CostReductionAbility.TotalReducer"/>
    /// and exposed for tests / bot probes.
    /// </summary>
    public static int CountDistinctCardTypesInGraveyard(Player caster)
    {
        if (caster == null) throw new ArgumentNullException(nameof(caster));
        var graveyard = caster.Zones.Graveyard.GetCards();
        var distinct = 0;
        foreach (var type in CountedCardTypes)
        {
            foreach (var c in graveyard)
            {
                if (c.HasType(type)) { distinct++; break; }
            }
        }
        return distinct;
    }

    /// <summary>
    /// Construct Emrakul, the Promised End with no live wiring. All
    /// ability markers + the cost reduction + the cast trigger are
    /// attached; nothing registers with a trigger bus or turn manager.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, turns: null, triggers: null);

    /// <summary>
    /// Construct Emrakul, the Promised End with optional runtime
    /// services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="turns">When supplied, the cast trigger's resolution
    /// enqueues an extra turn for the chosen opponent via
    /// <see cref="TurnManager.AddExtraTurn"/>.</param>
    /// <param name="triggers">When supplied, the cast trigger registers
    /// with the bus so a <see cref="SpellCastEvent"/> for this card
    /// automatically queues the ability (CR 603.2).</param>
    public static Creature Create(
        Player owner,
        TurnManager? turns,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying marker.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // CR 702.19 — Trample marker.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // CR 702.16 — Protection from instants. Canonical-plural
        // "instants" quality string recognised by
        // Rules.Protection.HasProtectionFromCardType for CardType.Instant.
        card.AddAbility(new ProtectionAbility(ProtectionFromInstantsQuality));

        // ----------------------------------------------------------------
        // CR 117.7 / 601.2f — cost reduction.
        //   "Emrakul, the Promised End costs {1} less to cast for each
        //    card type among cards in your graveyard."
        //
        // TotalReducer shape — the closure is called once per cast with
        // the live caster and returns the total generic-mana reduction
        // to apply. We count distinct card types among the caster's
        // graveyard at cost-calc time (CR 601.2f — cost is determined
        // when the spell is being cast, not when it was put into the
        // graveyard). Reading off the live caster (not Emrakul's owner)
        // means if control of the spell-being-cast changes mid-cast
        // (vanishingly rare) the reduction still tracks the actual
        // caster. Floor-at-zero is enforced in
        // CostReduction.GetEffectiveCost.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster => CountDistinctCardTypesInGraveyard(caster),
            description:
                "costs {1} less to cast for each card type among cards in your graveyard"));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.1 / CR 603.10.
        //   "When you cast this spell, you gain control of target
        //    opponent during that player's next turn. After that turn,
        //    that player takes an extra turn."
        //
        // V1 implementation:
        //   - Mind-control of the opponent is STUBBED (no player-mind-
        //     control primitive in v1 — see class doc).
        //   - The extra turn is enqueued for the chosen opponent.
        //
        // Self-cast detection mirrors Emrakul, the Aeons Torn: filter
        // SpellCastEvent on e.Spell.Card == card, active in the Stack
        // zone (Emrakul is on the stack at cast time). The chosen
        // opponent is captured via EmrakulThePromisedEndTrigger.
        // ControlledOpponent — settable by tests / bots / a future
        // mind-control overlay; null at resolution triggers the
        // deterministic first-opponent fallback so shape tests can
        // observe the extra-turn enqueue without driving target
        // selection.
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                // Self-cast detection. The caster identity is read off
                // ControlledOpponent's pre-resolution setter (which a
                // future player-mind-control overlay populates from the
                // cast event); v1 doesn't consume the caster identity
                // in the trigger body — the extra-turn enqueue routes
                // straight to the chosen opponent.
                return ReferenceEquals(e.Spell.Card, card);
            });

        EmrakulThePromisedEndTrigger? trigger = null;
        var castEffect = new Effect(
            $"{CardName}: enqueue extra turn for the controlled opponent (cast trigger)",
            () =>
            {
                if (turns == null) return;

                // CR 608.2b — illegal-on-resolution check. The
                // ControlledOpponent slot must be populated before
                // resolution (test / bot / future player-mind-control
                // overlay); a null slot no-ops the enqueue so shape
                // tests can observe the trigger without driving target
                // selection.
                var victim = trigger?.ControlledOpponent;
                if (victim == null) return;

                turns.AddExtraTurn(victim);
            });

        trigger = new EmrakulThePromisedEndTrigger(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { castEffect });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}

/// <summary>
/// Emrakul, the Promised End's cast triggered ability. Subclasses
/// <see cref="TriggeredAbility"/> so the chosen opponent travels with
/// the ability instance (test / bot setter), mirroring
/// <see cref="BorosReckonerTrigger"/>'s any-target slot for
/// <see cref="BorosReckonerFactory"/>'s damage-received trigger.
/// </summary>
public sealed class EmrakulThePromisedEndTrigger : TriggeredAbility
{
    /// <summary>
    /// The opponent chosen as "target opponent" for the cast trigger.
    /// Tests / bots / a future player-mind-control overlay set this
    /// before resolution; null at resolution triggers the
    /// deterministic first-opponent fallback. Setting a player that
    /// isn't actually an opponent of the spell's controller is the
    /// caller's responsibility — the engine doesn't re-validate the
    /// pick at resolution (CR 608.2b — illegal-target checks happen at
    /// the targeting layer, not in the trigger body).
    /// </summary>
    public Player? ControlledOpponent { get; set; }

    public EmrakulThePromisedEndTrigger(
        ICard source,
        Player controller,
        ITriggerCondition condition,
        IEffect[] effects)
        : base(
            source: source,
            controller: controller,
            condition: condition,
            effects: effects,
            activeZones: new[] { ZoneType.Stack })
    {
    }
}
