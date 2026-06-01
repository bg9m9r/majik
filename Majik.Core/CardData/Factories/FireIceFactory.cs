using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED split card Fire // Ice (Apocalypse /
/// various reprints, {1}{R} // {1}{U}). Both faces are Instants.
///
/// ## Card text (Scryfall verified)
///   Fire {1}{R} — Instant: "Fire deals 2 damage divided as you choose among
///     one or two targets."
///   Ice {1}{U} — Instant: "Tap target permanent. Draw a card."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one
/// face to cast and only that face's mana cost / effect applies (CR 712.4a).
/// Neither face is a permanent — both halves are Instants here, so each
/// resolves as a one-shot effect that then heads to the graveyard.
///
/// The combined card name "Fire // Ice" is the <c>[CardName]</c> dispatch
/// key (matching the embedded seed row), mirroring the two-face posture of
/// <see cref="BoomBustFactory"/>. The card SHAPE is materialised from the
/// embedded JSON definition (<c>fire-ice.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// <see cref="SpellDefinition"/> is delegated to the already-implemented
/// single-half factories (<see cref="FireFactory"/> / <see cref="IceFactory"/>),
/// which carry the divide-damage, tap-target, and draw behaviour.
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Instant, red, combined card name. The combined card
///   carries the front (Fire) face's {1}{R} cost — the engine's split-cast
///   plumbing selects the per-face cost when each face is cast; the printed
///   front cost is the natural default for the single combined object
///   (same posture as <see cref="BoomBustFactory"/> carrying the Boom cost).
/// - <b>Fire face</b> — divided 2 damage among one or two "any target"s,
///   delegated to <see cref="FireFactory.BuildSpellDefinition"/> (CR 119.4
///   division; CR 120.3 / CR 306.7 damage routing; CR 608.2b illegal-target
///   re-check).
/// - <b>Ice face</b> — tap target permanent (CR 701.27) then the caster draws
///   a card (CR 121.1), delegated to <see cref="IceFactory.BuildSpellDefinition"/>.
///   The untargeted draw resolves regardless of the tap target's legality
///   (CR 608.2c).
///
/// ## Deferred (v1 gaps)
/// - <b>Per-face cast cost selection.</b> The combined object exposes the Fire
///   front cost; selecting {1}{U} for Ice is the split-card cast-plumbing's
///   job. The per-face resolve definitions here are independent of how the
///   cast cost is chosen.
/// - <b>Agent-driven divide-damage prompt</b> (CR 601.2d) — inherited from
///   <see cref="FireFactory"/>; the caller-supplied distribute delegate is the
///   stand-in.
/// </summary>
[CardName("Fire // Ice")]
public static class FireIceFactory
{
    public const string CardName = "Fire // Ice";
    public const string Slug = "fire-ice";

    /// <summary>CR 712 — Fire (front face) printed cost.</summary>
    public const string FireManaCost = "{1}{R}";

    /// <summary>CR 712 — Ice (back face) printed cost.</summary>
    public const string IceManaCost = "{1}{U}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Instant, red, combined name "Fire // Ice"). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; per-face resolve
    /// behaviour is built on demand via <see cref="BuildFireDefinition"/> /
    /// <see cref="BuildIceDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve-time definition for the Fire face: "Fire deals 2
    /// damage divided as you choose among one or two targets." Delegated to
    /// <see cref="FireFactory.BuildSpellDefinition"/> so the divide-damage
    /// behaviour stays single-sourced.
    /// </summary>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    /// <param name="distribute">Optional per-target allocation strategy (see
    /// <see cref="FireFactory.BuildSpellDefinition"/>).</param>
    public static SpellDefinition BuildFireDefinition(
        Func<object, object> resolver,
        Func<IReadOnlyList<object>, IReadOnlyDictionary<object, int>>? distribute = null)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return FireFactory.BuildSpellDefinition(resolver, distribute);
    }

    /// <summary>
    /// Build the resolve-time definition for the Ice face: "Tap target
    /// permanent. Draw a card." Delegated to
    /// <see cref="IceFactory.BuildSpellDefinition"/> so the tap + draw
    /// behaviour stays single-sourced.
    /// </summary>
    /// <param name="caster">The player casting Ice; receives the draw.</param>
    /// <param name="resolver">Resolves a chosen target token to the live game
    /// object.</param>
    public static SpellDefinition BuildIceDefinition(
        Player caster,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);
        return IceFactory.BuildSpellDefinition(caster, resolver);
    }
}
