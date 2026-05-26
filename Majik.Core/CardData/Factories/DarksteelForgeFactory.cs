using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Darksteel Forge (Darksteel / Mirrodin Besieged
/// reprints, {9}).
///
/// Artifact. Oracle text:
///   "Indestructible.
///    Other artifacts you control have indestructible."
///
/// ## Implemented (v1)
/// - Artifact, mana cost {9}, owner/controller wired.
/// - <b>Indestructible</b> (CR 702.12) on Darksteel Forge itself — wired
///   as a <see cref="KeywordAbility"/> marker. Read by
///   <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>'s
///   non-creature destroy gate.
/// - <b>Static "Other artifacts you control have indestructible"</b>
///   wired via <see cref="IndestructibleGrantStaticEffect"/>: while
///   Darksteel Forge is on the battlefield, an entry is registered in
///   <see cref="Majik.Core.Rules.IndestructibleGrantRegistry"/> matching
///   any permanent on the battlefield that
///   <list type="bullet">
///     <item>is itself an artifact (CR 301.1 — covers pure Artifacts,
///           Artifact Creatures, Artifact Lands, Equipment, Vehicles
///           — anything with <see cref="CardType.Artifact"/>), AND</item>
///     <item>is controlled by Darksteel Forge's controller
///           (CR 109.5 — "you control" wording), AND</item>
///     <item>is not the Forge itself (CR 109.3 — "other").</item>
///   </list>
///   The destroy gates consult the registry alongside the printed
///   <see cref="KeywordAbility"/> markers, so other artifacts the Forge's
///   controller controls survive both creature-side SBAs
///   (<see cref="Majik.Core.Rules.Sba.Checks.CreatureDeathCheck"/>) and
///   the spell-resolution destroy primitive
///   (<see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>).
///
/// Multiple copies of Darksteel Forge stack idempotently — each registers
/// its own predicate; an artifact only needs a single matching predicate
/// to gain indestructible.
///
/// ## Deferred (v1 gaps)
/// - <b>Control-change re-eval</b>: the predicate captures the static
///   <see cref="Player"/> reference (Darksteel Forge's owner at construct
///   time) and reads it through Forge's live <c>Controller</c> on every
///   call, so control-change effects on the Forge itself are honoured;
///   control-change effects on OTHER artifacts are also honoured because
///   the predicate keys off the affected card's live
///   <see cref="ICard.Controller"/>. No follow-up needed.
/// - <b>"Indestructible" granted ability not visible via
///   <c>Permanent.Abilities</c></b>: the grant is surfaced through the
///   registry, not by attaching a <see cref="KeywordAbility"/> marker to
///   the affected artifact. Callers that enumerate
///   <see cref="ICard.Abilities"/> directly (rather than going through the
///   destroy gates) will not see the granted keyword. The Modern card
///   pool consumes "indestructible" exclusively via the destroy gates, so
///   this is structural-only for v1.
/// </summary>
[CardName("Darksteel Forge")]
public static class DarksteelForgeFactory
{
    public const string CardName = "Darksteel Forge";
    public const string PrintedManaCost = "{9}";

    /// <summary>
    /// Construct Darksteel Forge with no live event-bus wiring. The
    /// indestructible-grant lifecycle is built but never attached
    /// (so the registry isn't touched on the shape path). Suitable for
    /// dispatcher / shape tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null);

    /// <summary>
    /// Construct Darksteel Forge with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, the
    /// <see cref="IndestructibleGrantStaticEffect"/> attaches and the
    /// registry tracks the grant for the lifetime of the Forge on the
    /// battlefield (Attach is idempotent; LTB pulls the registration on
    /// the next <see cref="CardMovedEvent"/>).
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — OracleSpellBinder's
        // non-creature destroy gate reads KeywordAbility off Permanent.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Indestructible", card, owner));

        // ----------------------------------------------------------------
        // Static — "Other artifacts you control have indestructible."
        // (CR 117.1 / 702.12 / 613.1f.) The grant lifecycle keys to the
        // Forge's battlefield zone; the predicate keys off the affected
        // card's live controller (so control-change effects re-evaluate
        // naturally) and excludes the Forge itself ("other" — CR 109.3).
        // ----------------------------------------------------------------
        var grant = new IndestructibleGrantStaticEffect(
            source: card,
            eventBus: eventBus,
            predicate: c =>
            {
                if (c == null) return false;
                if (ReferenceEquals(c, card)) return false; // CR 109.3 — "other".
                if (!c.HasType(CardType.Artifact)) return false;
                // CR 109.5 — "you control" evaluated against Forge's live
                // controller (so swapping control of the Forge via Mind
                // Control / Threads of Disloyalty flips the grant set).
                var forgeController = card.Controller ?? owner;
                return ReferenceEquals(c.Controller, forgeController);
            });
        grant.Attach();

        return card;
    }
}
