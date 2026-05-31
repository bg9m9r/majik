using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Conduit of Ruin (Battle for Zendikar, {6}).
///
/// Creature — Eldrazi 5/5. Oracle text (Scryfall, verified):
///   "When you cast this spell, you may search your library for a
///    colorless creature card with mana value 7 or greater, then shuffle
///    and put that card on top of your library.
///    The first colorless creature spell you cast each turn costs {2}
///    less to cast."
///
/// ## Implemented (v1)
/// - 5/5 Creature — Eldrazi at {6}.
/// - <b>Cast trigger — CR 603.6a / CR 603.1 (fires from stack)</b>:
///   <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>
///   filtered to <c>ReferenceEquals(e.Spell.Card, card)</c> — same
///   self-cast detection pattern as
///   <see cref="EmrakulTheAeonsTornFactory"/> / Ulamog, the Ceaseless
///   Hunger. ActiveZones = { <see cref="ZoneType.Stack"/> } so the
///   trigger lands while Conduit is still itself on the stack as a
///   spell. Resolution prompts the controller's agent (via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for an OPTIONAL
///   pick over <c>caster.Zones.Library.GetCards().Where(c =&gt;
///   c.HasType(CardType.Creature) &amp;&amp; CardColors.GetColors(c).Count == 0
///   &amp;&amp; ManaCost.Parse(c.ManaCost).TotalValue &gt;= 7)</c>
///   (Worldly Tutor posture). Null pick = no-op (CR 701.19a permits
///   declining to find — the "may" gate); valid pick = Library →
///   top-of-Library via <see cref="LibraryShuffle.ShuffleLibrary"/>
///   (CR 701.20a) THEN <c>InsertCardAt(0, pick)</c> so the picked card
///   ends up on top of an otherwise-randomized library (mirrors
///   <see cref="WorldlyTutorFactory"/>'s shuffle-then-place sequencing).
/// - <b>Cost reduction — colorless creature spells cost {2} less</b>:
///   shipped as an ALWAYS-ON <see cref="SpellCostReductionAbility"/>
///   (not per-turn-first-cast — see deferred gap below). Predicate gates
///   on <c>card.HasType(CardType.Creature) &amp;&amp;
///   CardColors.GetColors(card).Count == 0</c> over the spell being
///   cast; reduction returns 2 unconditionally. Folded into the cost-calc
///   pipeline by <see cref="CostReduction.GetEffectiveCost"/> at cast
///   time alongside other subtractive riders (Goblin Electromancer /
///   Baral). CR 117.7c — coloured pips are never reduced; the {2}
///   discount lands on the generic bucket only. Conduit's own cast does
///   NOT discount itself (CR 117.7e — the spell being cast is on the
///   stack at cost-calc time, not the battlefield; the rider only fires
///   off reducers in the caster's battlefield).
///
/// ## Deferred (v1 gaps)
/// - <b>Per-turn-once cost reduction</b>: the printed text says "the
///   first colorless creature spell you cast each turn" — i.e. the
///   reduction should fire exactly once per turn per caster. The engine
///   currently lacks a per-turn-first-cast tracker keyed on a (predicate,
///   player) pair: <see cref="Majik.Core.Game.TurnState"/>'s
///   <c>SpellsCastByPlayer</c> tallies all spells with no by-type bucket,
///   and <see cref="SpellCostReductionAbility"/> doesn't expose a usage
///   counter or post-cast bookkeeping hook. Per the factory brief, ship
///   the always-on -2 for colorless creature spells; the consequence is
///   that the SECOND, THIRD, etc. colorless creature spell Conduit's
///   controller casts each turn also enjoys the discount. Acceptable
///   over-reduction (Eldrazi Tron rarely chains multiple {6}+ colourless
///   creature spells in a single turn). Tighten once
///   <c>TurnState.RecordSpellCast</c> grows a per-type bucket + a
///   subtractive-rider usage counter.
/// - <b>Spell-targeting predicate</b>: Conduit's own cast is not its own
///   reducer (the rider lives on the BATTLEFIELD copy of Conduit, not the
///   stack copy — <see cref="CostReduction.GetEffectiveCost"/> scans the
///   caster's battlefield only). First-cast Conduit pays full {6}; a
///   SECOND Conduit cast while the first is on the battlefield does see
///   the {2} discount. Correct per CR 117.7 — "reducers on the
///   battlefield".
/// </summary>
[CardName("Conduit of Ruin")]
public static class ConduitOfRuinFactory
{
    public const string CardName = "Conduit of Ruin";
    public const string PrintedManaCost = "{6}";
    public const int Power = 5;
    public const int Toughness = 5;
    public const int CostReductionAmount = 2;
    public const int TutorManaValueThreshold = 7;

