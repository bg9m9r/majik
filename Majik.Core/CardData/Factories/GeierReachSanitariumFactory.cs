using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Geier Reach Sanitarium (Eldritch Moon).
///
/// Legendary Land. Oracle text (Scryfall, verified 2026-06-02):
///   "{T}: Add {C}.
///    {2}, {T}: Each player draws a card, then discards a card."
///
/// ## Why it gets its own factory
/// A symmetric draw-one / discard-one "wheel-lite" stapled onto a colourless
/// utility land. The colourless {C} base ability is the plain Mirrodin's Core /
/// Academy Ruins shell (declared in the embedded JSON), and the activated
/// ability is the each-player draw-then-discard suite shared with Etched Oracle
/// / Burning Inquiry — except the discard here is a normal (player-chosen, not
/// "at random") discard, so it routes through <see cref="Fx.Discard"/> rather
/// than the per-game RNG. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - <b>Legendary Land</b> identity from the embedded JSON definition
///   (<c>geier-reach-sanitarium.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>, plus owner / controller wiring.
///   The Legendary supertype drives the legend rule (CR 704.5j) via the
///   engine's SBAs — no special-casing here.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, doesn't use the stack), declared in the JSON. {C} folds to one
///   generic/colourless via the mana-ability binder.
/// - <b>{2}, {T}: Each player draws a card, then discards a card.</b> — a
///   non-mana <see cref="ActivatedAbility"/> (CR 602) with two costs:
///   <see cref="ManaCostCost"/>("{2}") for the generic pips and
///   <see cref="AdditionalCost.Tap"/> on the land. On resolution every player
///   draws a card FIRST (CR 121.1 — all draws complete before any discard, so
///   the "then" sequences the two halves and the freshly-drawn card is itself
///   eligible to be discarded), then every player discards a card.
///
/// ## CR notes
/// - <b>"Each player draws a card"</b> (CR 121.1): one top-of-library draw per
///   player, performed for ALL players before any discard. Routed through
///   <see cref="Fx.DrawCards"/>, so an empty library flags the SBA loss
///   (CR 704.5b) via <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
/// - <b>"...then discards a card"</b> (CR 701.16): a normal discard — the
///   player chooses which card. v1 routes through <see cref="Fx.Discard"/>,
///   which takes the deterministic first-card-in-hand pick (agent-driven
///   choice is the same deferred gap as Faithless Looting / Liliana of the
///   Veil). An empty hand discards nothing (CR 701.16a — discard up to one).
/// - <b>APNAP order</b> (CR 101.4): "each player" effects resolve in APNAP
///   order. The effect reads every player off the live resolution context
///   (<c>ctx.Game.AllPlayers</c>) at resolution and iterates in that order —
///   same posture as Etched Oracle / the wheel family. When no live game is
///   wired (shape-only / legacy sync path) only the controller draws +
///   discards (#2551 land cleanup — no captured resolver, so correct on the
///   routed prod build).
///
/// ## Deferred (v1 gaps)
/// - <b>Player-chosen discard</b>: <see cref="Fx.Discard"/> takes the
///   first-card-in-hand deterministically rather than prompting the discarder;
///   the same gap the rest of the discard family carries.
/// </summary>
[CardName("Geier Reach Sanitarium")]
public static class GeierReachSanitariumFactory
{
    public const string CardName = "Geier Reach Sanitarium";
    public const string Slug = "geier-reach-sanitarium";
    public const int DrawCount = 1;
    public const int DiscardCount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Geier Reach Sanitarium. The {T}: Add {C} mana ability comes
    /// from the embedded JSON; the "{2}, {T}: Each player draws a card, then
    /// discards a card" activated ability is layered on structurally. The
    /// activated ability reads every player off the LIVE resolution context
    /// (<c>ctx.Game.AllPlayers</c>) at resolution and makes each draw a card
    /// (all draws first), then each discards a card; when no live game is wired
    /// (shape-only / legacy sync path) only the controller draws + discards.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}: Each player draws a card, then discards a card.
        // CR 602 — activated ability with two costs (generic pips + tap).
        // Not a mana ability (produces no mana), so it uses the stack.
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: each player draws a card, then discards a card",
            ctx =>
            {
                // "Each player" — read every player from the LIVE game at
                // resolution (ctx.Game.AllPlayers). No captured resolver, so
                // correct on the routed prod build (#2551 land cleanup). When
                // no live game is wired (shape-only / legacy sync path) the
                // controller is the only player affected.
                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { land.Controller ?? owner };

                // CR 121.1 — every player completes their draw before any
                // discard, so the freshly-drawn card is itself eligible to be
                // the discard.
                foreach (var p in players)
                {
                    if (p == null) continue;
                    Fx.DrawCards(p, DrawCount);
                }

                // CR 701.16 — then every player discards a card.
                foreach (var p in players)
                {
                    if (p == null) continue;
                    Fx.Discard(p, DiscardCount);
                }

                return ValueTask.CompletedTask;
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{2}"),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { effect }));

        return land;
    }
}
