using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vexing Bauble (Modern Horizons 3, {1}).
///
/// Artifact. Oracle text:
///   "Whenever a player casts a spell, if no mana was spent to cast it,
///    counter that spell."
///   "{1}, {T}, Sacrifice this artifact: Draw a card."
///
/// ## Implementation
///
/// - <see cref="Artifact"/> {1} — vanilla card shape, owner / controller
///   wired.
/// - <b>Free-spell counter (CR 603.1 / CR 118)</b> — a
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/> gated
///   on <see cref="Majik.Core.Spells.Spell.WasFreeCast"/> (the "no mana was
///   spent to cast it" sentinel stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> when the collapsed total
///   cost — printed + alt-cost + cost reductions + +X + Delve — is
///   <see cref="Majik.Core.ValueObjects.ManaCost.IsZero"/>). Unlike
///   <see cref="BoromirWardenOfTheTowerFactory"/> ("Whenever an <em>opponent</em>
///   casts") this trigger watches <em>every</em> player including the
///   controller (CR 102 — "a player"), so there is NO controller exemption
///   gate. Resolution counters the captured spell via
///   <see cref="Majik.Core.Primitives.Fx.Counter"/> against the live stack.
///   Mirrors Boromir's free-spell counter shape exactly, minus the
///   opponent filter.
/// - <b>{1}, {T}, Sacrifice this artifact: Draw a card (CR 602)</b> — an
///   <see cref="ActivatedAbility"/> with three costs:
///   <see cref="ManaCostCost"/>("{1}") + <see cref="AdditionalCost.Tap"/> +
///   <see cref="AdditionalCost.Sacrifice"/>, resolving to a single-card
///   draw via <see cref="Majik.Core.Primitives.Fx.DrawCards"/>(owner, 1).
///   Same cantrip-bauble shape as <see cref="ConjurersBaubleFactory"/> /
///   <see cref="MishrasBaubleFactory"/>. Empty library is a silent no-op;
///   SBAs (CR 704.5b / CR 120.3) handle the loss condition.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Both abilities are
///   attached for structural observability; the counter trigger is NOT
///   registered with any service (it no-ops without a stack). Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, TriggerManager?, Majik.Core.Stack.Stack?)"/>
///   — fully wired: registers the free-spell counter trigger so every
///   player's free cast drives it automatically, and threads the live stack
///   so the counter effect can remove the spell (CR 701.5).
///
/// ## Deferred (v1 gaps)
/// - <b>Sacrifice payment side-effect</b> on the draw ability —
///   <see cref="AdditionalCost.Sacrifice"/> zone-move is a no-op stub today
///   (same gap as Roiling Vortex / Relic of Progenitus / Nihil Spellbomb),
///   so activating the draw ability does not actually graveyard the Bauble
///   in v1.
/// </summary>
[CardName("Vexing Bauble")]
public static class VexingBaubleFactory
{
    public const string CardName = "Vexing Bauble";
    public const string PrintedManaCost = "{1}";

    /// <summary>
    /// Construct Vexing Bauble with no live wiring. Both abilities are
    /// attached to the card shape for structural observability; the counter
    /// trigger is not registered with a trigger manager. Suitable for
    /// dispatcher / structural tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, triggers: null, stack: null);

    /// <summary>
    /// Construct Vexing Bauble with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the free-spell counter trigger
    /// is registered so any player's free cast drives it automatically.</param>
    /// <param name="stack">The live stack — required for the counter effect
    /// to remove the free spell (CR 701.5). Without it the counter trigger
    /// fires but no-ops.</param>
    public static Artifact Create(
        Player owner,
        TriggerManager? triggers,
        Majik.Core.Stack.Stack? stack)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var bauble = new Artifact(CardName, PrintedManaCost);
        bauble.SetOwner(owner);
        bauble.SetController(owner);

        // ----------------------------------------------------------------
        // Free-spell counter — CR 603.1 / CR 118.
        //   "Whenever a player casts a spell, if no mana was spent to cast
        //    it, counter that spell."
        // Gated solely on the spell's WasFreeCast sentinel. "A player" =
        // every player (CR 102), so — unlike Boromir's "an opponent" —
        // there is NO controller exemption: the Bauble counters its own
        // controller's free casts too. The captured spell is countered at
        // resolution via Fx.Counter against the live stack.
        // ----------------------------------------------------------------
        Majik.Core.Spells.ISpell? capturedFreeSpell = null;

        var counterCondition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            if (e.Spell is not Majik.Core.Spells.Spell s) return false;
            if (!s.WasFreeCast) return false;
            capturedFreeSpell = s;
            return true;
        });

        var counterEffect = new Effect(
            $"{CardName}: counter the free spell",
            () =>
            {
                var spell = capturedFreeSpell;
                capturedFreeSpell = null;
                if (spell == null || stack == null) return;
                Majik.Core.Primitives.Fx.Counter(stack, spell);
            });

        var counterTrigger = new TriggeredAbility(
            source: bauble,
            controller: owner,
            condition: counterCondition,
            effects: new IEffect[] { counterEffect },
            activeZones: new[] { ZoneType.Battlefield });

        bauble.AddAbility(counterTrigger);
        triggers?.RegisterTriggeredAbility(counterTrigger);

        // ----------------------------------------------------------------
        // {1}, {T}, Sacrifice this artifact: Draw a card.
        // CR 602 — activated ability with three costs (ManaCostCost({1}) +
        // Tap + Sacrifice). Same cantrip-bauble shape as Conjurer's /
        // Mishra's Bauble. Empty library is a silent no-op (CR 704.5b /
        // CR 120.3 — SBAs handle the loss).
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            $"{CardName}: draw a card",
            () => Majik.Core.Primitives.Fx.DrawCards(owner, 1));

        var drawAbility = new ActivatedAbility(
            source: bauble,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Tap(bauble),
                AdditionalCost.Sacrifice(bauble),
            },
            effects: new IEffect[] { drawEffect });

        bauble.AddAbility(drawAbility);

        return bauble;
    }
}
