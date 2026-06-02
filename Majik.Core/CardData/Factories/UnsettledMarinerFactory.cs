using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Stack;
using Majik.Core.Targeting;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unsettled Mariner (Modern Horizons 2, {W}{U}).
///
/// Creature — Shapeshifter 2/2. Oracle text (verified against Scryfall):
///   "Changeling (This card is every creature type.)
///    Whenever you or a permanent you control becomes the target of a spell
///    or ability an opponent controls, counter that spell or ability unless
///    its controller pays {1}."
///
/// ## Implemented (v1)
///
/// - <b>2/2 Creature — Shapeshifter</b> at {W}{U}. The base shape (name,
///   Creature, Shapeshifter subtype, {W}{U}, 2/2) is materialised from the
///   embedded JSON definition (<c>unsettled-mariner.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the Changeling subtype
///   stamping + the ward-like trigger are layered on here.
///
/// - <b>Changeling (CR 702.73)</b> — the card is every creature type in
///   every zone. Same v1 modelling as <see cref="MutableExplorerFactory"/>:
///   the printed <see cref="Card.Subtypes"/> set is stamped with the
///   engine's currently-enumerated creature subtypes (sourced from
///   <see cref="MutavaultAnimateEffect.EveryCreatureType"/> so the
///   changeling list stays in lockstep with Mutavault's animate list — when
///   the enum grows, both pick up the new subtype with no per-card edits)
///   plus the printed <see cref="CardSubtype.Shapeshifter"/> base type, and
///   a <see cref="KeywordAbility"/>("Changeling") marker for UI / future
///   Changeling-aware enumerations. CR 702.73a: "Each object with the
///   changeling ability is each creature type. This ability works
///   everywhere, even outside the game." — stamping the printed list models
///   that static-everywhere posture (no Layer 4 registration needed).
///
/// - <b>Ward-style soft-counter trigger (CR 603.6c / 113.3 / 701.5)</b>:
///   "Whenever you or a permanent you control becomes the target of a spell
///    or ability an opponent controls, counter that spell or ability unless
///    its controller pays {1}." Wired via <see cref="TargetsChosenEvent"/>
///    (published by both <see cref="Majik.Core.Services.SpellCaster"/> and
///    <see cref="Majik.Core.Services.AbilityActivator"/>, so "spell or
///    ability" is covered automatically — same attachment point as
///    <see cref="NaduWingedWisdomFactory"/>). Predicate (CR 109.5 — "you"):
///      1. The targeting stack object's controller is an OPPONENT of the
///         Mariner's controller (CR 102.1 — different player). This is the
///         "a spell or ability an opponent controls" gate.
///      2. Some chosen target is either the Mariner's controller (the
///         player) OR a permanent that player controls ("you or a permanent
///         you control"). The Mariner itself qualifies — it is a permanent
///         its controller controls.
///   On resolution the soft-counter is applied: if the targeting object's
///   controller can pay {1} they do (auto-pay, same v1 posture as
///   <see cref="MausoleumWandererFactory"/> / Ward — CR 702.21f); otherwise
///   the spell or ability is countered via
///   <see cref="OracleSpellBinder.RemoveFromStack"/> (CR 701.5b — a
///   countered spell goes to its owner's graveyard; a countered
///   activated/triggered ability simply ceases to exist, no graveyard hop).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The trigger is attached for
///   dispatcher / structural tests but not registered (no stack / trigger
///   manager), so the soft-counter is a no-op.
/// - <see cref="Create(Player, Majik.Core.Stack.Stack?, TriggerManager?)"/>
///   — fully wired. The targeted-by trigger is registered so a matching
///   <see cref="TargetsChosenEvent"/> surfaces as pending; the stack handle
///   drives the counter / removal on resolution.
///
/// ## Notes
/// - <b>Per-instance soft-counter</b>: the engine's
///   <see cref="Majik.Core.Keywords.WardEffect"/> primitive models the
///   permanent-only Ward case (CR 702.21 — "this permanent becomes the
///   target"). Unsettled Mariner's trigger is broader ("you or a permanent
///   you control"), so the predicate + resolution are expressed directly
///   here off <see cref="TargetsChosenEvent"/> rather than via
///   <see cref="Majik.Core.Keywords.WardEffect"/>; the pay-or-counter math
///   is the same shape.
/// - <b>Auto-pay</b>: the "unless its controller pays {1}" choice is
///   auto-taken from the targeting controller's floating mana pool (pay when
///   able — the rational play). Same simplification used by Ward / Mausoleum
///   Wanderer / Mana Leak until an agent "may pay" prompt lands.
/// </summary>
[CardName("Unsettled Mariner")]
public static class UnsettledMarinerFactory
{
    public const string CardName = "Unsettled Mariner";
    public const string Slug = "unsettled-mariner";

    /// <summary>CR 113.3 — the soft-counter tax: {1}.</summary>
    public const string TaxCost = "{1}";

    /// <summary>
    /// Construct Unsettled Mariner with no live wiring. Changeling subtype
    /// stamping + the targeted-by trigger are attached to the card shape; the
    /// trigger is not registered (no stack / trigger manager) so the
    /// soft-counter is a no-op. Suitable for dispatcher / structural tests.
    /// This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, stack: null, triggers: null);

