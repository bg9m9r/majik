using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Three Tree Mascot (Bloomburrow, {2}).
///
/// Artifact Creature — Shapeshifter 2/1. Oracle text (verified against
/// Scryfall):
///   "Changeling (This card is every creature type.)
///    {1}: Add one mana of any color. Activate only once each turn."
///
/// ## Implemented (v1)
///
/// - <b>2/1 Artifact Creature — Shapeshifter</b> at {2} (colourless). The
///   base shape (name, Artifact + Creature types, Shapeshifter subtype, {2},
///   2/1) is materialised from the embedded JSON definition
///   (<c>three-tree-mascot.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>; the Changeling subtype
///   stamping + the any-colour mana ability are layered on here. The JSON
///   <c>types</c> array carries both Artifact and Creature so
///   <see cref="Card.HasType"/> surfaces the artifact type for affinity /
///   artifact-matters consumers (CR 301.1 / 302.1).
///
/// - <b>Changeling (CR 702.73)</b> — the card is every creature type in
///   every zone. Same v1 modelling as <see cref="UnsettledMarinerFactory"/> /
///   <see cref="MutableExplorerFactory"/>: the printed
///   <see cref="Card.Subtypes"/> set is stamped with the engine's
///   currently-enumerated creature subtypes (sourced from
///   <see cref="MutavaultAnimateEffect.EveryCreatureType"/> so the changeling
///   list stays in lockstep with Mutavault's animate list — when the enum
///   grows, both pick up the new subtype with no per-card edits) plus the
///   printed <see cref="CardSubtype.Shapeshifter"/> base type, and a
///   <see cref="KeywordAbility"/>("Changeling") marker for UI / future
///   Changeling-aware enumerations. CR 702.73a: "Each object with the
///   changeling ability is each creature type. This ability works everywhere,
///   even outside the game." — stamping the printed list models that
///   static-everywhere posture (no Layer 4 registration needed).
///
/// - <b>"{1}: Add one mana of any color. Activate only once each turn."</b>
///   (CR 605.1 — mana ability, no stack). Modelled as the combination of two
///   existing engine primitives:
///     1. The any-colour fan-out from <see cref="ShimmeringGrottoFactory"/> /
///        Mana Confluence — five sibling <see cref="ManaAbility"/> slots (one
///        per WUBRG). Bots/agents pick the colour by picking the matching
///        ability slot.
///     2. The no-tap, {1}-mana-cost, once-per-turn-locked shape from
///        <see cref="WallOfRootsFactory"/>. Three Tree Mascot's ability has NO
///        {T} component (the printed cost is the {1} alone), so each slot is
///        built on the no-tap <see cref="ManaAbility"/> overload
///        (<c>tapsAsCost: false</c>) with the {1} paid via
///        <c>additionalCostPayer</c> (the same way Shimmering Grotto pays its
///        {1} rider). The creature stays untapped.
///   <b>"Activate only once each turn"</b> (CR 602.5e) is a single per-turn
///   lock (<c>int[1] { 0 }</c>) SHARED across all five colour slots — the
///   ability is one printed ability with five colour modes, so activating any
///   colour consumes the turn's single use. The <c>canActivateCheck</c> gates
///   on <c>usedThisTurn[0] == 0 &amp;&amp; ManaPool.CanPay({1})</c>; the
///   <c>additionalCostPayer</c> deducts {1} and flips the lock; the
///   <see cref="TurnStartedEvent"/> handler installed by the
///   <c>(owner, eventBus)</c> overload resets it at the start of each new turn
///   (CR 500.1).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The mana ability is attached
///   with the per-turn gate active but the closure is never reset — first
///   activation succeeds, subsequent activations within the same lifetime are
///   blocked. Suitable for dispatcher / structural tests and single-turn
///   scenarios. This is the overload <see cref="NamedCardFactory"/> dispatches
///   to.
/// - <see cref="Create(Player, IEventBus?)"/> — fully wired. The per-turn
///   activation lock is reset at the start of every turn (CR 500.1) — mirrors
///   <see cref="WallOfRootsFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - Activation of a colour mode requires {1} to already be in the mana pool.
///   The engine doesn't auto-tap other sources to feed the {1} cost (no
///   look-ahead mana-fixer planner) — the same posture every other
///   additional-mana-cost activated mana ability takes (Shimmering Grotto,
///   filter lands, signets).
/// </summary>
[CardName("Three Tree Mascot")]
public static class ThreeTreeMascotFactory
{
    public const string CardName = "Three Tree Mascot";
    public const string Slug = "three-tree-mascot";

