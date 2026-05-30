using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Werewolf Pack Leader (Innistrad: Midnight Hunt,
/// {G}{G}).
///
/// Creature — Human Werewolf 3/3. Oracle text (verified against Scryfall
/// 2026-05-29):
///   "Pack tactics — Whenever Werewolf Pack Leader attacks, if you attacked
///    with creatures with total power 6 or greater this combat, draw a card.
///    {3}{G}: Until end of turn, Werewolf Pack Leader has base power and
///    toughness 5/3, gains trample, and isn't a Human."
///
/// The base card shape (name / Creature — Human Werewolf / {G}{G} / 3/3) is
/// materialised from the embedded JSON definition (<c>werewolf-pack-leader.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; the two abilities are layered on
/// here because the JSON <c>AbilityDefinition</c> schema models neither an
/// intervening-if attack trigger nor a multi-clause animate ability yet (same
/// posture as <see cref="ArdentPleaFactory"/> / <see cref="RestlessSpireFactory"/>).
///
/// ## Implemented (v1)
/// - <b>3/3 Creature — Human Werewolf</b> at printed cost {G}{G}.
/// - <b>Pack tactics attack trigger (CR 508.1f trigger + CR 603.4
///   intervening-if)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="CreatureAttacksEvent"/> filtered via
///   <see cref="Triggers.OnAttackSelf"/>. The "if you attacked with creatures
///   with total power 6 or greater this combat" clause is the trigger's
///   <see cref="TriggeredAbility.InterveningIf"/> (CR 603.4 — checked both as
///   the trigger would be put on the stack and again on resolution). Total
///   power is read from the injected <c>attackingCreaturesSource</c> closure —
///   the same live-attacker snapshot pattern Exalted uses
///   (<see cref="IgnobleHierarchFactory"/> / <see cref="ArdentPleaFactory"/>) —
///   summing the power of attackers <b>this card's controller</b> declared
///   ("<i>you</i> attacked with creatures"). On resolution the controller
///   draws a card via <see cref="Fx.DrawCards"/>.
/// - <b>{3}{G}: become base 5/3, gain trample, isn't a Human until EOT</b>
///   (CR 602 ordinary activated ability) — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/> of
///   <c>{3}{G}</c>. Resolution registers three end-of-turn-expirable continuous
///   effects against the supplied <see cref="ContinuousEffectsService"/>:
///     - Layer 7b (<see cref="BecomesPTUntilEndOfTurnEffect"/>) — set-base P/T
///       5/3 (CR 613.7b). Werewolf Pack Leader already has the Creature row, so
///       this surfaces directly through
///       <see cref="ContinuousEffectsService.Compute(Permanent)"/> (unlike the
///       manland animate shims).
///     - Layer 6 (<see cref="GrantKeywordUntilEndOfTurnEffect"/>) — gains
///       Trample (CR 702.19).
///     - Layer 4 (<see cref="RemoveSubtypeUntilEndOfTurnEffect"/>) — removes the
///       Human subtype ("isn't a Human", CR 613.1d / CR 205.3). The Werewolf
///       subtype is left intact.
///   All three carry <see cref="ContinuousEffect.ExpiresAtEndOfTurn"/> = true so
///   <see cref="ContinuousEffectsService.ExpireEndOfTurn"/> (CR 514.2 cleanup
///   step) lifts the animation.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. Both abilities attached; the
///   pack-tactics intervening-if reads an empty attacker set (always &lt; 6, so
///   <see cref="TriggeredAbility.CanBePutOnStack"/> is false) and the activate
///   body is a no-op (no effects service). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — wires the
///   continuous-effects service so the activate ability registers its layer
///   effects.
/// - <see cref="Create(Player, TriggerManager?, Func{IReadOnlyList{Creature}}?, ContinuousEffectsService?)"/>
///   — fully wired: the attack trigger registers with the
///   <see cref="TriggerManager"/>, the intervening-if reads the live attacker
///   snapshot, and the activate ability registers its layer effects.
///
/// ## Deferred (v1 gaps)
/// - <b>Live combat attacker snapshot on the production load path</b>: the
///   intervening-if's attacker source is injected. The production binder chain
///   builds the card via <see cref="Create(Player)"/> (no live snapshot), so the
///   pack-tactics draw is observable in tests / EV search but not auto-fed from
///   the combat manager yet — same posture as Exalted's
///   <c>attackingCreaturesSource</c> closure.
/// </summary>
[CardName("Werewolf Pack Leader")]
public static class WerewolfPackLeaderFactory
{
    public const string CardName = "Werewolf Pack Leader";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "werewolf-pack-leader";

    /// <summary>Pack-tactics total-power threshold (CR 603.4 intervening-if).</summary>
    public const int PackTacticsThreshold = 6;

    /// <summary>Base power the {3}{G} ability sets until end of turn.</summary>
    public const int AnimatedPower = 5;

    /// <summary>Base toughness the {3}{G} ability sets until end of turn.</summary>
    public const int AnimatedToughness = 3;

    /// <summary>
    /// Single-arg dispatcher path (used by <see cref="NamedCardFactory"/>).
    /// Attaches both abilities structurally; the pack-tactics intervening-if
    /// reads an empty attacker set and the activate body is a no-op (no
    /// effects service). No TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, attackingCreaturesSource: null, effects: null);

    /// <summary>
    /// Wire the continuous-effects service (so the activate ability registers
    /// its layer effects) without a live attacker snapshot. Used by the
    /// activated-ability tests.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, triggers: null, attackingCreaturesSource: null, effects: effects);

    /// <summary>
    /// Fully-wired construction.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager to register the pack-tactics attack
    /// trigger against. May be null — the trigger is still attached to the card
    /// shape.</param>
    /// <param name="attackingCreaturesSource">Closure returning the current
    /// attacker list, read by the intervening-if to sum the controller's
    /// attacking power ("you attacked with creatures with total power 6 or
    /// greater this combat"). May be null — treated as an empty attacker set,
    /// so the trigger never meets the threshold.</param>
    /// <param name="effects">Continuous-effects service for the {3}{G} ability's
    /// Layer 7b / Layer 6 / Layer 4 registration. May be null — the ability
    /// still resolves but no continuous effect is recorded.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<IReadOnlyList<Creature>>? attackingCreaturesSource,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name / Creature — Human Werewolf / {G}{G} / 3/3) from the
        // embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Creature card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Creature but got "
                + $"'{built.GetType().Name}'.");
        }

        // ----------------------------------------------------------------
        // Pack tactics — "Whenever Werewolf Pack Leader attacks, if you
        // attacked with creatures with total power 6 or greater this combat,
        // draw a card."
        //
        // CR 508.1f — attack trigger keyed on this creature (Triggers.OnAttackSelf).
        // CR 603.4 — "if …" is an intervening-if: checked as the ability would
        //            be put on the stack and again on resolution. Wired as the
        //            trigger's InterveningIf.
        // ----------------------------------------------------------------
        bool PackTacticsConditionMet()
        {
            var attackers = attackingCreaturesSource?.Invoke()
                ?? Array.Empty<Creature>();

            // "you attacked with creatures" — only the controller's attackers
            // count toward the total (CR 508.1 — the active player declares
            // attackers; "you" = this card's controller).
            var total = 0;
            foreach (var atk in attackers)
            {
                if (atk == null) continue;
                if (!ReferenceEquals(atk.Controller, card.Controller)) continue;
                total += atk.GetPower();
            }

            return total >= PackTacticsThreshold;
        }

        var drawEffect = new Effect(
            $"{CardName} — Pack tactics: draw a card (CR 603.4 intervening-if)",
            () =>
            {
                // CR 603.4 — re-check the intervening-if on resolution. If the
                // condition is no longer met (attackers removed in response),
                // the ability does nothing.
                if (!PackTacticsConditionMet()) return;
                Fx.DrawCards(card.Controller ?? owner, 1);
            });

        var packTacticsTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { drawEffect },
            interveningIf: PackTacticsConditionMet,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(packTacticsTrigger);
        triggers?.RegisterTriggeredAbility(packTacticsTrigger);

        // ----------------------------------------------------------------
        // {3}{G}: Until end of turn, Werewolf Pack Leader has base power and
        // toughness 5/3, gains trample, and isn't a Human.
        //
        // CR 602 — ordinary activated ability (uses the stack). Cost = {3}{G},
        // no tap. Resolution registers three EOT-expirable continuous effects.
        // ----------------------------------------------------------------
        var animateEffect = new Effect(
            $"{CardName}: base P/T {AnimatedPower}/{AnimatedToughness}, gains trample, "
            + "isn't a Human until EOT",
            () =>
            {
                if (effects == null) return; // no service wired — shape-only path

                // Layer 7b — set base P/T 5/3 (CR 613.7b).
                effects.Register(new BecomesPTUntilEndOfTurnEffect(
                    card, AnimatedPower, AnimatedToughness));

                // Layer 6 — gains trample (CR 702.19).
                effects.Register(new GrantKeywordUntilEndOfTurnEffect(card, "Trample"));

                // Layer 4 — isn't a Human (CR 613.1d / CR 205.3). Werewolf
                // subtype is left intact.
                effects.Register(new RemoveSubtypeUntilEndOfTurnEffect(
                    card, CardSubtype.Human));
            });

        card.AddAbility(new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost("{3}{G}") },
            effects: new IEffect[] { animateEffect }));

        return card;
    }
}

