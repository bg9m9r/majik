using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Lackey (Outlaws of Thunder Junction,
/// {R}).
///
/// Creature — Goblin Pirate 1/2. Oracle text (verified against Scryfall):
///   "First strike, haste
///    {2}{R}, Sacrifice this creature: Draw a card and create a Treasure
///    token. (It's an artifact with "{T}, Sacrifice this token: Add one
///    mana of any color.")"
///
/// ## Why it gets its own factory
/// First strike + haste are intrinsic keywords carried declaratively by the
/// embedded JSON definition (<c>reckless-lackey.json</c>), wired as
/// <see cref="KeywordAbility"/> markers by <see cref="CardDefinitionFactory"/>
/// (their gameplay mechanics are enforced by combat / timing — CR 702.7
/// first strike, CR 702.10 haste). The single printed activated ability is
/// the residual bespoke behaviour: it combines the
/// "{2}{R}, Sacrifice this:" cost shape of
/// <see cref="ExperimentalSynthesizerFactory"/>
/// (<see cref="ManaCostCost"/> + <see cref="SacrificeSelfCost"/>) with the
/// draw-a-card + Treasure-mint resolve of <see cref="DeadlyDisputeFactory"/>
/// (<see cref="Fx.DrawCards"/> + <see cref="TokenFactory.CreateTreasure"/>).
/// All three primitives already ship — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - 1/2 Creature — Goblin Pirate, mana cost {R}, with intrinsic First
///   strike + Haste keyword markers. Base shape (name, type, subtypes,
///   P/T, keywords) materialises from the embedded JSON definition via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>"{2}{R}, Sacrifice this creature: Draw a card and create a Treasure
///   token."</b> — one <see cref="ActivatedAbility"/> (CR 602.1) whose cost
///   list is a <see cref="ManaCostCost"/>({2}{R}) plus a
///   <see cref="SacrificeSelfCost"/> (CR 701.16). NOT sorcery-speed — the
///   ability has no timing rider, so it activates at instant speed
///   (CR 602.2). On resolve: the controller draws one card (CR 121.1) via
///   <see cref="Fx.DrawCards"/> (per-draw replacement bus; an empty library
///   stamps the SBA loss flag — CR 704.5b — without throwing), then creates
///   one Treasure token under their control (CR 111.10) via
///   <see cref="TokenFactory.CreateTreasure"/> — a colourless artifact with
///   the five-option any-colour sac mana ability. No targets.
///
/// ## Rules citations
/// - CR 702.7 — First strike. CR 702.10 — Haste.
/// - CR 602.1 — activated ability. CR 701.16 — sacrifice cost.
/// - CR 121.1 — "Draw a card."
/// - CR 111.10 — Treasure token (colourless artifact, any-colour sac mana).
///
/// ## Deferred (v1 gaps)
/// - <b>Treasure tap-to-sac colour prompt</b>: uses the five-option
///   ManaAbility model shared by all Treasure tokens; the agent picks the
///   colour at mana-pick time.
/// </summary>
[CardName("Reckless Lackey")]
public static class RecklessLackeyFactory
{
    public const string CardName = "Reckless Lackey";
    public const string Slug = "reckless-lackey";
    public const string ActivatedManaCost = "{2}{R}";

    /// <summary>CR 121.1 — "Draw a card."</summary>
    public const int DrawAmount = 1;

    /// <summary>
    /// Construct Reckless Lackey with no live runtime services. The activated
    /// ability is fully wired (it needs no external service beyond the
    /// controller's zones). For Treasure-ETB <see cref="Events.CardMovedEvent"/>
    /// publishing pass the <see cref="ZoneService"/> overload. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, zoneService: null);

    /// <summary>
    /// Construct Reckless Lackey, optionally threading a
    /// <see cref="ZoneService"/> so the minted Treasure's ETB publishes a
    /// <see cref="Events.CardMovedEvent"/> (enabling downstream triggers).
    /// Null → direct zone move, suitable for unit-test / shape-only paths.
    /// </summary>
    public static Creature Create(Player owner, ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Goblin Pirate
        // 1/2 at {R}, intrinsic First strike + Haste keyword markers).
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {2}{R}, Sacrifice this creature: Draw a card and create a Treasure
        // token.
        // CR 602.1 — activated ability. CR 701.16 — sacrifice cost. No
        // timing rider → instant speed (CR 602.2). The mana pip is paid
        // through the standard ManaCostCost; the self-sacrifice through
        // SacrificeSelfCost.
        // ----------------------------------------------------------------
        var drawAndTreasureEffect = new Effect(
            $"{CardName}: draw a card and create a Treasure token.",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 121.1 — draw a card. Replacement bus per-draw; an empty
                // library stamps the SBA loss flag (CR 704.5b) without
                // throwing.
                Fx.DrawCards(controller, DrawAmount);

                // CR 111.10 — create one Treasure token: a colourless
                // artifact with the five-option any-colour sac mana ability.
                // TokenFactory.CreateTreasure handles the full spec + the
                // battlefield ETB move.
                TokenFactory.CreateTreasure(controller, zoneService);
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivatedManaCost),
                new SacrificeSelfCost(card),
            },
            effects: new IEffect[] { drawAndTreasureEffect });

        card.AddAbility(ability);

        return card;
    }
}
