using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stonecoil Serpent (Throne of Eldraine, {X}).
/// Artifact Creature — Snake 0/0. Oracle text (verified against Scryfall):
///   "Reach, trample, protection from multicolored
///    This creature enters with X +1/+1 counters on it."
///
/// Colourless ({X} cost, no coloured pips, no colour indicator). The base
/// shape (name, Artifact + Creature types, Snake subtype, {X}, 0/0) is
/// materialised from the embedded JSON definition
/// (<c>stonecoil-serpent.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The static keyword riders and
/// the ETB X-counters trigger are layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers,
/// protection qualities, or variable-X ETB counters, so they live in the
/// factory (same posture as
/// <see cref="SphinxOfTheSteelWindFactory"/> for the keyword/protection
/// riders and <see cref="HangarbackWalkerFactory"/> for the ETB-X-counters
/// trigger).
///
/// ## Implemented (v1)
/// - 0/0 Snake (CR 205.3) at {X}, Artifact Creature, colourless.
///   <see cref="Card.ManaCostValue.HasX"/> reports true.
/// - <b>Reach (CR 702.17)</b>, <b>trample (CR 702.19)</b> — wired as
///   <see cref="KeywordAbility"/> markers so
///   <see cref="Majik.Core.Combat.CombatAbilities"/> surfaces the combat
///   behaviour (canonical casing matching the <c>CombatAbilities.Has*</c>
///   lookups). Same marker shape as Sphinx of the Steel Wind's keyword
///   riders.
/// - <b>Protection from multicolored (CR 702.16)</b> — a
///   <see cref="ProtectionAbility"/> carrying a
///   <see cref="ProtectionAbility.SpellPredicate"/> closure
///   <c>spell => CardColors.GetColors(spell.Card).Count >= 2</c>. The
///   quality string surface ("white"/"red"/…) can't reduce
///   "multicolored" to a single colour token, so the predicate is the
///   canonical surface (same posture as Emrakul, the Aeons Torn's
///   "protection from coloured spells" — see
///   <see cref="EmrakulTheAeonsTornFactory"/>). The predicate reads the
///   spell's live colour identity off its card at gate time
///   (CR 105 / CR 105.2 — a card/spell is multicolored if it has two or
///   more colours).
/// - <b>ETB +1/+1 counters trigger (CR 603.6a / CR 122.1g)</b>: on
///   entering the battlefield, places X +1/+1 counters. X is read from
///   <see cref="Card.PendingCastX"/> (stamped by
///   <see cref="Majik.Core.Game.SpellCastFlow"/> after the caster's
///   ChooseXAsync), then the stamp is consumed so a non-cast re-entry
///   (blink, copy) doesn't reuse it — that entry leaves Stonecoil as a
///   0/0 with zero counters, and the SBA pass (CR 704.5f) puts it in the
///   graveyard, matching the printed behaviour. Counter placement routes
///   through <see cref="CountersService.Add"/> when a
///   <see cref="ReplacementBus"/> is supplied so Hardened Scales /
///   Doubling Season rewrite the count (CR 614). Identical pattern to
///   <see cref="HangarbackWalkerFactory"/>'s ETB-counter trigger.
///
/// ## Deferred (v1 gaps — shared with the predicate-protection cohort)
/// - <b>Combat / damage / equip side of protection from multicolored</b>:
///   <see cref="ProtectionAbility.SpellPredicate"/> only gates the
///   spell-targeting case. The colour-string combat / damage gates
///   (<see cref="Majik.Core.Combat.CombatValidator"/>,
///   <see cref="Majik.Core.Rules.Protection.HasProtectionFromColor"/>)
///   match single colours, not "multicolored", so a multicolored blocker /
///   damage source / Aura is not yet stopped by this rider — the same
///   documented v1 posture as Emrakul's "protection from coloured spells".
/// </summary>
[CardName("Stonecoil Serpent")]
public static class StonecoilSerpentFactory
{
    public const string CardName = "Stonecoil Serpent";
    public const string Slug = "stonecoil-serpent";

