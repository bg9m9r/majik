using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Long River's Pull (Tarkir: Dragonstorm, {U}{U}).
///
/// Instant. Printed oracle text (verified against Scryfall):
///   "Gift a card (You may promise an opponent a gift as you cast this
///    spell. If you do, they draw a card before its other effects.)
///    Counter target creature spell. If the gift was promised, instead
///    counter target spell."
///
/// ## Implemented
/// - Instant {U}{U} (Blue) card shape, owner / controller wired. The
///   concrete card class is <see cref="LongRiversPullCard"/> which extends
///   <see cref="Instant"/> and implements
///   <see cref="Majik.Core.Spells.IGiftClause"/> so
///   <see cref="Majik.Core.Game.SpellCastFlow"/> detects the gift hook at
///   cast time (CR 701.59). Mirrors
///   <see cref="IntoTheFloodMawFactory"/> — the first Gift implementor.
/// - <see cref="BuildDefinition"/> exposes a single 1..1 "target spell"
///   request whose live candidate pool flips between "creature spell"
///   (base) and "any spell" (gift-promised) via a
///   <see cref="TargetRequest.CandidateGatherer"/> that reads
///   <see cref="Card.HasGiftPromised"/> off the source card. The gatherer
///   only fires when the cast pipeline supplies a live
///   <see cref="GameContext"/> with a <c>Stack</c>.
/// - Resolve body branches on the same <see cref="Card.HasGiftPromised"/>
///   sentinel: base mode counters the target only if it is a creature
///   spell (CR 608.2b — a noncreature spell is an illegal target and
///   survives); gift mode counters any spell (CR 701.5).
/// - The counter itself reuses <see cref="OracleSpellBinder.RemoveFromStack"/>
///   + graveyard tail (CR 701.5), honouring uncounterable spells
///   (CR 701.5b) — same as <see cref="CounterspellFactory"/> /
///   <see cref="NegateFactory"/>.
/// - <see cref="LongRiversPullCard.DeliverTo"/> is the Gift clause delivery
///   — the recipient draws a card via <see cref="Fx.DrawCards"/>
///   (CR 701.59 — "Gift a card").
///
/// ## CR 701.59 deviation — cast-time gift delivery
/// Strict CR 701.59 places gift delivery INSIDE the spell's resolution
/// ("before its other effects"). The engine v1 simplification — matching
/// <see cref="IntoTheFloodMawFactory"/> and the shared
/// <see cref="Majik.Core.Spells.IGiftClause"/> contract — delivers the
/// gift at cast time (right after the promise is recorded) so a countered
/// gift spell still leaves the promised card drawn. Documented on
/// <see cref="Majik.Core.Spells.IGiftClause"/> + at the SpellCastFlow
/// delivery call site.
/// </summary>
[CardName("Long River's Pull")]
public static class LongRiversPullFactory
{
    public const string CardName = "Long River's Pull";
    public const string PrintedManaCost = "{U}{U}";

    /// <summary>Printed oracle text, kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Gift a card (You may promise an opponent a gift as you cast this " +
        "spell. If you do, they draw a card before its other effects.)\n" +
        "Counter target creature spell. If the gift was promised, instead " +
        "counter target spell.";

    /// <summary>Human-readable label for the Gift clause. Surfaced by the
    /// agent-prompt UI through
    /// <see cref="Majik.Core.Spells.IGiftClause.Description"/>.</summary>
    public const string GiftDescription = "a card";

