using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Voice of Victory (Tarkir: Dragonstorm, {1}{W}).
///
/// Creature — Human Bard, 2/2. Oracle text:
///   "Mobilize 2 (Whenever this creature attacks, create two tapped and
///    attacking 1/1 red Warrior creature tokens. Sacrifice them at the
///    beginning of the next end step.)
///    Your opponents can't cast spells during your turn."
///
/// ## Implemented (v1)
/// - 2/2 white Human Bard at {1}{W}, owner / controller wired (CR 105 —
///   white from the {W} pip).
/// - <b>Static "Your opponents can't cast spells during your turn"
///   (CR 601.3)</b> — the same total-cast-block shape as Grand Abolisher
///   (minus the activated-ability clause; Voice blocks <i>spells only</i>).
///   Wired through the new
///   <see cref="CastingRestrictions.AddCannotCastAnySpell"/> rail: when an
///   <see cref="IEventBus"/> + opponent resolver are supplied, a
///   <see cref="TurnStartedEvent"/> handler registers the total-cast block
///   against every opponent at the start of the controller's turn, and a
///   <see cref="TurnEndedEvent"/> handler tears it down at end of the
///   controller's turn (CR 514.2 — the "during your turn" window). The
///   block is keyed by the card token so multiple sources stack without
///   trampling, and is consulted by
///   <see cref="ActionValidator.ValidateCastSpell"/> for every cast
///   (creature and noncreature alike). Without an event bus the caller
///   manages the window (matching Ranger-Captain of Eos's posture).
/// - <b>Mobilize 2 (CR 508.3g)</b> — an
///   <see cref="Triggers.OnAttackSelf"/> <see cref="TriggeredAbility"/>
///   that, on resolution:
///     1. Creates two 1/1 red Warrior creature tokens via
///        <see cref="TokenFactory.CreateOnBattlefield"/>.
///     2. Splices each token into the in-progress combat as a token that
///        is already <b>tapped and attacking</b> the same defender as Voice
///        of Victory, via the new
///        <see cref="CombatManager.AddTappedAndAttackingToken"/> combat
///        primitive (CR 508.3 — enters tapped; CR 508.4 — attacking the
///        same player/planeswalker). Because the tokens are "put onto the
///        battlefield attacking" rather than "declared" as attackers, they
///        do NOT re-trigger Mobilize or other "whenever a creature attacks"
///        abilities (CR 508.3g).
///     3. Registers a <see cref="DelayedTriggeredAbility"/> (CR 603.7) that
///        sacrifices the two tokens at the start of the next end step
///        (CR 500.4 / CR 701.16). The trigger fence-checks
///        <c>e.Timestamp &gt; resolvedAt</c> so the current end step (if
///        any) doesn't trip it (mirrors Sneak Attack / Through the Breach).
///
/// ## Deferred (v1 gaps)
/// - <b>Summoning-sickness on the spliced tokens</b>: the printed tokens
///   enter "tapped and attacking" and are exempt from summoning sickness
///   for the purpose of attacking this turn (they're already attacking).
///   <see cref="TokenFactory.CreateOnBattlefield"/> stamps
///   <c>HasSummoningSickness = true</c>; the combat splice does not consult
///   that flag (the tokens are in the attacker set regardless), so combat
///   damage this turn is correct. Activated-ability / tap-cost uses of the
///   tokens this turn would incorrectly see them as sick — a follow-up can
///   clear the flag when splicing. Same posture as Geist of Saint Traft's
///   Angel note.
/// - <b>No-combat fallback</b>: if the Mobilize trigger somehow resolves
///   with no combat in progress (no <see cref="CombatManager"/> supplied,
///   or combat already ended), the tokens still enter the battlefield
///   (untapped, not attacking) — the "tapped and attacking" fidelity
///   requires a live combat to splice into.
/// </summary>
[CardName("Voice of Victory")]
public static class VoiceOfVictoryFactory
{
    public const string CardName = "Voice of Victory";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>Mobilize 2 — two 1/1 red Warrior tokens per attack.</summary>
    public const int MobilizeCount = 2;
    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Construct Voice of Victory with no live runtime wiring. The Mobilize
    /// trigger is attached to the card shape; its resolution creates plain
    /// battlefield tokens (no combat splice, no delayed sac) and the static
    /// is inert (no opponent resolver / event bus). Suitable for dispatcher /
    /// shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, opponentResolver: null, eventBus: null, triggers: null, combat: null);