/// <summary>
/// CR 613.1d / CR 205.3 — Layer 4 subtype-removing effect that strips a single
/// named subtype from a specific permanent until end of turn (CR 514.2).
/// Counterpart of <see cref="SetSubtypesEffect"/> (which rewrites a whole
/// category) for the "isn't a [subtype]" rider where only one subtype is
/// removed and the rest are preserved. Werewolf Pack Leader's "{3}{G}: … isn't
/// a Human" is the canonical caller — only the Human subtype is removed; the
/// Werewolf subtype stays.
/// </summary>
public sealed class RemoveSubtypeUntilEndOfTurnEffect : ContinuousEffect
{
    private readonly Permanent _target;
    private readonly CardSubtype _subtype;

    public RemoveSubtypeUntilEndOfTurnEffect(Permanent target, CardSubtype subtype)
    {
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _subtype = subtype;
    }

    /// <summary>The subtype removed by this effect.</summary>
    public CardSubtype Subtype => _subtype;

    public override Layer Layer => Layer.Type;

    public override Permanent? Source => _target;

    public override bool ExpiresAtEndOfTurn => true;

    public override bool IsActive() =>
        _target.Zone == Majik.Core.Zones.ZoneType.Battlefield;

    public override bool AppliesTo(Creature creature) => ReferenceEquals(creature, _target);

    public override bool AppliesTo(Permanent permanent) => ReferenceEquals(permanent, _target);

    public override void Apply(CreatureCharacteristics chars) =>
        Apply((PermanentCharacteristics)chars);

    public override void Apply(PermanentCharacteristics chars)
    {
        chars.Subtypes.Remove(_subtype);
    }
}
