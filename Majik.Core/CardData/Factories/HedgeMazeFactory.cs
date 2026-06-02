using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hedge Maze (Murders at Karlov Manor "surveil land"
/// dual cycle).
///
/// G/U surveil tapland. Oracle text (verified against Scryfall):
///   "This land enters tapped.
///    When this land enters, surveil 1. (Look at the top card of your
///    library. You may put it into your graveyard.)
///    {T}: Add {G} or {U}."
///
/// Type line is <c>Land — Forest Island</c>. The whole card shape is fully
/// expressible in the data schema, so this factory is a thin wrapper: it
/// loads <c>Majik.Core/CardData/Cards/hedge-maze.json</c> and builds it
/// through <see cref="CardDefinitionFactory"/>. No code-side wiring is
/// needed — unlike the painland Deserts (Abraded Bluffs) whose targeted ETB
/// damage isn't yet expressible in JSON.
///
/// ## Implemented (v1) — all from the JSON definition
/// - <b>Dual mana (CR 605.1a)</b> — two single-colour
///   <see cref="Majik.Core.Abilities.ManaAbility"/> instances producing {G}
///   and {U} ("{T}: Add {G} or {U}"). Mana abilities don't use the stack.
/// - <b>ETB surveil 1 (CR 603.6a + CR 701.43 — surveil keyword action)</b> —
///   a self-ETB <see cref="Majik.Core.Abilities.TriggeredAbility"/> whose
///   <c>surveil_self</c> effect peeks the top card and, via the controller's
///   registered agent, decides graveyard-vs-top. With no agent it defaults
///   to all-peeked-to-graveyard (same posture as the rest of the surveil
///   cycle).
///
/// ## Note on enters-tapped (CR 614.1c)
/// "This land enters tapped." is applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> from the oracle text,
/// not by this named factory — same posture as the Refuge / Temple cycles
/// and the rest of the surveil lands. The shape-only factory path therefore
/// enters untapped (no <see cref="Majik.Core.Abilities.ReplacementBus"/>
/// available here to own the replacement).
/// </summary>
[CardName("Hedge Maze")]
public static class HedgeMazeFactory
{
    public const string CardName = "Hedge Maze";
    public const string Slug = "hedge-maze";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Hedge Maze (Land — Forest Island) from its embedded JSON
    /// definition: dual {G}/{U} mana plus the ETB surveil-1 trigger.
    /// Enters-tapped (CR 614.1c) is owned by the binder layer on the
    /// production load path, not wired here.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
