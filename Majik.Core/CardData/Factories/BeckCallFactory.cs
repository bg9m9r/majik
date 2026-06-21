using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
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
/// ## Fuse (CR 702.102) — IMPLEMENTED
/// - <b>Fuse</b>: the split card cast surface (<see cref="SplitCardCast"/> +
///   <see cref="Majik.Core.Costs.FuseAlternativeCost"/>) composes both halves
///   into one fused <see cref="SpellDefinition"/>
///   (<see cref="BuildFusedDefinition"/>) paid at the combined cost
///   {4}{G}{W}{U}{U} (CR 702.102b, <see cref="FuseCost"/>). On resolution the
///   spell registers Beck's creature-ETB delayed trigger THEN creates Call's
///   four Birds (CR 702.102e) — so each of the four Birds entering off Call
///   sees the live Beck trigger and may draw a card (the classic fuse line).
///   Each half is still independently castable via its own <c>[CardName]</c>
///   factory (<see cref="BeckFactory"/> / <see cref="CallFactory"/>); the
///   single-half combined object continues to carry the front (Beck) cost.
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

    /// <summary>
    /// CR 702.102 — Fuse. Build the FUSED <see cref="SpellDefinition"/> casting
    /// BOTH halves as one split spell: register Beck's turn-scoped "whenever a
    /// creature enters this turn, you may draw a card" repeating delayed trigger
    /// (CR 603.7e) AND create four 1/1 white Bird tokens with flying (Call), in
    /// printed order (CR 702.102e). This is the CLASSIC fuse line — because
    /// Beck's trigger is registered BEFORE Call's Birds enter, each of the four
    /// Birds entering off Call sees the live Beck trigger and may draw a card.
    /// Both halves are untargeted, so the fused definition carries no target
    /// requests. Pair with <see cref="FuseCost"/> for the combined cost
    /// (CR 702.102b).
    /// </summary>
    /// <param name="caster">Spell controller — trigger owner + token
    /// controller.</param>
    /// <param name="triggers">Optional TriggerManager for Beck's delayed
    /// trigger.</param>
    /// <param name="zoneService">Optional ZoneService so each Bird publishes its
    /// ETB (so Beck's trigger sees them enter).</param>
    /// <param name="mayDraw">Optional "you may draw" gate for Beck's trigger.</param>
    public static SpellDefinition BuildFusedDefinition(
        Player caster,
        TriggerManager? triggers = null,
        ZoneService? zoneService = null,
        Func<bool>? mayDraw = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        var beck = SpellDefinition.Vanilla(
            _ => BuildBeckResolveEffect(caster, triggers, mayDraw));
        var call = SpellDefinition.Vanilla(
            _ => BuildCallResolveEffect(caster, zoneService));
        return SplitCardCast.BuildFusedDefinition(
            beck, call,
            $"{BeckFactory.CardName} — creature-ETB draw trigger",
            $"{CallFactory.CardName} — four 1/1 flying Birds");
    }

    /// <summary>CR 702.102b — the combined Fuse mana cost {4}{G}{W}{U}{U}.</summary>
    public static Majik.Core.ValueObjects.ManaCost FuseCost() =>
        SplitCardCast.FuseCost(BeckManaCost, CallManaCost);
}
