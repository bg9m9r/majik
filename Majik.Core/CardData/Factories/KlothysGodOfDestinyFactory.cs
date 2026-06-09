using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Klothys, God of Destiny (Theros Beyond Death,
/// {1}{R}{G}).
///
/// Legendary Enchantment Creature — God, 4/5. Oracle text (verified against
/// Scryfall):
///   "Indestructible
///    As long as your devotion to red and green is less than seven, Klothys
///    isn't a creature.
///    At the beginning of your first main phase, exile target card from a
///    graveyard. If it was a land card, add {R} or {G}. Otherwise, you gain
///    2 life and Klothys deals 2 damage to each opponent."
///
/// The base shape (name, Legendary supertype, God subtype, multi-type
/// Enchantment + Creature, {1}{R}{G}, 4/5) is materialised from the embedded
/// JSON definition (<c>klothys-god-of-destiny.json</c>, whose
/// <c>"types": ["Creature", "Enchantment"]</c> drives the multi-type
/// shape — same pattern as <see cref="EnduringCuriosityFactory"/>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The printed behaviours
/// (Indestructible, the devotion-gated type-strip, the first-main-phase
/// trigger) are layered on here — same posture as
/// <see cref="HeliodSunCrownedFactory"/>.
///
/// ## Implemented (v1)
///
/// - 4/5 Legendary Enchantment Creature — God at {1}{R}{G}.
/// - <b>Indestructible (CR 702.12)</b>: <see cref="KeywordAbility"/> marker.
///   SBA 704.5g + the destroy pipeline read it via
///   <see cref="Majik.Core.Combat.CombatAbilities.HasIndestructible"/> — same
///   wiring as Heliod / Avacyn / The One Ring.
/// - <b>Devotion-to-red-and-green check (CR 700.5)</b>: "As long as your
///   devotion to red and green is less than seven, Klothys isn't a
///   creature." <see cref="ComputeDevotionToRedAndGreen"/> sums every {R}
///   and {G} pip among the controller's battlefield permanents'
///   mana costs (CR 700.5: devotion to two colours counts symbols that are
///   either colour). v1 reads pure-{R} / pure-{G} pips via
///   <see cref="ValueObjects.ManaCost.Red"/> +
///   <see cref="ValueObjects.ManaCost.Green"/> (same hybrid / Phyrexian
///   deferral as Heliod's devotion-to-white). Klothys itself contributes 2
///   ({R} + {G}).
/// - <b>Layer 4 devotion-gated type-strip (CR 205.2 / 613.1d)</b>: when the
///   <c>(owner, triggers, effects)</c> overload is invoked with a
///   <see cref="ContinuousEffectsService"/>, a
///   <see cref="Layer4TypeStripEffect"/> is registered on Klothys with
///   predicate <c>ComputeDevotionToRedAndGreen(controller) &lt; 7</c>. While
///   the predicate holds, Klothys's layered characteristics drop Creature —
///   he can't be targeted by creature-only spells, can't attack, can't be
///   declared as a blocker. Identical wiring to Heliod's type-strip.
/// - <b>First-main-phase triggered ability (CR 500 / 603.6a)</b>: "At the
///   beginning of your first main phase, exile target card from a graveyard.
///   If it was a land card, add {R} or {G}. Otherwise, you gain 2 life and
///   Klothys deals 2 damage to each opponent." Wired via
///   <see cref="Triggers.OnStepBegin"/> on
///   <see cref="Majik.Core.StateMachine.StepStateType.PreCombatMain"/>
///   (the precombat / "first" main phase), restricted to Klothys's
///   controller's own turns. On resolution the chosen graveyard card is
///   rechecked (still in a graveyard — CR 608.2b), exiled via
///   <see cref="Fx.MoveToExile"/>, then the land / nonland branch is taken:
///   * <b>Land</b> — one mana of the controller's chosen colour is added
///     via <see cref="Fx.AddMana"/> (CR 106). The {R}-or-{G} choice is the
///     deferred binary-choice posture used across the v1 factory surface;
///     <see cref="LandManaColor"/> defaults to <c>"R"</c> and is settable
///     for tests / future agent prompts.
///   * <b>Nonland</b> — the controller gains
///     <see cref="LifeGain"/> life (<see cref="Fx.GainLife"/>) and Klothys
///     deals <see cref="DamageToEachOpponent"/> to each opponent supplied by
///     the optional <paramref name="opponentsResolver"/> via
///     <see cref="Fx.DealDamageAny"/> — same each-opponent-damage posture as
///     <see cref="HazoretTheFerventFactory"/>'s burn.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape + Indestructible + the
///   trigger (no continuous-effects service, no opponents resolver). The
///   overload <see cref="NamedCardFactory"/> dispatches to.
/// - <see cref="Create(Player, Func{IReadOnlyList{Player}}?)"/> — adds an
///   opponents resolver for the nonland-branch burn.
/// - <see cref="Create(Player, TriggerManager?, ContinuousEffectsService?,
///   Func{IReadOnlyList{Player}}?)"/> — fully wired: trigger registration +
///   the devotion type-strip + the opponents resolver.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Hybrid / Phyrexian {R} / {G} devotion contributions</b>: CR 700.5a
///   counts every symbol that includes the colour; v1 reads pure pips only
///   (same plumbing deferral as Heliod's devotion-to-white).
/// - <b>{R}-or-{G} player choice</b>: the land branch reads
///   <see cref="LandManaColor"/> (default {R}) rather than prompting the
///   controller — same deferred binary-choice posture as the rest of the
///   "add X or Y" mana surface.
/// - <b>Opponents-resolver threading</b>: the nonland burn's opponent list
///   is supplied by the caller at resolve time rather than read off a live
///   table — identical posture to Hazoret / Boltwave.
/// - <b>Agent-driven target prompt</b>: the trigger honours pre-set
///   <see cref="ITriggeredAbility.ChosenTargets"/>; the factory does not
///   wire an <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> prompt
///   (same posture as Heliod / Earthshaker Khenra).
///
/// CR rule references: 205.2 (Legendary / type-changing), 205.3m (God
/// subtype), 301 / 302 (Enchantment + Creature multi-type), 700.5
/// (devotion), 702.12 (Indestructible), 613.1d (Layer 4 type strip), 500 /
/// 603.6a (beginning-of-main-phase trigger), 608.2b (target recheck),
/// 701.20 (exile), 106 (mana), 119 (damage), 119.3 (life gain).
/// </summary>
[CardName("Klothys, God of Destiny")]
public static class KlothysGodOfDestinyFactory
{
    public const string CardName = "Klothys, God of Destiny";
    public const string Slug = "klothys-god-of-destiny";
    public const string PrintedManaCost = "{1}{R}{G}";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>CR 700.5 — Klothys is a creature only while devotion to red
    /// and green is at least this many pips.</summary>
    public const int DevotionThreshold = 7;

    /// <summary>Life gained on the nonland branch (CR 119.3).</summary>
    public const int LifeGain = 2;

    /// <summary>Damage dealt to each opponent on the nonland branch
    /// (CR 119).</summary>
    public const int DamageToEachOpponent = 2;

    /// <summary>
    /// Land-branch mana colour. The printed choice is "{R} or {G}"; v1
    /// defaults to red and exposes this seam for tests / a future agent
    /// prompt (deferred binary-choice posture). Accepts a single-colour
    /// short form parsable by <see cref="Fx.AddMana"/> (e.g. "R" / "G").
    /// </summary>
    public static string LandManaColor { get; set; } = "R";

    /// <summary>
    /// Construct Klothys, God of Destiny. Indestructible + the
    /// first-main-phase trigger are attached to the card shape, but the
    /// trigger is not registered with a <see cref="TriggerManager"/> and no
    /// devotion type-strip / opponents resolver is wired. The overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, effects: null);

    /// <summary>
    /// Construct a fully-wired Klothys, God of Destiny. The nonland-branch burn
    /// reads "each opponent" from the live resolution context at resolution
    /// (<see cref="ContextOpponents"/>), so it is correct on the production
    /// routed build.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the first-main-phase
    /// <see cref="TriggeredAbility"/> is registered for bus-driven
    /// firing.</param>
    /// <param name="effects">When supplied, a
    /// <see cref="Layer4TypeStripEffect"/> is registered with predicate
    /// <c>ComputeDevotionToRedAndGreen(controller) &lt; 7</c> (CR 205.2 /
    /// 613.1d). The service is also stamped onto Klothys's
    /// <see cref="Creature.ActiveEffects"/>.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Legendary
        // supertype, God subtype, Enchantment + Creature multi-type,
        // {1}{R}{G}, 4/5). The JSON carries no abilities — printed
        // behaviours are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.12 — Indestructible keyword marker.
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // ----------------------------------------------------------------
        // First-main-phase triggered ability — CR 500 / 603.6a.
        //   "At the beginning of your first main phase, exile target card
        //    from a graveyard. If it was a land card, add {R} or {G}.
        //    Otherwise, you gain 2 life and Klothys deals 2 damage to each
        //    opponent."
        // Fires on the precombat ("first") main phase, restricted to the
        // controller's own turns. Resolution: recheck the chosen card is
        // still in a graveyard (CR 608.2b), exile it (CR 701.20), then take
        // the land / nonland branch.
        // ----------------------------------------------------------------
        TriggeredAbility? trigger = null;
        var triggerEffect = new Effect(
            $"{CardName}: exile target graveyard card; land -> add {{R}}/{{G}}, else gain {LifeGain} life + {DamageToEachOpponent} to each opponent",
            ctx =>
            {
                if (trigger == null) return ValueTask.CompletedTask;
                var chosen = trigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return ValueTask.CompletedTask;

                if (chosen[0][0] is not ICard target) return ValueTask.CompletedTask;
                if (target.Zone != ZoneType.Graveyard) return ValueTask.CompletedTask; // CR 608.2b

                var wasLand = target.HasType(CardType.Land);

                // CR 701.20 — exile the targeted card.
                Fx.MoveToExile(target);

                var controller = card.Controller ?? owner;

                if (wasLand)
                {
                    // CR 106 — add {R} or {G}. Deferred binary choice: read
                    // LandManaColor (default {R}).
                    Fx.AddMana(controller, LandManaColor);
                }
                else
                {
                    // CR 119.3 — you gain 2 life.
                    Fx.GainLife(controller, LifeGain);

                    // CR 119 — Klothys deals 2 damage to each opponent. "Each
                    // opponent" is read from the LIVE resolution context — NOT
                    // a captured resolver, which was null on the routed prod
                    // build and made the burn INERT in real games (resolver-null
                    // bug class; mirrors Stormbreath #2540 / Grist #2549).
                    foreach (var opp in ContextOpponents.Of(ctx, controller))
                    {
                        Fx.DealDamageAny(opp, DamageToEachOpponent);
                    }
                }
                return ValueTask.CompletedTask;
            });

        trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.StepStateType.PreCombatMain),
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target card from a graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // Layer 4 devotion-gated type-strip — CR 205.2 / 613.1d.
        //   "As long as your devotion to red and green is less than seven,
        //    Klothys isn't a creature."
        // Predicate re-evaluated every Compute pass, so devotion bumps /
        // drops flip Klothys's effective Creature type without
        // re-registering. Source-anchored — ends when Klothys LTB's.
        // ----------------------------------------------------------------
        if (effects != null)
        {
            card.ActiveEffects = effects;
            effects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () =>
                    ComputeDevotionToRedAndGreen(card.Controller!) < DevotionThreshold));
        }

        return card;
    }

    /// <summary>
    /// CR 700.5 — devotion to red and green. Sum of {R} and {G} mana symbols
    /// among the mana costs of permanents <paramref name="player"/> controls.
    /// A symbol that is both red and green (a {R/G} hybrid) would count once
    /// toward this two-colour devotion; v1 reads pure-{R} / pure-{G} pips
    /// only (hybrid / Phyrexian contributions DEFERRED — same plumbing
    /// deferral as Heliod's devotion-to-white).
    ///
    /// Exposed publicly so bots / tests can read the live count directly.
    /// </summary>
    public static int ComputeDevotionToRedAndGreen(Player player)
    {
        if (player == null) return 0;
        var total = 0;
        foreach (var perm in player.Zones.Battlefield.GetCards())
        {
            if (perm is Card concrete)
            {
                total += concrete.ManaCostValue.Red + concrete.ManaCostValue.Green;
            }
        }
        return total;
    }
}