    /// <summary>
    /// Construct Long River's Pull as an Instant card that implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> (so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time
    /// gift hook) with owner / controller wired. The resolve
    /// SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up
    /// site (mirrors Into the Flood Maw / Counterspell).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new LongRiversPullCard(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Long River's Pull. A single 1..1
    /// "target spell" request whose live candidate pool flips:
    /// <list type="bullet">
    ///   <item>Base mode (gift NOT promised) — "target creature spell"
    ///         (CR 701.5).</item>
    ///   <item>Gift mode (gift PROMISED) — "target spell" (any spell on
    ///         the stack — CR 701.5 + CR 701.59 upgrade).</item>
    /// </list>
    /// The flip is implemented by a
    /// <see cref="TargetRequest.CandidateGatherer"/> closure that reads
    /// <see cref="Card.HasGiftPromised"/> off the supplied
    /// <paramref name="card"/> at prompt time.
    /// </summary>
    /// <param name="caster">The player casting Long River's Pull. Currently
    /// unused at resolve time (any spell controller may be targeted — a
    /// counter has no "an opponent controls" restriction) but threaded for
    /// symmetry with the Into the Flood Maw shape and future use.</param>
    /// <param name="stack">Live stack — required to remove the countered
    /// spell. Null in pure-shape tests; the effect becomes a no-op.</param>
    /// <param name="card">Optional source card; required for the
    /// gift-aware target gatherer + resolve-time mode branch (both read
    /// <see cref="Card.HasGiftPromised"/> off this reference). When null,
    /// the request keeps the static base-mode (empty) candidate pool.</param>
    public static SpellDefinition BuildDefinition(
        Player? caster = null,
        Majik.Core.Stack.Stack? stack = null,
        Card? card = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature spell",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    BotIntent.Counter,
                    CandidateGatherer: card == null ? null : ctx =>
                        GatherTargets(ctx, card)),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName} — counter target spell",
                        () => Resolve(raw, stack, card)),
                };
            });

    /// <summary>
    /// Live candidate enumeration over the stack. When the gift is promised
    /// (read off <paramref name="card"/>.<see cref="Card.HasGiftPromised"/>)
    /// the pool is every spell on the stack; otherwise only creature spells
    /// (CR 601.2c — choose-time legality).
    /// </summary>
    private static IReadOnlyList<object> GatherTargets(GameContext ctx, Card card)
    {
        bool gifted = card.HasGiftPromised;
        return ctx.Stack.GetAll()
            .OfType<ISpell>()
            .Where(s => gifted || IsCreatureSpell(s))
            .Cast<object>()
            .ToList();
    }

    /// <summary>CR 302.1 — a creature spell is a spell whose card has the
    /// Creature card type.</summary>
    private static bool IsCreatureSpell(ISpell spell) =>
        spell.Card is Card c && c.HasType(CardType.Creature);

    private static void Resolve(object raw, Majik.Core.Stack.Stack? stack, Card? card)
    {
        if (stack == null || raw is not ISpell spell) return;

        bool gifted = card?.HasGiftPromised ?? false;

        // CR 608.2b — resolution-time legality. Base mode only counters a
        // creature spell; if the chosen target is no longer a creature
        // spell, the effect does nothing for it (the spell stays on the
        // stack). Gift mode counters any spell.
        if (!gifted && !IsCreatureSpell(spell)) return;

        // CR 701.5 / CR 701.5b — remove from the stack unless the spell is
        // uncounterable. Gate the graveyard tail on the return so an
        // uncounterable spell resolves normally instead of being silently
        // binned while the stack still references it.
        if (!OracleSpellBinder.RemoveFromStack(stack, spell)) return;
        spell.Card.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// CR 701.59 Gift delivery — the recipient draws a card ("Gift a
    /// card"). Routed through <see cref="Fx.DrawCards"/> so the engine's
    /// draw-replacement bus (CR 614) + draw triggers see the gift draw.
    /// </summary>
    public static void DeliverCardGift(Player recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        Fx.DrawCards(recipient, 1);
    }

    /// <summary>
    /// Concrete card class for Long River's Pull. Subclasses
    /// <see cref="Instant"/> and implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> picks up the gift hook
    /// at cast time. Kept nested in the factory so the Gift wiring stays
    /// adjacent to the printed-effect implementation (mirrors
    /// <see cref="IntoTheFloodMawFactory.IntoTheFloodMawCard"/>).
    /// </summary>
    public sealed class LongRiversPullCard : Instant, IGiftClause
    {
        public LongRiversPullCard(string name, string manaCost) : base(name, manaCost) { }

        /// <summary>Simulation copy constructor. No extra runtime fields.</summary>
        private LongRiversPullCard(LongRiversPullCard src) : base(src) { }

        /// <inheritdoc cref="Majik.Core.Cards.Card.CloneForSim"/>
        internal override Majik.Core.Cards.Card CloneForSim() => new LongRiversPullCard(this);

        /// <inheritdoc />
        public string Description => GiftDescription;

        /// <inheritdoc />
        public void DeliverTo(Player recipient, Majik.Core.Spells.Spell spell)
        {
            ArgumentNullException.ThrowIfNull(recipient);
            DeliverCardGift(recipient);
        }
    }
}