    /// <summary>
    /// Construct Voice of Victory with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="opponentResolver">When supplied alongside an event bus,
    /// each opponent it returns is barred from casting spells during the
    /// controller's turn (CR 601.3). Pass <c>null</c> to leave the static
    /// inert (shape-only).</param>
    /// <param name="eventBus">When supplied, drives the "during your turn"
    /// window for the cast-block static via <see cref="TurnStartedEvent"/> /
    /// <see cref="TurnEndedEvent"/>.</param>
    /// <param name="triggers">When supplied, the Mobilize attack trigger is
    /// registered so a <see cref="CreatureAttacksEvent"/> for Voice of
    /// Victory lands it on the stack automatically, and the delayed
    /// end-step sacrifice is registered.</param>
    /// <param name="combat">When supplied, the Mobilize tokens are spliced
    /// into the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    public static Creature Create(
        Player owner,
        Func<IReadOnlyList<Player>>? opponentResolver,
        IEventBus? eventBus,
        TriggerManager? triggers,
        CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Bard });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // Static: "Your opponents can't cast spells during your turn."
        // CR 601.3 — total cast block, scoped to the controller's turn.
        // Register the block against each opponent when the controller's turn
        // begins; remove it when the controller's turn ends.
        // --------------------------------------------------------------------
        if (eventBus != null && opponentResolver != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(e =>
            {
                if (!ReferenceEquals(e.Player, card.Controller ?? owner)) return;
                var opponents = opponentResolver();
                if (opponents == null) return;
                foreach (var opp in opponents)
                {
                    if (opp == null) continue;
                    CastingRestrictions.AddCannotCastAnySpell(card, opp);
                }
            });

            eventBus.Subscribe<TurnEndedEvent>(e =>
            {
                if (!ReferenceEquals(e.Player, card.Controller ?? owner)) return;
                CastingRestrictions.RemoveCannotCastAnySpell(card);
            });
        }

        // --------------------------------------------------------------------
        // Mobilize 2 (CR 508.3g): "Whenever this creature attacks, create two
        // tapped and attacking 1/1 red Warrior creature tokens. Sacrifice
        // them at the beginning of the next end step."
        // --------------------------------------------------------------------
        var mobilizeEffect = new Effect(
            $"{CardName}: Mobilize {MobilizeCount} — create {MobilizeCount} tapped & attacking 1/1 red Warriors",
            () => ResolveMobilize(card, owner, combat, triggers));

        var mobilizeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { mobilizeEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(mobilizeTrigger);
        triggers?.RegisterTriggeredAbility(mobilizeTrigger);

        return card;
    }

    /// <summary>
    /// Resolve Mobilize 2 — create the tokens, splice them into combat tapped
    /// and attacking, and register the end-step sacrifice.
    /// </summary>
    private static void ResolveMobilize(
        Creature source,
        Player owner,
        CombatManager? combat,
        TriggerManager? triggers)
    {
        var controller = source.Controller ?? owner;

        // CR 111.4 — two 1/1 red Warrior creature tokens.
        var spec = new TokenFactory.TokenSpec(
            Name: "Warrior",
            Power: TokenPower,
            Toughness: TokenToughness,
            Subtypes: new[] { CardSubtype.Warrior },
            Keywords: null,
            Colors: new[] { ManaColor.Red });

        var tokens = new List<Creature>(MobilizeCount);
        for (int i = 0; i < MobilizeCount; i++)
        {
            var token = TokenFactory.CreateOnBattlefield(spec, controller);
            tokens.Add(token);

            // CR 508.3g — splice the token into the in-progress combat as a
            // tapped and attacking token (the core Mobilize primitive). When
            // no combat is live the token stays on the battlefield untapped
            // (see class xmldoc no-combat fallback).
            combat?.AddTappedAndAttackingToken(token);
        }

        // CR 603.7 — sacrifice the tokens at the start of the next end step.
        if (triggers == null) return;
        RegisterEndStepSacrifice(source, controller, tokens, triggers);
    }

    /// <summary>
    /// CR 603.7 / 500.4 — register a one-shot delayed trigger that sacrifices
    /// the Mobilize tokens at the start of the next end step. The trigger
    /// fence-checks <c>e.Timestamp &gt; resolvedAt</c> so the current end step
    /// (if Mobilize resolves during an end step) doesn't trip it (mirrors
    /// Sneak Attack / Through the Breach).
    /// </summary>
    private static void RegisterEndStepSacrifice(
        Creature source,
        Player controller,
        IReadOnlyList<Creature> tokens,
        TriggerManager triggers)
    {
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();

        var sacEffect = new Effect(
            $"{CardName}: sacrifice {tokens.Count} Mobilize tokens at next end step",
            () =>
            {
                foreach (var token in tokens)
                {
                    if (token.Zone != ZoneType.Battlefield) continue;
                    var bfPlayer = token.Controller ?? controller;
                    if (!bfPlayer.Zones.Battlefield.GetCards().Contains(token)) continue;

                    // CR 701.16 — sacrifice: controller's battlefield → owner's
                    // graveyard. (Tokens cease to exist as an SBA after this
                    // zone change — CR 111.7 / 704.5d — handled by the engine's
                    // SBA pass; this move performs the sacrifice itself.)
                    var graveyardOwner = token.Owner ?? controller;
                    bfPlayer.Zones.Battlefield.RemoveCard(token);
                    graveyardOwner.Zones.Graveyard.AddCard(token);
                    token.SetZone(ZoneType.Graveyard);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: source,
            controller: controller,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == Majik.Core.StateMachine.PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
