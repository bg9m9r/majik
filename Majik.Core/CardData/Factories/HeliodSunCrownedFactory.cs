using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Heliod, Sun-Crowned (Theros Beyond Death,
/// {1}{W}{W}).
///
/// Legendary Enchantment Creature — God, 5/5. Oracle text (per Scryfall):
///   "Indestructible
///    As long as your devotion to white is less than five, Heliod isn't
///    a creature.
///    Whenever you gain life, put a +1/+1 counter on target creature or
///    enchantment you control.
///    {1}{W}: Another target creature gains lifelink until end of turn."
///
/// ## Implemented (v1)
/// - Legendary Enchantment Creature — God with mana cost {1}{W}{W} and
///   printed P/T 5/5. Multi-type shape: <see cref="Creature"/> shell with
///   <see cref="CardType.Enchantment"/> additively stamped via
///   <see cref="Card.AddCardType"/> (same multi-type pattern as Esika's
///   Chariot / Wurmcoil Engine).
/// - <b>Indestructible (CR 702.12)</b>: <see cref="KeywordAbility"/> marker
///   so SBA 704.5g + the rest of the destroy / regeneration pipeline read
///   it via <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/>
///   — identical wiring to Avacyn / The One Ring / Ulamog.
/// - <b>Lifegain triggered ability (CR 119.3 / 603.6a)</b>: "Whenever you
///   gain life, put a +1/+1 counter on target creature or enchantment you
///   control." Wired via the new <see cref="Triggers.OnLifeGainedByPlayer"/>
///   helper consuming <see cref="LifeChangedEvent"/> (filtered to Heliod's
///   controller and to strictly-positive deltas — life *gain*, not life
///   loss). On resolution the chosen target is rechecked CR 608.2b
///   (still on the controller's battlefield, still a creature OR
///   enchantment) and one
///   <see cref="CounterType.PlusOnePlusOne"/> counter is placed via
///   <see cref="CounterCollection.Add"/>. Single-arg dispatcher path
///   attaches the trigger structurally without
///   <see cref="TriggerManager"/> registration; the
///   <c>(owner, triggers)</c> overload registers for bus-driven firing.
/// - <b>{1}{W}: Another target creature gains lifelink until end of turn
///   (CR 602.1 / 702.15)</b>: a single <see cref="ActivatedAbility"/> with
///   a <see cref="ManaCostCost"/> and a 1..1 "another target creature"
///   <see cref="TargetRequest"/> (the printed "Another" is encoded as a
///   resolve-time identity check against the source — same posture as the
///   rest of the engine's printed "another" predicates that gate at
///   resolve rather than at choose-time, since
///   <see cref="TargetRequest.LegalCandidates"/> is empty by default for
///   the Solitude / Earthshaker Khenra family). On resolution the chosen
///   creature gets a <see cref="GrantKeywordUntilEndOfTurnEffect"/> for
///   Lifelink (Layer 6, EOT-expirable per CR 514.2) registered against
///   its <see cref="Creature.ActiveEffects"/> service — identical shape to
///   Guide of Souls' Flying grant.
/// - <b>Devotion-to-white check (CR 700.5)</b>: "As long as your devotion
///   to white is less than five, Heliod isn't a creature." Heliod's
///   devotion-to-white reads as a live aggregate of the controller's
///   battlefield via <see cref="ComputeDevotionToWhite"/> — every permanent
///   the controller controls contributes its <c>ManaCostValue.White</c>
///   count of {W} pips (CR 700.5 is the canonical phrasing; hybrid /
///   Phyrexian pips that include {W} are counted as white per CR 700.5a —
///   for v1 we read the parsed <see cref="ValueObjects.ManaCost.White"/>
///   field directly, which is the pure-{W} count and excludes hybrid /
///   Phyrexian {W} contributions). Heliod itself contributes 2 (its own
///   cost has two {W} pips), so two more white permanents are typically
///   enough to flip him on. <see cref="ComputeDevotionToWhite"/> is
///   exposed publicly so tests + bots can inspect the live count
///   directly.
/// - <b>Layer 4 devotion-gated type-strip (CR 205.2 / 613.1d)</b>: when
///   the <c>(owner, triggers, effects)</c> overload is invoked with a
///   <see cref="ContinuousEffectsService"/>, a
///   <see cref="Layer4TypeStripEffect"/> is registered on Heliod with
///   predicate <c>ComputeDevotionToWhite(controller) &lt; 5</c>. The
///   predicate is re-evaluated on every layer-system Compute, so
///   devotion bumps / drops flip Heliod's effective Creature type
///   without re-registering the effect. While the predicate is true,
///   Heliod's layered characteristics drop Creature — he can't be
///   targeted by creature-only spells, can't attack, and can't be
///   declared as a blocker.
///
/// ## Deferred (v1 gaps)
/// - <b>Hybrid / Phyrexian {W} contributions</b>: CR 700.5a counts every
///   mana symbol in a permanent's mana cost that includes {W} toward
///   devotion to white. v1 reads
///   <see cref="ValueObjects.ManaCost.White"/> only — pure {W} pips. The
///   <see cref="ValueObjects.ManaCost"/> value object doesn't yet carry
///   hybrid / Phyrexian buckets; closing the gap is paired with the same
///   plumbing the hybrid-mana-cost templates already document (e.g.
///   Boros Reckoner's {R/W} cost parsing).
/// - <b>"Another target creature" choose-time exclusion</b>: the printed
///   "Another" is enforced at resolve via the source-identity check, not
///   at choose-time (CR 608.2b posture matching Solitude / Earthshaker
///   Khenra). Choose-time filtering depends on
///   <see cref="TargetRequest.LegalCandidates"/> being populated by a
///   live battlefield gather — same deferred plumbing the rest of the
///   target-restricted family lives with.
/// - <b>Agent-driven target prompt</b>: the lifegain trigger + activated
///   ability honour pre-set
///   <see cref="ITriggeredAbility.ChosenTargets"/> /
///   <see cref="ActivatedAbility.ChosenTargets"/>; the factory does NOT
///   wire an <see cref="IPlayerAgent"/> prompt. Tests call
///   <see cref="TriggeredAbility.SetChosenTargets"/> /
///   <see cref="ActivatedAbility.SetChosenTargets"/> directly (same
///   posture as Earthshaker Khenra / Guide of Souls).
/// </summary>
[CardName("Heliod, Sun-Crowned")]
public static class HeliodSunCrownedFactory
{
    public const string CardName = "Heliod, Sun-Crowned";
    public const string PrintedManaCost = "{1}{W}{W}";
    public const int Power = 5;
    public const int Toughness = 5;
    public const string LifelinkActivationCost = "{1}{W}";
    public const int DevotionToWhiteThreshold = 5;

    /// <summary>
    /// Construct Heliod, Sun-Crowned. The lifegain trigger + activated
    /// ability are attached to the card shape but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for card-shape / dispatcher
    /// tests — tests fire the triggered ability by invoking its effect
    /// directly.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null, effects: null);

    /// <summary>
    /// Trigger-manager-only overload (kept for back-compat with callers
    /// that wire triggers without a layer-system service). Equivalent to
    /// <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?)"/>
    /// with <c>effects: null</c>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
        => Create(owner, triggers, effects: null);

    /// <summary>
    /// Construct Heliod, Sun-Crowned with the lifegain trigger registered
    /// against <paramref name="triggers"/> when supplied, and the Layer 4
    /// devotion-gated type-strip registered against
    /// <paramref name="effects"/> when supplied.
    ///
    /// <para>When <paramref name="effects"/> is non-null:</para>
    /// <list type="bullet">
    ///   <item>The service is stamped onto Heliod's
    ///   <see cref="Creature.ActiveEffects"/> so downstream P/T + type
    ///   lookups route through the layer system.</item>
    ///   <item>A <see cref="Layer4TypeStripEffect"/> is registered with
    ///   predicate <c>ComputeDevotionToWhite(controller) &lt; 5</c>
    ///   (CR 205.2 / 613.1d). While the predicate is true, Heliod's
    ///   layered characteristics drop the Creature type — he can't be
    ///   targeted by creature-only spells, can't attack, can't block.
    ///   When devotion bumps to 5+, the predicate flips false and
    ///   Heliod's printed Creature type surfaces again.</item>
    /// </list>
    ///
    /// Lifelink-grant activated ability is structurally identical across
    /// all overloads; it consults the target creature's
    /// <see cref="Creature.ActiveEffects"/> on resolve (silent no-op when
    /// missing, matching Guide of Souls' posture).
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.God });

        // Multi-type: Heliod is both an Enchantment AND a Creature.
        // CR 301.1 / 302.1 — additive type via Card.AddCardType (same
        // pattern as Esika's Chariot's Artifact + Creature shape).
        card.AddCardType(CardType.Enchantment);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12) — marker only; SBA reads via
        // CombatAbilities.HasIndestructible (same wiring shape as
        // Avacyn / The One Ring / Ulamog).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // ----------------------------------------------------------------
        // Lifegain triggered ability — CR 119.3 / 603.6a.
        //   "Whenever you gain life, put a +1/+1 counter on target
        //    creature or enchantment you control."
        // Wired via Triggers.OnLifeGainedByPlayer — fires on
        // LifeChangedEvent filtered to Heliod's controller AND
        // strictly-positive deltas (NewLife > PreviousLife). Resolution
        // recheck (CR 608.2b): chosen target is still on the controller's
        // battlefield and is still either a Creature or an Enchantment.
        // ----------------------------------------------------------------
        TriggeredAbility? lifegainTrigger = null;
        var lifegainEffect = new Effect(
            $"{CardName}: place a +1/+1 counter on target creature or enchantment you control",
            () =>
            {
                if (lifegainTrigger == null) return;
                var chosen = lifegainTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Permanent target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
                if (!ReferenceEquals(target.Controller, card.Controller)) return;
                if (!target.HasType(CardType.Creature)
                    && !target.HasType(CardType.Enchantment)) return;

                target.Counters.Add(CounterType.PlusOnePlusOne, 1);
            });

        lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature or enchantment you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.
        //   "{1}{W}: Another target creature gains lifelink until end of
        //    turn."
        // Cost: ManaCostCost("{1}{W}"). "Another" enforced at resolve via
        // identity check vs source (CR 608.2b posture — same as Solitude /
        // Earthshaker Khenra's deferred choose-time filter).
        // ----------------------------------------------------------------
        ActivatedAbility? lifelinkGrantAbility = null;
        var lifelinkGrantEffect = new Effect(
            $"{CardName}: another target creature gains Lifelink until end of turn",
            () =>
            {
                if (lifelinkGrantAbility == null) return;
                var chosen = lifelinkGrantAbility.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return; // CR 608.2b
                if (ReferenceEquals(target, card)) return;      // "Another"
                if (target.ActiveEffects == null) return;       // shape-only no-op

                // CR 613.1c Layer 6 — keyword grant (Lifelink), EOT-expirable.
                target.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, "Lifelink"));
            });

        lifelinkGrantAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(LifelinkActivationCost) },
            effects: new IEffect[] { lifelinkGrantEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Buff),
            });

        card.AddAbility(lifelinkGrantAbility);

        // ----------------------------------------------------------------
        // Layer 4 devotion-gated type-strip — CR 205.2 / 613.1d.
        //   "As long as your devotion to white is less than five, Heliod
        //    isn't a creature."
        // Registers a Layer4TypeStripEffect on the supplied service with
        // a live devotion predicate. Predicate is re-evaluated on every
        // Compute pass, so devotion bumps (cast another white permanent)
        // / drops (white permanent LTB's) flip Heliod's effective
        // Creature type without re-registering the effect. Effect is
        // source-anchored — when Heliod LTB's, the effect ends.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            effects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () =>
                    ComputeDevotionToWhite(card.Controller!) < DevotionToWhiteThreshold));
        }

        return card;
    }

    /// <summary>
    /// CR 700.5 — devotion to white. Sum of {W} mana symbols among the
    /// mana costs of permanents <paramref name="player"/> controls.
    ///
    /// v1 reads pure-{W} pips only via
    /// <see cref="ValueObjects.ManaCost.White"/>; hybrid / Phyrexian {W}
    /// contributions are DEFERRED (CR 700.5a — every mana symbol that
    /// includes {W} counts) pending the
    /// <see cref="ValueObjects.ManaCost"/> hybrid-bucket retrofit.
    ///
    /// Tokens count when their token-spec mana cost carries {W} pips
    /// (Soldier tokens with cost <c>""</c> contribute 0; spirit tokens
    /// minted from white-coloured spells contribute 0 unless the spec was
    /// stamped with a parsed mana cost — same gap as the rest of the
    /// token / colour-identity surface).
    ///
    /// Exposed publicly so bots / tests can read the live count without
    /// going through the (deferred) Layer-4 type-strip path.
    /// </summary>
    public static int ComputeDevotionToWhite(Player player)
    {
        if (player == null) return 0;
        var total = 0;
        foreach (var perm in player.Zones.Battlefield.GetCards())
        {
            if (perm is Card concrete)
            {
                total += concrete.ManaCostValue.White;
            }
        }
        return total;
    }
}
