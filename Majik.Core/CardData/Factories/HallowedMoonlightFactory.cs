using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hallowed Moonlight (Magic Origins, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Until end of turn, if a creature would enter and it wasn't cast,
///    exile it instead.
///    Draw a card."
///
/// ## Why a named factory
/// Hallowed Moonlight is the white "stop reanimation / tokens / cheat-into-
/// play" cantrip — the instant-speed, one-turn echo of Containment Priest.
/// It composes three primitives that already ship; <b>no new engine
/// mechanic is required</b>:
///
/// 1. <b>Exile-non-cast-creature predicate (CR 614)</b> — the same
///    "would enter the battlefield + creature + not cast → exile instead"
///    test that <see cref="ContainmentPriestExileReplacementEffect"/> uses,
///    reading <see cref="ZoneMoveIntent.WasCast"/> (populated by
///    <see cref="Majik.Core.Services.ZoneService"/> from the persistent
///    <see cref="Majik.Core.Cards.Card.WasCast"/> stamp set at cast time —
///    CR 113.5 / CR 400.7). Reanimation, blink, Sneak Attack, Through the
///    Breach, Aether Vial puts, Show and Tell, and token creation all leave
///    <c>WasCast = false</c>, so the replacement fires for them.
/// 2. <b>"Until end of turn" expiry (CR 514.2)</b> — the replacement is
///    registered on the supplied <see cref="ReplacementBus"/> and implements
///    <see cref="IEndOfTurnExpirable"/>, so the bus's
///    <see cref="ReplacementBus.ExpireEndOfTurn"/> cleanup-step sweep drops
///    it at end of turn. This is the exact "spell registers an EOT-expirable
///    replacement at resolution" shape proven by
///    <see cref="AngerOfTheGodsFactory"/>.
/// 3. <b>Cantrip (CR 121.1)</b> — <see cref="Fx.DrawCards"/> draws one card,
///    same as <see cref="DeadlyDisputeFactory"/>'s draw half.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}, white. Card shape comes from the
///   embedded JSON (<c>hallowed-moonlight.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Resolve</b>: register the EOT-expirable exile-instead replacement on
///   the supplied bus (when non-null), then draw one card.
///
/// ## Faithful-to-text scope note
/// The current oracle wording is "if <b>a creature</b> would enter" — there
/// is no "nontoken" qualifier (unlike Containment Priest's printed text), so
/// the predicate here intentionally does <b>not</b> exclude tokens: a token
/// creature that would enter and wasn't cast is exiled too (CR 614). The
/// scope is otherwise identical to Containment Priest's: creatures only.
///
/// ## Deferred (v1 gaps)
/// - <b>Live cantrip-draw replacement bus</b>: <see cref="Fx.DrawCards"/>
///   routes through the player's own replacement bus when one is attached
///   (CR 614 — Dredge etc.); on the shape-only unit path no per-player bus
///   is wired, matching every other JSON-backed cantrip factory.
/// </summary>
[CardName("Hallowed Moonlight")]
public static class HallowedMoonlightFactory
{
    public const string CardName = "Hallowed Moonlight";
    public const string Slug = "hallowed-moonlight";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>CR 121.1 — "Draw a card."</summary>
    public const int DrawAmount = 1;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build Hallowed Moonlight's resolve effect. Two halves, sequenced:
    ///   1. If <paramref name="replacements"/> is non-null, register an
    ///      EOT-expirable <see cref="ExileNonCastCreatureUntilEndOfTurnReplacement"/>
    ///      that rewrites any non-cast creature's enter-the-battlefield move
    ///      to exile (CR 614). When <paramref name="replacements"/> is null,
    ///      the rider half is skipped (shape-only test path) — the cantrip
    ///      still draws.
    ///   2. The caster draws one card (CR 121.1) via <see cref="Fx.DrawCards"/>.
    /// </summary>
    /// <param name="caster">The player who cast Hallowed Moonlight; draws the
    /// card. The replacement itself is global (it affects every creature
    /// entering this turn, regardless of controller — the printed text has no
    /// controller restriction).</param>
    /// <param name="replacements">The shared <see cref="ReplacementBus"/> the
    /// "until end of turn" replacement registers on. Null on shape-only test
    /// paths.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: until EOT exile non-cast creatures that would enter; draw a card.",
                () =>
                {
                    // CR 614 — register the "until end of turn" exile-instead
                    // replacement. The bus's cleanup-step sweep (CR 514.2)
                    // drops it because it is IEndOfTurnExpirable.
                    replacements?.Register<ZoneMoveIntent>(
                        new ExileNonCastCreatureUntilEndOfTurnReplacement());

                    // CR 121.1 — "Draw a card." Per-draw replacement bus when
                    // the caster has one attached; empty library stamps the
                    // SBA loss flag (CR 704.5b) without throwing.
                    Fx.DrawCards(caster, DrawAmount);
                }),
        };
    }
}

/// <summary>
/// Replacement effect (CR 614): if a creature would enter the battlefield
/// and it wasn't cast, exile it instead. Registered for the turn by
/// Hallowed Moonlight and dropped by the cleanup-step sweep (CR 514.2) via
/// <see cref="IEndOfTurnExpirable"/> — the printed "Until end of turn".
///
/// Mirrors the predicate in
/// <see cref="ContainmentPriestExileReplacementEffect"/>, minus the source-
/// on-battlefield gate (this is a turn-scoped spell effect, not a permanent's
/// printed static) and minus the token exclusion (Hallowed Moonlight's
/// current oracle text says "a creature", not "a nontoken creature").
/// <see cref="ZoneMoveIntent.WasCast"/> is the cast marker stamped by
/// <see cref="Majik.Core.Game.SpellCastFlow"/> (CR 113.5 / CR 400.7).
/// </summary>
public sealed class ExileNonCastCreatureUntilEndOfTurnReplacement
    : IReplacementEffect<ZoneMoveIntent>, IEndOfTurnExpirable
{
    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent.ToZone == ZoneType.Battlefield
        && intent.Card.HasType(CardType.Creature)
        && !intent.WasCast;

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