    /// <summary>
    /// Construct Stonecoil Serpent with no live wiring. The ETB-counters
    /// trigger is attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>; counter placement uses the direct
    /// <see cref="CountersService.Add"/> fallthrough (no replacement-bus
    /// rewrites, no event publish). Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, replacements: null, eventBus: null);

    /// <summary>
    /// Construct Stonecoil Serpent with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">TriggerManager. When supplied the ETB
    /// counter-placement trigger registers for bus-driven firing
    /// (CR 603.2).</param>
    /// <param name="replacements">ReplacementBus. When supplied counter
    /// placement routes through <see cref="CountersService.Add"/> so
    /// Hardened Scales / Doubling Season rewrite the count (CR 614).</param>
    /// <param name="eventBus">EventBus. When supplied counter placement
    /// publishes <see cref="CounterAddedEvent"/> so Animation-Module-style
    /// "+1/+1 counters were put on …" triggers can chain.</param>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        ReplacementBus? replacements,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Artifact types, Snake subtype, {X}, 0/0). The JSON carries no
        // abilities — the keyword riders are layered below; the ETB X counters
        // are owned by the generic binder (see note below).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner, replacements);

        // ----------------------------------------------------------------
        // "This creature enters with X +1/+1 counters on it" (CR 614.1d /
        // CR 202.3b) is NOT wired by this factory. It is registered by the
        // generic EntersWithCountersBinder as a variable-X
        // EntersWithCountersReplacement. On the production deck-build
        // (DeckCardBuilder APPROACH B) the binder runs in OverlayAdditiveBinders
        // against the live ReplacementBus, matches Stonecoil's oracle text
        // ("enters with X +1/+1 counters on it"), reads the chosen X off
        // Card.PendingCastX (stamped by SpellCastFlow after ChooseXAsync), and
        // stamps ZoneMoveIntent.PlusOneCountersOnEnter so the permanent enters
        // WITH the counters (no transient 0/0 window). Hardened Scales /
        // Doubling Season compose on that same ETB intent channel (CR 614).
        //
        // The factory deliberately does NOT MarkSelfManagesEntersWithCounters()
        // and does NOT attach an ETB TriggeredAbility for the counters — that
        // was the bug (the same one Walking Ballista had, #2635): the prod
        // Approach-B route calls NamedCardFactory.Create with no TriggerManager,
        // so a self-managed ETB trigger is never registered and never fires, AND
        // the self-manage flag suppresses the binder — the one mechanism that
        // route DOES run — yielding ZERO counters in real play. The keyword
        // riders (reach / trample / protection from multicolored) below are
        // unaffected.
        //
        // triggers / eventBus remain on the signature for overload-API
        // compatibility with shape/dispatcher tests; the X-counter mechanism no
        // longer consumes them (the binder owns the ReplacementBus on the live
        // path).
        // ----------------------------------------------------------------
        _ = triggers;
        _ = eventBus;

        // Evergreen combat keywords. KeywordAbility markers so
        // CombatAbilities.Has{Reach,Trample} surface the combat behaviour.
        // Casing matches the CombatAbilities lookups.
        card.AddAbility(new KeywordAbility("Reach", card, owner));    // CR 702.17
        card.AddAbility(new KeywordAbility("Trample", card, owner));  // CR 702.19

        // CR 702.16 — Protection from multicolored. The colour-string
        // surface can't reduce "multicolored" to a single colour token, so
        // the SpellPredicate is the canonical surface (same posture as
        // Emrakul, the Aeons Torn's "protection from coloured spells").
        // CR 105.2 — a spell is multicolored when it has two or more
        // colours; colour is read off the spell's card at gate time so a
        // colour-changing continuous effect is honoured (CR 105 /
        // CR 202.2).
        card.AddAbility(new ProtectionAbility(
            "multicolored",
            spellPredicate: spell => CardColors.GetColors(spell.Card).Count >= 2));

        return card;
    }
}