    /// <summary>
    /// Construct Conduit of Ruin with no live wiring. The cast trigger is
    /// attached for shape observability; not registered with any
    /// <see cref="TriggerManager"/>; library moves on resolution use raw
    /// zone manipulation. Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Conduit of Ruin with an optional
    /// <see cref="TriggerManager"/>. When supplied, the cast trigger
    /// registers with the bus so a qualifying
    /// <see cref="SpellCastEvent"/> automatically queues the trigger on
    /// the stack (CR 603.2). The cost-reduction rider is attached in both
    /// overloads — it's static metadata read by
    /// <see cref="CostReduction.GetEffectiveCost"/> at cast time.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Eldrazi });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Cost reduction — "The first colorless creature spell you cast
        // each turn costs {2} less to cast."
        // CR 117.7. Per the factory brief, the per-turn-once primitive
        // isn't shipped yet, so v1 ships the always-on -2 for colorless
        // creature spells the controller casts (see class xmldoc / gap).
        // Lives on the card itself; CostReduction.GetEffectiveCost scans
        // the caster's battlefield for SpellCostReductionAbility riders
        // at cost-calc time. Predicate matches Creature + GetColors().Count
        // == 0 over the SPELL being cast.
        // ----------------------------------------------------------------
        card.AddAbility(new SpellCostReductionAbility(
            predicate: spell =>
                spell != null
                && spell.HasType(CardType.Creature)
                && CardColors.GetColors(spell).Count == 0,
            reduction: (_, _) => CostReductionAmount,
            description: $"Colorless creature spells you cast cost {{{CostReductionAmount}}} less to cast"));

        // ----------------------------------------------------------------
        // Cast trigger — CR 603.6a / CR 603.1.
        //   "When you cast this spell, you may search your library for a
        //    colorless creature card with mana value 7 or greater, then
        //    shuffle and put that card on top of your library."
        // Self-cast detection follows Emrakul's posture: filter
        // SpellCastEvent on ReferenceEquals(e.Spell.Card, card), capture
        // the caster so the resolve body uses the actual caster (and
        // therefore searches the right library / consults the right
        // agent) rather than the original owner. ActiveZones = Stack so
        // the trigger is alive while Conduit is itself the cast spell.
        // ----------------------------------------------------------------
        Player? capturedCaster = null;
        var castCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Spell.Card, card)) return false;
                capturedCaster = e.Spell.Controller;
                return true;
            });

        var castEffect = new Effect(
            $"{CardName}: tutor colorless creature mv >= {TutorManaValueThreshold} -> top of library",
            async ctx =>
            {
                var caster = capturedCaster ?? card.Controller ?? owner;
                if (caster == null) return;

                var candidates = caster.Zones.Library.GetCards()
                    .Where(IsTutorCandidate)
                    .ToList();

                // CR 701.19a — "may" search permits declining; agent
                // returns null = no-op. Empty candidate list also no-ops
                // (still permitted to "search"; deferred shuffle/empty-
                // search wiring is consistent with WorldlyTutorFactory).
                if (candidates.Count == 0) return;

                var agent = ctx.Agent ?? AgentRegistry.Get(caster);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates,
                        "colorless creature card with mana value 7 or greater").ConfigureAwait(false))
                    : candidates[0];
                if (pick == null) return;

                caster.Zones.Library.RemoveCard(pick);
                // CR 701.20a — shuffle BEFORE the place-on-top so the
                // tutored card ends up on top of an otherwise-randomized
                // library (Worldly Tutor / Mystical Tutor sequencing).
                LibraryShuffle.ShuffleLibrary(caster, "conduit-of-ruin");
                caster.Zones.Library.InsertCardAt(0, pick);
                pick.SetZone(ZoneType.Library);
            });

        var castTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: castCondition,
            effects: new IEffect[] { castEffect },
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(castTrigger);
        triggers?.RegisterTriggeredAbility(castTrigger);

        return card;
    }

    /// <summary>
    /// Pure helper: is <paramref name="candidate"/> a colorless Creature
    /// card with mana value &gt;= 7? Exposed for tests + bot policies.
    /// CR 202.3b — mana value of a card off the stack reads its printed
    /// cost ({X} contributes 0; no chosen X in the library).
    /// CR 105.2 — colorless = no W/U/B/R/G pip in the mana cost
    /// (delegated to <see cref="CardColors.GetColors"/>).
    /// </summary>
    public static bool IsTutorCandidate(ICard candidate)
    {
        if (candidate == null) return false;
        if (!candidate.HasType(CardType.Creature)) return false;
        if (CardColors.GetColors(candidate).Count != 0) return false;

        var mv = ManaCost.Parse(candidate.ManaCost ?? string.Empty).TotalValue;
        return mv >= TutorManaValueThreshold;
    }
}
