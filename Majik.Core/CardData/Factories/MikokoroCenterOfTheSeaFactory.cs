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
/// Named-card factory for Mikokoro, Center of the Sea (Champions of Kamigawa).
///
/// Legendary Land. Oracle text (Scryfall, verified 2026-06-23):
///   "{T}: Add {C}.
///    {2}, {T}: Each player draws a card."
///
/// ## Why it gets its own factory
/// A symmetric group-draw ("howling mine on a stick") stapled onto a colourless
/// utility land. The colourless {C} base ability is the plain colourless-land
/// shell (declared in the embedded JSON); the activated ability is the same
/// each-player-draws suite as Geier Reach Sanitarium — minus the discard half,
/// so it is strictly simpler. No new engine mechanic is required.
///
/// ## Implemented (v1)
/// - <b>Legendary Land</b> identity from the embedded JSON definition
///   (<c>mikokoro-center-of-the-sea.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory.Build"/>, plus owner / controller wiring.
///   The Legendary supertype drives the legend rule (CR 704.5j) via the
///   engine's SBAs — no special-casing here.
/// - <b>{T}: Add {C}</b> — single <see cref="ManaAbility"/> (CR 605.1 — mana
///   ability, doesn't use the stack), declared in the JSON.
/// - <b>{2}, {T}: Each player draws a card.</b> — a non-mana
///   <see cref="ActivatedAbility"/> (CR 602) with two costs:
///   <see cref="ManaCostCost"/>("{2}") for the generic pips and
///   <see cref="AdditionalCost.Tap"/> on the land. On resolution every player
///   draws one card.
///
/// ## CR notes
/// - <b>"Each player draws a card"</b> (CR 121.1 / CR 101.4 APNAP): one
///   top-of-library draw per player. The effect reads every player off the live
///   resolution context (<c>ctx.Game.AllPlayers</c>) at resolution and iterates
///   in that order — same posture as Geier Reach Sanitarium / the wheel family.
///   When no live game is wired (shape-only / legacy sync path) only the
///   controller draws (no captured resolver, so correct on the routed prod
///   build). Routed through <see cref="Fx.DrawCards"/>, so an empty library
///   flags the SBA loss (CR 704.5b) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/>.
/// </summary>
[CardName("Mikokoro, Center of the Sea")]
public static class MikokoroCenterOfTheSeaFactory
{
    public const string CardName = "Mikokoro, Center of the Sea";
    public const string Slug = "mikokoro-center-of-the-sea";

    /// <summary>The {2} generic mana component of the group-draw ability.</summary>
    public const string ActivationCost = "{2}";

    /// <summary>Number of cards each player draws.</summary>
    public const int DrawCount = 1;

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Mikokoro, Center of the Sea. The {T}: Add {C} mana ability
    /// comes from the embedded JSON; the "{2}, {T}: Each player draws a card"
    /// activated ability is layered on structurally. The activated ability
    /// reads every player off the LIVE resolution context
    /// (<c>ctx.Game.AllPlayers</c>) at resolution and makes each draw one card;
    /// when no live game is wired (shape-only / legacy sync path) only the
    /// controller draws.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape (name, Legendary Land, {T}: Add {C}) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);
        land.SetOwner(owner);
        land.SetController(owner);

        // ----------------------------------------------------------------
        // {2}, {T}: Each player draws a card.
        // CR 602 — activated ability with two costs (generic pips + tap).
        // Not a mana ability (produces no mana), so it uses the stack.
        // ----------------------------------------------------------------
        var effect = new Effect(
            $"{CardName}: each player draws a card",
            ctx =>
            {
                // "Each player" — read every player from the LIVE game at
                // resolution (ctx.Game.AllPlayers). No captured resolver, so
                // correct on the routed prod build. When no live game is wired
                // (shape-only / legacy sync path) the controller is the only
                // player affected. APNAP order (CR 101.4) follows the live
                // player list.
                var players = ctx.Game?.AllPlayers
                    ?? (IReadOnlyList<Player>)new[] { land.Controller ?? owner };

                // CR 121.1 — each player draws one card.
                foreach (var p in players)
                {
                    if (p == null) continue;
                    Fx.DrawCards(p, DrawCount);
                }

                return ValueTask.CompletedTask;
            });

        land.AddAbility(new ActivatedAbility(
            source: land,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost(ActivationCost),
                AdditionalCost.Tap(land),
            },
            effects: new IEffect[] { effect }));

        return land;
    }
}
