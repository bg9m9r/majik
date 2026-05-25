using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Game;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shardless Agent (Planechase 2012 / Modern
/// Horizons 2, {U}{B}{G} — Scryfall lists {1}{G}{U} on the MH2 printing
/// which is the current Modern-legal version; this factory targets the
/// MH2 reprint).
///
/// Artifact Creature — Human Rogue 2/2. Oracle text:
///   "Cascade (When you cast this spell, exile cards from the top of
///    your library until you exile a nonland card that costs less. You
///    may cast it without paying its mana cost. Put the exiled cards on
///    the bottom in a random order.)"
///
/// ## Implemented (v1)
/// - 2/2 Creature — Human Rogue, mana cost {1}{G}{U} (MH2 printing).
///   The Artifact card-type is added post-construction via
///   <see cref="Card.AddCardType"/> (same shape as
///   <see cref="ArcboundRavagerFactory"/> for the artifact-creature
///   combo) so the printed type-line is `Artifact Creature`.
/// - <b>Cascade triggered ability (CR 702.85)</b> on
///   <see cref="SpellCastEvent"/> for this card — mirrors
///   <see cref="CrashingFootfallsFactory"/>'s shape. Mana value for the
///   exile-until-less filter is 3 (the printed total of {1}{G}{U} —
///   Shardless Agent's cascade can hit cards with mana value ≤ 2).
///   Resolution invokes <see cref="CascadeAction.Cascade"/> with
///   <c>sourceManaValue: 3</c>; the optional <c>willCast</c> predicate
///   forwards the controller's "you may" decision. The actual
///   alternative-cost cast (CR 702.85a — "you may cast it without
///   paying its mana cost") is driven by the caller via
///   <see cref="Costs.CastFromExileAlternativeCost"/> on the
///   <see cref="CascadeAction.CascadeResult.Eligible"/> card; this
///   factory only fires the trigger (same posture as Crashing
///   Footfalls).
/// - <b>Cascade discovery</b>: the card name is registered with
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>
///   so the bot's bidding heuristic / value layer sees Shardless Agent
///   as a cascade card without extra wiring (mirrors Crashing
///   Footfalls / Living End discovery slots).
///
/// ## Deferred (v1 gaps)
/// - <b>Cascade-into-cascade</b>: when Shardless Agent's cascade
///   resolves into another cascade spell (e.g. Bloodbraid Elf), the
///   secondary cascade fires when the secondary spell is cast — wired
///   automatically as long as that spell's factory registers its own
///   SpellCastEvent trigger.
/// - <b>Free-cast wiring</b>: caller-driven via the
///   <c>onCascadeResolved</c> hook (same shape as Crashing Footfalls).
///   Single-arg dispatcher path attaches the trigger structurally with
///   no SpellCastFlow wired — suitable for shape / dispatcher tests.
/// </summary>
[CardName("Shardless Agent")]
public static class ShardlessAgentFactory
{
    public const string CardName = "Shardless Agent";
    public const string PrintedManaCost = "{1}{G}{U}";
    public const int CascadeSourceManaValue = 3;
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Single-arg dispatcher path. Attaches the cascade trigger
    /// structurally so card shape is correct; no TriggerManager wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers
    /// the cascade trigger so a <see cref="SpellCastEvent"/> for this
    /// card lands on the stack automatically. <paramref name="willCast"/>
    /// is the controller's "you may" decision on the cascaded card
    /// (default = always cast). <paramref name="onCascadeResolved"/>
    /// receives the <see cref="CascadeAction.CascadeResult"/> for
    /// caller-driven free-cast through
    /// <see cref="Costs.CastFromExileAlternativeCost"/>.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        Func<ICard, bool>? willCast = null,
        Action<CascadeAction.CascadeResult>? onCascadeResolved = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Rogue });

        // CR 205.2a — Artifact Creature combines two card types. Add
        // Artifact post-construction (same pattern Arcbound Ravager /
        // Wurmcoil Engine use for the artifact creatures route).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.85 — Cascade. "When you cast this spell, exile cards
        // from the top of your library until you exile a nonland card
        // whose mana value is less than this spell's mana value …"
        // ----------------------------------------------------------------
        var cascadeCondition = new EventTriggerCondition<SpellCastEvent>(
            (e, _) => ReferenceEquals(e.Spell.Card, card));

        var cascadeEffect = new Effect(
            $"{CardName} — Cascade (CR 702.85)",
            () =>
            {
                var result = CascadeAction.Cascade(
                    controller: owner,
                    sourceManaValue: CascadeSourceManaValue,
                    willCast: willCast);
                onCascadeResolved?.Invoke(result);
            });

        var cascadeTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: cascadeCondition,
            effects: new IEffect[] { cascadeEffect },
            // Cascade trigger fires while the cascading spell is on the
            // stack — same active-zone choice as Crashing Footfalls /
            // Living End.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }
}