    /// <summary>
    /// Construct Three Tree Mascot with no event bus wiring. The
    /// once-per-turn activation gate is attached but the closure is never
    /// reset — first activation succeeds, subsequent activations within the
    /// same Creature lifetime are blocked. Suitable for card-shape /
    /// dispatcher tests and single-turn scenarios. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Three Tree Mascot with optional <see cref="TurnStartedEvent"/>
    /// reset wiring. When <paramref name="eventBus"/> is supplied, the
    /// per-turn activation lock is reset at the start of every turn
    /// (CR 500.1) — mirrors <see cref="WallOfRootsFactory"/>.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus that publishes
    /// <see cref="TurnStartedEvent"/>. May be null — the closure is then never
    /// reset and a second-activation-same-turn test is the only path the gate
    /// exercises.</param>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Artifact +
        // Creature types, Shapeshifter subtype, {2}, 2/1). The Changeling
        // subtype stamping + the any-colour mana ability are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.73a — Changeling: this card is every creature type. Stamp the
        // engine's currently-modelled creature subtype set on the printed body
        // so HasSubtype(Goblin), HasSubtype(Elf), etc. all return true
        // everywhere. Sourced from MutavaultAnimateEffect.EveryCreatureType so
        // the changeling list stays in lockstep (same posture as
        // UnsettledMarinerFactory). Shapeshifter (printed) is already present
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
        // Per-turn activation lock (CR 602.5e — "Activate only once each
        // turn"). A SINGLE lock shared across all five colour slots: the
        // printed ability is one ability with five colour modes, so
        // activating any colour consumes the turn's single use.
        // ----------------------------------------------------------------
        var usedThisTurn = new int[] { 0 };

        // ----------------------------------------------------------------
        // "{1}: Add one mana of any color." — five ManaAbility instances
        // (one per WUBRG), same any-colour fan-out as Shimmering Grotto /
        // Mana Confluence. There is NO {T} component, so each slot is built
        // on the no-tap overload (tapsAsCost: false): the {1} is paid via
        // additionalCostPayer (CR 605.1 — the {1} cost is paid as part of
        // activation), and the same call flips the shared per-turn lock.
        //   canActivateCheck: not used this turn AND controller can pay {1}.
        // ----------------------------------------------------------------
        var oneGeneric = ManaCost.Parse("1");
        foreach (var color in new[] { "W", "U", "B", "R", "G" })
        {
            var mana = ManaCost.Parse(color);
            card.AddAbility(new ManaAbility(
                source: card,
                controller: owner,
                manaGenerated: mana,
                canActivateCheck: () =>
                    usedThisTurn[0] == 0 &&
                    (card.Controller ?? owner).ManaPool.CanPay(oneGeneric),
                additionalCostPayer: p =>
                {
                    p.PayMana(oneGeneric);
                    usedThisTurn[0] = 1;
                },
                tapsAsCost: false));
        }

        // CR 500.1 — reset the per-turn activation lock at the start of each
        // turn. Mirrors WallOfRootsFactory's TurnStartedEvent subscription.
        // When no event bus is supplied the closure remains permanently set
        // after the first activation — acceptable for shape / single-turn
        // tests.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => usedThisTurn[0] = 0);
        }

        return card;
    }
}
