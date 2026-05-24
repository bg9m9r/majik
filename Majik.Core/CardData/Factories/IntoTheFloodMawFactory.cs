using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Into the Flood Maw (Bloomburrow, {U}).
///
/// Instant. Printed oracle text:
///   "Gift a tapped Fish (You may promise an opponent a gift as you cast
///    this spell. If you do, they create a tapped 1/1 blue Fish creature
///    token before its other effects.)
///    Return target creature an opponent controls to its owner's hand.
///    If the gift was promised, instead return target nonland permanent
///    an opponent controls to its owner's hand."
///
/// ## Implemented (v1)
/// - Instant {U} card shape, owner / controller wired.
/// - <see cref="BuildDefinition"/> emits a SpellDefinition with a single
///   1..1 "target creature an opponent controls" <see cref="TargetRequest"/>
///   (BotIntent.Bounce). On resolve, the chosen target is returned to its
///   owner's hand via <see cref="ZoneService.MoveCard"/> when supplied, or
///   raw zone manipulation otherwise (CR 701.20 — return-to-hand).
/// - Resolution-time legality (CR 608.2b): target must still be a Creature
///   on the Battlefield, AND its controller must be an opponent of the
///   spell's controller. Illegal at resolution → effect does nothing.
/// - Tokens that are "returned" follow CR 111.7 / SBA 704.5d — the token
///   is moved to its owner's Hand by this effect; the
///   <see cref="Majik.Core.Rules.Sba.Checks.TokensCeaseToExistCheck"/> SBA
///   subsequently removes it from that off-battlefield zone (the
///   resolve-side bounce does NOT skip the move — the token does briefly
///   exist in the Hand zone before the next SBA pass, matching CR 111.7).
///
/// ## Deferred (v1 gaps) — Gift mechanic (CR 701.59 in the 2024 errata)
/// The "Gift a tapped Fish" clause is a cast-time choice that lets the
/// caster promise an opponent a gift; if promised, the opponent creates a
/// tapped 1/1 blue Fish creature token BEFORE the spell's other effects,
/// and Into the Flood Maw's target predicate upgrades from "creature an
/// opponent controls" to "nonland permanent an opponent controls".
///
/// Two engine primitives are missing:
///   1. <b>Gift cast-time prompt</b>: <c>SpellCastFlow</c> has no hook for
///      "may promise a gift to an opponent" at cast — the agent surface
///      cannot yet declare the gift recipient. The current
///      <see cref="ChosenSpellParams"/> shape has no Modes-like channel
///      for this binary choice.
///   2. <b>Conditional target-predicate</b>: <see cref="TargetRequest"/>
///      has no "if gift was promised, upgrade the predicate from X to Y"
///      branch; v1 fixes the predicate at definition time.
///
/// Until both land, v1 ships the printed base mode (no-gift): return
/// target creature an opponent controls to its owner's hand. Gift-mode
/// (token creation + upgraded nonland-permanent target) is documented
/// here + in MODERN_COVERAGE so the next pass adds it without rewriting
/// the dispatcher entry.
/// </summary>
public static class IntoTheFloodMawFactory
{
    public const string CardName = "Into the Flood Maw";
    public const string PrintedManaCost = "{U}";

    /// <summary>Printed oracle text, kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Gift a tapped Fish (You may promise an opponent a gift as you cast " +
        "this spell. If you do, they create a tapped 1/1 blue Fish creature " +
        "token before its other effects.)\n" +
        "Return target creature an opponent controls to its owner's hand. " +
        "If the gift was promised, instead return target nonland permanent " +
        "an opponent controls to its owner's hand.";

    /// <summary>
    /// Construct Into the Flood Maw as an Instant card with owner /
    /// controller wired. The resolve SpellDefinition is built on demand
    /// via <see cref="BuildDefinition"/> at the SpellCastFlow resolver
    /// wire-up site (mirrors Vapor Snag / Aether Gust).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the printed base-mode "return target creature an opponent
    /// controls to its owner's hand" SpellDefinition (CR 701.20).
    ///
    /// CR 608.2b: if the chosen target is no longer a creature on the
    /// battlefield controlled by an opponent of the spell's controller
    /// at resolution, the effect does nothing.
    /// </summary>
    /// <param name="caster">The player casting Into the Flood Maw. Used
    /// at resolve time to verify "an opponent controls" (CR 109.1 —
    /// opponent = any other player). May be null in shape tests, in
    /// which case the opponent-control gate is skipped.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-
    /// bus-aware zone moves. When null, raw zone manipulation is used
    /// (shape tests / dispatcher path).</param>
    public static SpellDefinition BuildDefinition(
        Player? caster = null,
        ZoneService? zoneService = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature an opponent controls",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    BotIntent.Bounce),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — return target creature an opponent controls to its owner's hand",
                        () => Resolve(raw, caster, zoneService)),
                };
            });

    private static void Resolve(object raw, Player? caster, ZoneService? zoneService)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (!target.HasType(CardType.Creature)) return;

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        var controller = target.Controller ?? targetOwner;

        // CR 109.1 — "an opponent controls" gate. Skipped when no caster
        // was wired (shape tests). Self-targeting an own creature is
        // illegal at resolution → effect does nothing.
        if (caster != null && ReferenceEquals(controller, caster)) return;

        // CR 701.20 — return to owner's hand.
        if (zoneService != null)
        {
            zoneService.MoveCard(target, ZoneType.Battlefield, ZoneType.Hand);
        }
        else
        {
            controller.Zones.Battlefield.RemoveCard(target);
            targetOwner.Zones.Hand.AddCard(target);
            target.SetZone(ZoneType.Hand);
            target.SetController(targetOwner);
        }
        // CR 111.7 / SBA 704.5d — if the target was a token, it briefly
        // exists in its owner's Hand and is then removed from that zone
        // by TokensCeaseToExistCheck on the next SBA pass. No special-
        // casing required here.
    }
}
