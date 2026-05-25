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
/// Named-card factory for Bloodbraid Elf (Alara Reborn / Modern Horizons 2,
/// {2}{R}{G}). Creature — Elf Berserker 3/2. Oracle text:
///   "Cascade (When you cast this spell, exile cards from the top of your
///    library until you exile a nonland card that costs less. You may cast
///    it without paying its mana cost. Put the exiled cards on the bottom
///    in a random order.)
///    Haste"
///
/// ## Implemented (v1)
/// - 3/2 Creature — Elf Berserker, mana cost {2}{R}{G} (mana value 4 — the
///   cascade-into-MV-3 ceiling that makes Bloodbraid Elf the
///   archetypal Jund cascade beater; cascade exiles until a nonland card
///   with mana value &lt; 4, so anything MV 3 or lower can be cascaded into
///   off Bloodbraid).
/// - Haste (CR 702.10) printed as a <see cref="KeywordAbility"/> marker
///   (same wiring shape as Slickshot Show-Off, Arclight Phoenix, Earthshaker
///   Khenra — consumed by `CombatValidator` / `CombatAbilities` for the
///   summoning-sickness bypass).
/// - <b>Cascade triggered ability (CR 702.85)</b> on
///   <see cref="SpellCastEvent"/> for this card — mirrors
///   <see cref="ShardlessAgentFactory"/> /
///   <see cref="CrashingFootfallsFactory"/>'s shape. Mana value for the
///   exile-until-less filter is 4 (the printed total of {2}{R}{G}).
///   Resolution invokes <see cref="CascadeAction.Cascade"/> with
///   <c>sourceManaValue: 4</c>; the optional <c>willCast</c> predicate
///   forwards the controller's "you may" decision. The actual
///   alternative-cost cast (CR 702.85a — "you may cast it without paying
///   its mana cost") is driven by the caller via
///   <see cref="Costs.CastFromExileAlternativeCost"/> on the
///   <see cref="CascadeAction.CascadeResult.Eligible"/> card; this factory
///   only fires the trigger (same posture as Shardless Agent).
/// - <b>Cascade discovery</b>: the card name is registered with
///   <see cref="Players.Agents.CascadeAltCostProbe.DefaultIsCascadeCard"/>
///   so the bot's bidding heuristic / value layer sees Bloodbraid Elf as
///   a cascade card without extra wiring (joins Crashing Footfalls / Living
///   End / Shardless Agent in the ship list).
///
/// ## Deferred (v1 gaps)
/// - <b>Cascade-into-cascade</b>: when Bloodbraid Elf's cascade resolves
///   into another cascade spell (e.g. Shardless Agent at MV 3, or Violent
///   Outburst at MV 3), the secondary cascade fires when the secondary
///   spell is cast — wired automatically as long as that spell's factory
///   registers its own SpellCastEvent trigger.
/// - <b>Free-cast wiring</b>: caller-driven via the
///   <c>onCascadeResolved</c> hook (same shape as Crashing Footfalls /
///   Shardless Agent). Single-arg dispatcher path attaches the trigger
///   structurally with no SpellCastFlow wired — suitable for shape /
///   dispatcher tests.
/// </summary>
[CardName("Bloodbraid Elf")]
public static class BloodbraidElfFactory
{
    public const string CardName = "Bloodbraid Elf";
    public const string PrintedManaCost = "{2}{R}{G}";
    public const int CascadeSourceManaValue = 4;
    public const int Power = 3;
    public const int Toughness = 2;

    /// <summary>
    /// Single-arg dispatcher path. Attaches the Haste keyword + cascade
    /// trigger structurally so card shape is correct; no TriggerManager
    /// wiring.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, willCast: null, onCascadeResolved: null);

    /// <summary>
    /// Fully-wired construction. <paramref name="triggers"/> registers the
    /// cascade trigger so a <see cref="SpellCastEvent"/> for this card
    /// lands on the stack automatically. <paramref name="willCast"/> is
    /// the controller's "you may" decision on the cascaded card (default
    /// = always cast). <paramref name="onCascadeResolved"/> receives the
    /// <see cref="CascadeAction.CascadeResult"/> for caller-driven
    /// free-cast through <see cref="Costs.CastFromExileAlternativeCost"/>.
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
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Berserker });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.10 — Haste. Keyword marker consumed by the combat /
        // summoning-sickness reader. Same shape as Slickshot Show-Off /
        // Arclight Phoenix / Earthshaker Khenra's printed Haste.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

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
            // Living End / Shardless Agent.
            activeZones: new[] { ZoneType.Stack });

        card.AddAbility(cascadeTrigger);
        triggers?.RegisterTriggeredAbility(cascadeTrigger);

        return card;
    }
}
