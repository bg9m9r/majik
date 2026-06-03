using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the COMBINED split/fuse card Beck // Call (Dragon's
/// Maze, {G}{U} // {4}{W}{U}). Both faces are Sorceries.
///
/// ## Card text (Scryfall verified 2026-06-03)
///   Beck {G}{U} — Sorcery: "Whenever a creature enters this turn, you may
///     draw a card. Fuse (You may cast one or both halves of this card from
///     your hand.)"
///   Call {4}{W}{U} — Sorcery: "Create four 1/1 white Bird creature tokens
///     with flying. Fuse ..."
///
/// ## Split-card posture (CR 712.2 / 712.4)
///
/// A split card has two faces printed on one card. The caster chooses one face
/// to cast and only that face's mana cost / effect applies (CR 712.4a). Neither
/// face is a permanent — both halves are Sorceries, so each resolves as a
/// one-shot effect that then heads to the graveyard (Beck additionally leaves a
/// turn-scoped repeating delayed trigger behind, CR 603.7e).
///
/// The combined card name "Beck // Call" is the <c>[CardName]</c> dispatch key
/// (matching the embedded seed row), mirroring the two-face posture of
/// <see cref="WearTearFactory"/> / <see cref="PushPullFactory"/>. The card
/// SHAPE is materialised from the embedded JSON definition (<c>beck-call.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>; each face's resolve-time
/// behaviour is delegated to the already-implemented single-half factories
/// (<see cref="BeckFactory"/> / <see cref="CallFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Sorcery, combined card name, carrying the front (Beck)
///   face's {G}{U} cost — the natural default for the single combined object
///   (same posture as <see cref="WearTearFactory"/> carrying the Wear cost).
///   Colours G/U/W unioned on the combined JSON so the object reports the full
///   split-card colour identity (CR 709.4).
/// - <b>Beck face</b> — the turn-scoped REPEATING delayed trigger "whenever a
///   creature enters this turn, you may draw a card" (CR 603.7e), delegated to
///   <see cref="BeckFactory.BuildResolveEffect"/>. THIS is the deferral this
///   card unblocks.
/// - <b>Call face</b> — create four 1/1 white Bird tokens with flying
///   (CR 111 / CR 702.9), delegated to <see cref="CallFactory.BuildResolveEffect"/>.
///
/// ## Deferred (v1 gap — shared with Wear // Tear / Push // Pull)
/// - <b>Fuse</b> (CR 702.102): the engine has no general split-cast / fuse
///   surface yet, so the combined object exposes the front (Beck) cost and each
///   half is castable independently via its own <c>[CardName]</c> factory
///   (<see cref="BeckFactory"/> / <see cref="CallFactory"/>). The classic
///   fuse line (Call's four Birds each drawing a card off the Beck trigger)
///   is exercisable by resolving Beck then Call against the same
///   <see cref="TriggerManager"/> + <see cref="ZoneService"/>.
/// </summary>
[CardName("Beck // Call")]
public static class BeckCallFactory
{
    public const string CardName = "Beck // Call";
    public const string Slug = "beck-call";

    /// <summary>CR 712 — Beck (front face) printed cost.</summary>
    public const string BeckManaCost = "{G}{U}";

    /// <summary>CR 712 — Call (back face) printed cost.</summary>
    public const string CallManaCost = "{4}{W}{U}";

    /// <summary>
    /// Build the combined card shape from the embedded JSON definition
    /// (Sorcery, combined name "Beck // Call"). This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to; per-face resolve behaviour
    /// is built on demand via <see cref="BuildBeckResolveEffect"/> /
    /// <see cref="BuildCallResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the resolve effect for the Beck face — register the turn-scoped
    /// repeating "whenever a creature enters this turn, you may draw a card"
    /// delayed trigger (CR 603.7e). Delegated to
    /// <see cref="BeckFactory.BuildResolveEffect"/> so the trigger wiring stays
    /// single-sourced.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildBeckResolveEffect(
        Player caster,
        TriggerManager? triggers = null,
        Func<bool>? mayDraw = null)
        => BeckFactory.BuildResolveEffect(caster, triggers, mayDraw);

    /// <summary>
    /// Build the resolve effect for the Call face — create four 1/1 white Bird
    /// tokens with flying (CR 111 / CR 702.9). Delegated to
    /// <see cref="CallFactory.BuildResolveEffect"/>.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildCallResolveEffect(
        Player caster,
        ZoneService? zoneService = null)
        => CallFactory.BuildResolveEffect(caster, zoneService);
}
