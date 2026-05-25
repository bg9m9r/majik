using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
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
/// ## Implemented (v2)
/// - Instant {U} card shape, owner / controller wired. The concrete card
///   class is <see cref="IntoTheFloodMawCard"/> which extends
///   <see cref="Instant"/> and implements
///   <see cref="Majik.Core.Spells.IGiftClause"/> so
///   <see cref="Majik.Core.Game.SpellCastFlow"/> detects the gift hook
///   at cast time (CR 701.59).
/// - <see cref="BuildDefinition"/> exposes a single 1..1 target request
///   whose live candidate pool flips between "creature an opponent
///   controls" (base) and "nonland permanent an opponent controls"
///   (gift-promised) via a <see cref="TargetRequest.CandidateGatherer"/>
///   that reads <see cref="Card.HasGiftPromised"/> off the source card.
///   The base mode keeps its static <see cref="TargetRequest.LegalCandidates"/>
///   list for shape-only tests; the gatherer only fires when the cast
///   pipeline supplies a live <see cref="GameContext"/>.
/// - Resolve body branches on the same <see cref="Card.HasGiftPromised"/>
///   sentinel: base mode bounces a Creature; gift mode bounces any
///   nonland permanent (CR 701.20 — return to owner's hand).
/// - <see cref="IntoTheFloodMawCard.DeliverTo"/> is the Gift clause
///   delivery — produces a tapped 1/1 blue Fish creature token under
///   the recipient via <see cref="TokenFactory.CreateOnBattlefield"/>
///   and immediately taps it (CR 701.59c — gift token enters tapped).
///
/// ## CR 701.59 deviation — cast-time gift delivery
/// Strict CR 701.59 places gift delivery INSIDE the spell's resolution
/// ("before the spell's other effects"). The engine v1 simplification —
/// matching the test spec — delivers the gift at cast time (right after
/// the promise is recorded) so a countered gift spell still leaves the
/// promised token in the recipient's hand. Documented on
/// <see cref="Majik.Core.Spells.IGiftClause"/> + at the SpellCastFlow
/// delivery call site.
/// </summary>
[CardName("Into the Flood Maw")]
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

    /// <summary>Human-readable label for the Gift clause. Surfaced by
    /// the agent-prompt UI through
    /// <see cref="Majik.Core.Spells.IGiftClause.Description"/>.</summary>
    public const string GiftDescription = "a tapped 1/1 blue Fish creature token";

    /// <summary>
    /// Construct Into the Flood Maw as an Instant card that implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> (so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time
    /// gift hook) with owner / controller wired. The resolve
    /// SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver
    /// wire-up site (mirrors Vapor Snag / Aether Gust).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new IntoTheFloodMawCard(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Into the Flood Maw. Targets:
    /// <list type="bullet">
    ///   <item>Base mode (gift NOT promised) — "target creature an
    ///         opponent controls" (CR 701.20).</item>
    ///   <item>Gift mode (gift PROMISED) — "target nonland permanent an
    ///         opponent controls" (CR 701.20 + CR 701.59 upgrade).</item>
    /// </list>
    /// The flip is implemented by a
    /// <see cref="TargetRequest.CandidateGatherer"/> closure that reads
    /// <see cref="Card.HasGiftPromised"/> off the supplied
    /// <paramref name="card"/> at prompt time.
    /// </summary>
    /// <param name="caster">The player casting Into the Flood Maw. Used
    /// at resolve time to verify "an opponent controls" (CR 109.1 —
    /// opponent = any other player). May be null in shape tests, in
    /// which case the opponent-control gate is skipped.</param>
    /// <param name="zoneService">Optional ZoneService for replacement-
    /// bus-aware zone moves. When null, raw zone manipulation is used
    /// (shape tests / dispatcher path).</param>
    /// <param name="card">Optional source card; required for the
    /// gift-aware target gatherer (the gatherer reads
    /// <see cref="Card.HasGiftPromised"/> off this reference). When
    /// null, the request keeps the static base-mode candidate pool
    /// (shape-only path — back-compat with pre-Gift call sites).</param>
    public static SpellDefinition BuildDefinition(
        Player? caster = null,
        ZoneService? zoneService = null,
        Card? card = null) =>
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
                    BotIntent.Bounce,
                    CandidateGatherer: card == null ? null : ctx =>
                        GatherTargets(ctx, caster, card)),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — return target permanent an opponent controls to its owner's hand",
                        () => Resolve(raw, caster, zoneService, card)),
                };
            });

    /// <summary>
    /// Live candidate enumeration. When the gift is promised (read off
    /// <paramref name="card"/>.<see cref="Card.HasGiftPromised"/>) the
    /// pool is every nonland permanent an opponent controls; otherwise
    /// just creatures an opponent controls. CR 109.1 — "opponent" =
    /// any player other than <paramref name="caster"/>.
    /// </summary>
    private static IReadOnlyList<object> GatherTargets(
        GameContext ctx, Player? caster, Card card)
    {
        bool gifted = card.HasGiftPromised;
        var pool = new List<object>();
        foreach (var player in ctx.AllPlayers)
        {
            if (caster != null && ReferenceEquals(player, caster)) continue;
            foreach (var perm in player.Zones.Battlefield.GetCards())
            {
                if (perm is not Card asCard) continue;
                if (gifted)
                {
                    // Nonland permanent — any permanent type other than Land.
                    if (asCard.HasType(CardType.Land)) continue;
                    pool.Add(asCard);
                }
                else
                {
                    if (asCard is Creature) pool.Add(asCard);
                }
            }
        }
        return pool;
    }

    private static void Resolve(object raw, Player? caster, ZoneService? zoneService, Card? card)
    {
        // Bind target as Card (gift-mode covers all nonland permanent
        // types — Artifact / Enchantment / Planeswalker / Creature).
        if (raw is not Card target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        bool gifted = card?.HasGiftPromised ?? false;

        // CR 608.2b — resolution-time type gate.
        if (gifted)
        {
            // Gift mode: any nonland permanent.
            if (target.HasType(CardType.Land)) return;
        }
        else
        {
            // Base mode: must still be a creature.
            if (!target.HasType(CardType.Creature)) return;
        }

        var targetOwner = target.Owner;
        if (targetOwner == null) return;

        var controller = target.Controller ?? targetOwner;

        // CR 109.1 — "an opponent controls" gate. Skipped when no caster
        // was wired (shape tests). Self-targeting an own permanent is
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

    /// <summary>
    /// CR 701.59 Gift delivery — create the tapped 1/1 blue Fish
    /// creature token under <paramref name="recipient"/>. Routed
    /// through <see cref="TokenFactory.CreateOnBattlefield"/> so
    /// CardMovedEvent fires (Soul Warden / token-tribal triggers see
    /// the gift). The token is tapped immediately after creation
    /// (CR 701.59c — "tapped 1/1 blue Fish creature token"). No
    /// ZoneService is threaded here — the engine v1 simplification
    /// performs the create on the owning Player's zone manager only
    /// (matching <see cref="TokenFactory.CreateOnBattlefield"/>'s
    /// raw-zone code path when no ZoneService is supplied). Followup:
    /// pass ZoneService through <see cref="IGiftClause.DeliverTo"/>
    /// once Bloomburrow gift cycle grows beyond Into the Flood Maw.
    /// </summary>
    public static Creature DeliverFishGift(Player recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        var fish = TokenFactory.CreateOnBattlefield(
            new TokenFactory.TokenSpec(
                Name: "Fish",
                Power: 1,
                Toughness: 1,
                Subtypes: new[] { CardSubtype.Fish },
                Keywords: null,
                Colors: new[] { ManaColor.Blue }),
            recipient,
            zones: null);
        // CR 701.59c — gift Fish enters tapped. Tokens default to
        // untapped on ETB; flip it now so the recipient cannot use the
        // creature on the cast-priority turn (matching the printed
        // "tapped 1/1 blue Fish" phrasing).
        if (!fish.IsTapped) fish.Tap();
        return fish;
    }

    /// <summary>
    /// Concrete card class for Into the Flood Maw. Subclasses
    /// <see cref="Instant"/> and implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> picks up the gift
    /// hook at cast time. Kept nested in the factory so the Gift
    /// wiring stays adjacent to the printed-effect implementation.
    /// </summary>
    public sealed class IntoTheFloodMawCard : Instant, IGiftClause
    {
        public IntoTheFloodMawCard(string name, string manaCost) : base(name, manaCost) { }

        /// <inheritdoc />
        public string Description => GiftDescription;

        /// <inheritdoc />
        public void DeliverTo(Player recipient, Majik.Core.Spells.Spell spell)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            DeliverFishGift(recipient);
        }
    }
}
