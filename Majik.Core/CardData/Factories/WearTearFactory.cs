using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED split card Wear // Tear (Dragon's Maze,
/// {1}{R} // {W}). Both faces are Instants.
///
/// ## Card text (Scryfall verified 2026-06-01)
///   Wear {1}{R} — Instant: "Destroy target artifact.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///   Tear {W}    — Instant: "Destroy target enchantment.
///     Fuse (You may cast one or both halves of this card from your hand.)"
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one
/// face to cast and only that face's mana cost / effect applies (CR 712.4a).
/// Neither face is a permanent — both halves are Instants here, so each
/// resolves as a one-shot effect that then heads to the graveyard.
///
/// The combined card name "Wear // Tear" is the <c>[CardName]</c> dispatch
/// key (matching the embedded seed row), mirroring the two-face posture of
/// <see cref="FireIceFactory"/> / <see cref="BoomBustFactory"/>. The card
/// SHAPE is materialised from the embedded JSON definition
/// (<c>wear-tear.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// <see cref="SpellDefinition"/> is delegated to the already-implemented
/// single-half factories (<see cref="WearFactory"/> / <see cref="TearFactory"/>),
/// which carry the destroy-artifact / destroy-enchantment behaviour.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Instant, red, combined card name. The combined card
///   carries the front (Wear) face's {1}{R} cost — the engine's split-cast
///   plumbing selects the per-face cost when each face is cast; the printed
///   front cost is the natural default for the single combined object
///   (same posture as <see cref="FireIceFactory"/> carrying the Fire cost).
/// - <b>Wear face</b> — destroy target artifact, delegated to
///   <see cref="WearFactory.BuildDefinition"/> (CR 701.7 Destroy; CR 608.2b
///   illegal-target re-check honoured in that half's resolve body).
/// - <b>Tear face</b> — destroy target enchantment, delegated to
///   <see cref="TearFactory.BuildDefinition"/> (CR 701.7 Destroy; CR 608.2b
///   illegal-target re-check).
///
/// ## Deferred (v1 gaps — shared with Fire // Ice / Boom // Bust)
/// - <b>Fuse</b> (CR 702.102) — casting BOTH halves from hand as one split
///   spell. The engine has no split-cast / fuse cast surface yet, so the Fuse
///   keyword is informational only; the combined object exposes the front
///   (Wear) cost and each half is castable independently via its own
///   <c>[CardName]</c> factory (<see cref="WearFactory"/> /
///   <see cref="TearFactory"/>).
/// </summary>
[CardName("Wear // Tear")]
public static class WearTearFactory
{
    public const string CardName = "Wear // Tear";
    public const string Slug = "wear-tear";

    /// <summary>CR 712 — Wear (front face) printed cost.</summary>
    public const string WearManaCost = "{1}{R}";

    /// <summary>CR 712 — Tear (back face) printed cost.</summary>
    public const string TearManaCost = "{W}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Instant, red, combined name "Wear // Tear"). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; per-face resolve
    /// behaviour is built on demand via <see cref="BuildWearDefinition"/> /
    /// <see cref="BuildTearDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time definition for the Wear face: "Destroy target
    /// artifact." Delegated to <see cref="WearFactory.BuildDefinition"/> so the
    /// destroy-artifact behaviour (CR 701.7; CR 608.2b legality re-check)
    /// stays single-sourced.
    /// </summary>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildWearDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return WearFactory.BuildDefinition(resolver);
    }

    /// <summary>
    /// Build the resolve-time definition for the Tear face: "Destroy target
    /// enchantment." Delegated to <see cref="TearFactory.BuildDefinition"/> so
    /// the destroy-enchantment behaviour (CR 701.7; CR 608.2b legality
    /// re-check) stays single-sourced.
    /// </summary>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildTearDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return TearFactory.BuildDefinition(resolver);
    }
}