    /// <summary>
    /// Construct Unsettled Mariner with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="stack">Live stack — required for the counter to remove
    /// the targeting spell/ability on resolution via
    /// <see cref="OracleSpellBinder.RemoveFromStack"/>. May be null for shape
    /// tests (the trigger still fires + resolves harmlessly).</param>
    /// <param name="triggers">TriggerManager — when supplied the targeted-by
    /// trigger is registered so a matching
    /// <see cref="TargetsChosenEvent"/> surfaces as pending. May be null —
    /// the trigger is still attached to the card shape.</param>
    public static Creature Create(
        Player owner,
        Majik.Core.Stack.Stack? stack,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Shapeshifter, {W}{U}, 2/2). The Changeling subtype stamping + the
        // ward-like trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.73a — Changeling: this card is every creature type. Stamp
        // the engine's currently-modelled creature subtype set on the printed
        // body so HasSubtype(Goblin), HasSubtype(Elf), etc. all return true
        // everywhere. Sourced from MutavaultAnimateEffect.EveryCreatureType so
        // the changeling list stays in lockstep (same posture as
        // MutableExplorerFactory). Shapeshifter (printed) is already present
        // from the JSON; dedupe against it.
        foreach (var st in MutavaultAnimateEffect.EveryCreatureType)
        {
            if (card.HasSubtype(st)) continue; // dedupe (incl. Shapeshifter)
            card.AddSubtype(st);
        }

        // Changeling keyword marker (CR 702.73) — observational; the subtype
        // stamping above drives tribal-lord interactions.
        card.AddAbility(new KeywordAbility("Changeling", card, owner));

        // ----------------------------------------------------------------
        // Targeted-by soft-counter trigger — CR 603.6c / 113.3 / 701.5.
        //   "Whenever you or a permanent you control becomes the target of a
        //    spell or ability an opponent controls, counter that spell or
        //    ability unless its controller pays {1}."
        //
        // Fires on TargetsChosenEvent where:
        //   (a) the targeting stack object's controller is an OPPONENT of
        //       the Mariner's controller (CR 102.1 — "an opponent controls"),
        //   (b) some chosen target is the Mariner's controller (the player)
        //       OR a permanent that player controls (CR 109.5 — "you or a
        //       permanent you control"). The Mariner itself qualifies.
        //
        // TargetsChosenEvent is published by both SpellCaster and
        // AbilityActivator, so "spell or ability" is covered automatically.
        // ----------------------------------------------------------------
        IStackObject? capturedSource = null;

        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            var controller = card.Controller ?? owner;

            // (a) opponent-controlled spell/ability gate (CR 102.1).
            var sourceController = e.StackObject.Controller;
            if (sourceController == null) return false;
            if (ReferenceEquals(sourceController, controller)) return false;

            // (b) target is you, or a permanent you control (CR 109.5).
            foreach (var t in e.Targets)
            {
                if (TargetMatchesYouOrYours(t, controller))
                {
                    capturedSource = e.StackObject;
                    return true;
                }
            }

            return false;
        });

        var counterEffect = new Effect(
            $"{CardName} — counter that spell or ability unless its controller pays {{1}}",
            () =>
            {
                var source = capturedSource;
                capturedSource = null;
                if (source == null || stack == null) return;

                // CR 608.2b — recheck the targeting object is still on the
                // stack at resolution. If it already left, nothing to counter.
                if (!stack.GetAll().Contains(source)) return;

                // CR 113.3 / 702.21f — "unless its controller pays {1}". The
                // cost is paid only when the controller both CAN and (in v1)
                // auto-chooses to. Pay when able is the rational play.
                if (ControllerPaidTax(source))
                {
                    return; // paid → not countered.
                }

                // CR 701.5b — counter the spell or ability. RemoveFromStack
                // returns false for an uncounterable spell (it stays put).
                if (!OracleSpellBinder.RemoveFromStack(stack, source)) return;

                // CR 701.5b — a countered SPELL goes to its owner's
                // graveyard; a countered activated/triggered ability simply
                // ceases to exist (no zone move).
                if (source is Majik.Core.Spells.ISpell spell && spell.Card is Card spellCard)
                {
                    spellCard.SetZone(ZoneType.Graveyard);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    /// <summary>
    /// CR 109.5 — does <paramref name="target"/> match "you or a permanent
    /// you control" for <paramref name="controller"/>? True when the target
    /// is the controller (the player) or a permanent that player controls.
    /// </summary>
    private static bool TargetMatchesYouOrYours(ITarget target, Player controller)
    {
        if (target is not Target concrete) return false;

        switch (concrete.TargetType)
        {
            case TargetType.Player:
                return ReferenceEquals(concrete.GetPlayer(), controller);

            case TargetType.Permanent:
            case TargetType.Card:
                // "a permanent you control" — a permanent on the battlefield
                // whose controller is you. We accept Card/Permanent target
                // shapes (Nadu reads both) and gate on the controller +
                // permanent identity.
                return concrete.TargetObject is Permanent perm
                       && ReferenceEquals(perm.Controller, controller);

            default:
                return false;
        }
    }

    /// <summary>
    /// CR 113.3 — may the targeting object's controller pay the {1} tax? v1
    /// auto-pays from their floating mana pool when able (same posture as
    /// Ward / Mausoleum Wanderer / Mana Leak). Returns true when paid (so the
    /// spell/ability is NOT countered).
    /// </summary>
    private static bool ControllerPaidTax(IStackObject source)
    {
        var payer = source.Controller;
        if (payer == null) return false;
        return payer.PayMana(ManaCost.Zero.AddGenericCost(1));
    }
}
