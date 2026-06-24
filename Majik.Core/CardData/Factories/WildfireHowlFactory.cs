using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Spells;
using Majik.Core.Targeting;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wildfire Howl (Tarkir: Dragonstorm, {1}{R}{R}).
///
/// Sorcery. Printed oracle text (verified against Scryfall 2026-06-24):
///   "Gift a card (You may promise an opponent a gift as you cast this
///    spell. If you do, they draw a card before its other effects.)
///    Wildfire Howl deals 2 damage to each creature. If the gift was
///    promised, instead Wildfire Howl deals 1 damage to any target and
///    2 damage to each creature."
///
/// ## Relationship to its analogues
/// - The Gift clause ("Gift a card" → recipient draws a card) is identical
///   to <see cref="LongRiversPullFactory"/> — the recipient draws one card
///   (<see cref="Fx.DrawCards"/>) and the printed body upgrades when the
///   promise is made. Wildfire Howl is therefore the second "Gift a card"
///   (draw-a-card) implementor and reuses the shared
///   <see cref="Majik.Core.Spells.IGiftClause"/> cast-time delivery contract.
/// - The unconditional half — "2 damage to each creature" — is the
///   <see cref="PyroclasmFactory"/> / <see cref="FieryCannonadeFactory"/>
///   sweeper (a 2-damage-to-each-creature wrath; CR 109.5 — "each creature"
///   reaches every battlefield).
/// - The gift-mode rider — "1 damage to any target" — is the Shock-shaped
///   any-target burn dealt through <see cref="Fx.DealDamageAny"/>
///   (CR 115.3 — "any target" = creature, player, planeswalker, or battle;
///   CR 306.7 — damage to a planeswalker becomes loyalty removal).
///
/// ## Implementation
/// - The base Sorcery shape (name / Sorcery type / {1}{R}{R} cost) is
///   materialised by the concrete <see cref="WildfireHowlCard"/> subclass —
///   a <see cref="Sorcery"/> that implements
///   <see cref="Majik.Core.Spells.IGiftClause"/> so
///   <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time gift
///   hook (CR 701.59). Mirrors
///   <see cref="LongRiversPullFactory.LongRiversPullCard"/>.
/// - <see cref="BuildDefinition"/> exposes a SINGLE 0..1 "any target"
///   request whose live candidate pool is empty in base mode and the full
///   any-target pool when the gift was promised — gated by a
///   <see cref="TargetRequest.CandidateGatherer"/> that reads
///   <see cref="Card.HasGiftPromised"/> off the source card. The gift
///   prompt is resolved by SpellCastFlow BEFORE target collection
///   (CR 701.59), so <see cref="Card.HasGiftPromised"/> is already stamped
///   when the gatherer fires — base mode collects no target, gift mode
///   collects exactly one (CR 601.2c — the printed minimum for the chosen
///   "any target" mode is 1, recorded via
///   <see cref="TargetRequest.PrintedMinTargets"/>).
/// - The resolve body (<see cref="BuildResolveEffects"/>) ALWAYS sweeps 2
///   damage to each creature; when the gift was promised it FIRST deals 1
///   damage to the chosen any-target (printed order: "1 damage to any
///   target and 2 damage to each creature"). Both halves are part of the
///   single spell resolution (CR 608.2c — a spell's instructions resolve in
///   order).
///
/// ## CR 701.59 deviation — cast-time gift delivery
/// Strict CR 701.59 places gift delivery INSIDE the spell's resolution
/// ("before its other effects"). The engine v1 simplification — matching
/// <see cref="LongRiversPullFactory"/> / <see cref="IntoTheFloodMawFactory"/>
/// and the shared <see cref="Majik.Core.Spells.IGiftClause"/> contract —
/// delivers the gift at cast time (right after the promise is recorded) so
/// a countered gift spell still leaves the promised card drawn. Documented
/// on <see cref="Majik.Core.Spells.IGiftClause"/> + at the SpellCastFlow
/// delivery call site.
///
/// ## CR notes
/// - CR 109.5 / CR 700 — "each creature" enumerates every creature on every
///   battlefield regardless of controller.
/// - CR 119.2 — non-combat damage; CR 119.3 — damage recorded by
///   <see cref="Creature.TakeDamage"/>; SBA (CR 704.5g / CreatureDeathCheck)
///   moves lethal-damaged creatures to graveyards on the next SBA pass.
/// - CR 614 — replacement effects on damage (protection, prevention) are
///   the caller's responsibility; this factory deals damage directly to keep
///   the resolve body minimal, same shape as <see cref="PyroclasmFactory"/>.
/// </summary>
[CardName("Wildfire Howl")]
public static class WildfireHowlFactory
{
    public const string CardName = "Wildfire Howl";
    public const string PrintedManaCost = "{1}{R}{R}";

    /// <summary>Sweep dealt to every creature (both modes).</summary>
    public const int SweepDamage = 2;

    /// <summary>Any-target damage dealt only when the gift was promised.</summary>
    public const int GiftTargetDamage = 1;

    /// <summary>Printed oracle text, kept here so the data-driven import
    /// path can cross-check the named factory against Scryfall.</summary>
    public const string OracleText =
        "Gift a card (You may promise an opponent a gift as you cast this " +
        "spell. If you do, they draw a card before its other effects.)\n" +
        "Wildfire Howl deals 2 damage to each creature. If the gift was " +
        "promised, instead Wildfire Howl deals 1 damage to any target and " +
        "2 damage to each creature.";

    /// <summary>Human-readable label for the Gift clause. Surfaced by the
    /// agent-prompt UI through
    /// <see cref="Majik.Core.Spells.IGiftClause.Description"/>.</summary>
    public const string GiftDescription = "a card";

    /// <summary>
    /// Construct Wildfire Howl as a Sorcery card that implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> (so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> detects the cast-time
    /// gift hook) with owner / controller wired. The resolve
    /// SpellDefinition is built on demand via <see cref="BuildDefinition"/>
    /// (mirrors Long River's Pull / Into the Flood Maw).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new WildfireHowlCard(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the SpellDefinition for Wildfire Howl. A single 0..1 "any
    /// target" request whose live candidate pool flips:
    /// <list type="bullet">
    ///   <item>Base mode (gift NOT promised) — no target; the request
    ///         gathers nothing so SpellCastFlow collects zero targets.</item>
    ///   <item>Gift mode (gift PROMISED) — "any target" (creature / player /
    ///         planeswalker / battle — CR 115.3), printed minimum 1
    ///         (CR 601.2c).</item>
    /// </list>
    /// The flip is implemented by a
    /// <see cref="TargetRequest.CandidateGatherer"/> closure that reads
    /// <see cref="Card.HasGiftPromised"/> off the supplied
    /// <paramref name="card"/> at prompt time (which SpellCastFlow stamps
    /// before target collection, CR 701.59).
    /// </summary>
    /// <param name="caster">The player casting Wildfire Howl. Threaded for
    /// symmetry with the gift-card family; the sweep + any-target burn carry
    /// no "an opponent controls" restriction so it is unused at resolve
    /// time.</param>
    /// <param name="card">Optional source card; required for the gift-aware
    /// target gatherer + resolve-time mode branch (both read
    /// <see cref="Card.HasGiftPromised"/> off this reference). When null,
    /// the request keeps the static base-mode (empty) candidate pool
    /// (shape-only path).</param>
    public static SpellDefinition BuildDefinition(
        Player? caster = null,
        Card? card = null) =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // 0..1 gift-mode "any target": optional so the BASE (no-gift)
                // mode does not gate the cast on a target (CR 601.2c — base
                // Wildfire Howl is untargeted). PrintedMinTargets = 1 records
                // that the any-target mode, once it IS active (gift promised,
                // gatherer yields candidates), demands a target.
                //
                // Description deliberately does NOT contain "any target": the
                // central TargetCandidateService synthesizes an any-target pool
                // from any description it classifies as AnyTarget when the
                // request's own gatherer comes back EMPTY (TargetCollection.
                // ResolveLivePool, synthesize-when-empty for opted-in agents).
                // That fallback would wrongly offer base-mode (un-gifted)
                // Wildfire Howl a target. Keeping the description un-classifiable
                // (TargetCategory.None) hands the gatherer full control of the
                // pool — empty in base mode, full any-target pool in gift mode.
                new TargetRequest(
                    "gift damage recipient",
                    MinTargets: 0,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    BotIntent.Burn,
                    CandidateGatherer: card == null ? null : ctx =>
                        GatherTargets(ctx, card),
                    PrintedMinTargets: 1),
            },
            EffectFactory: p =>
            {
                // Targets[0] is empty in base mode (no gift) and holds the
                // single chosen any-target in gift mode.
                var chosen = p.Targets.Count > 0 && p.Targets[0].Count > 0
                    ? p.Targets[0][0]
                    : null;
                return BuildResolveEffects(p.AllPlayers, chosen, card);
            });

    /// <summary>
    /// Live candidate enumeration for the gift-mode "any target". Empty when
    /// the gift was NOT promised (base Wildfire Howl is untargeted); the full
    /// any-target pool — every legal damage target plus every player
    /// (CR 115.3) — when the gift WAS promised (read off
    /// <paramref name="card"/>.<see cref="Card.HasGiftPromised"/>).
    /// </summary>
    private static IReadOnlyList<object> GatherTargets(GameContext ctx, Card card)
    {
        if (!card.HasGiftPromised) return Array.Empty<object>();

        // CR 115.3 / 711 — classify by EFFECTIVE types via DamageTargeting so a
        // creature-front DFC flipped to its planeswalker back is offered as a
        // planeswalker, then add every player.
        return ctx.AllPlayers
            .SelectMany(pl => pl.Zones.Battlefield.GetCards())
            .Where(DamageTargeting.IsAnyDamageTarget)
            .Cast<object>()
            .Concat(ctx.AllPlayers.Cast<object>())
            .ToList();
    }

    /// <summary>
    /// Build Wildfire Howl's resolve effects. ALWAYS sweeps
    /// <see cref="SweepDamage"/> (2) damage to every creature on every
    /// supplied player's battlefield. When the gift was promised
    /// (<paramref name="card"/>.<see cref="Card.HasGiftPromised"/>) and a
    /// target was chosen, FIRST deals <see cref="GiftTargetDamage"/> (1)
    /// damage to that any-target (printed order: "1 damage to any target and
    /// 2 damage to each creature").
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields the sweep
    /// should reach. Typically every player in the game.</param>
    /// <param name="chosenTarget">The chosen any-target (gift mode), or null
    /// in base mode. Routed through <see cref="Fx.DealDamageAny"/> so all
    /// legal target classes resolve correctly.</param>
    /// <param name="card">Source card; the gift-mode branch is gated on its
    /// <see cref="Card.HasGiftPromised"/> sentinel (defensive — a chosen
    /// target only exists when the gatherer yielded one, which only happens
    /// when the gift was promised).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffects(
        IReadOnlyList<Player>? allPlayers,
        object? chosenTarget,
        Card? card)
    {
        var players = allPlayers ?? Array.Empty<Player>();
        bool gifted = card?.HasGiftPromised ?? chosenTarget != null;
        var effects = new List<IEffect>(2);

        // CR 608.2c — printed order: the any-target 1 damage resolves before
        // the sweep when the gift was promised.
        if (gifted && chosenTarget != null)
        {
            effects.Add(new Effect(
                $"{CardName}: deal {GiftTargetDamage} damage to any target (gift mode).",
                () => Fx.DealDamageAny(chosenTarget, GiftTargetDamage)));
        }

        effects.Add(new Effect(
            $"{CardName}: deal {SweepDamage} damage to each creature.",
            () =>
            {
                // CR 109.5 / CR 700 — "each creature" reaches every creature on
                // every battlefield. Snapshot to a list before applying so any
                // same-step zone-move side effects don't disturb the
                // enumeration; SBAs run on the next priority pass and move
                // lethal-damaged creatures to graveyards.
                var seen = new HashSet<Creature>();
                foreach (var pl in players)
                {
                    foreach (var c in pl.Zones.Battlefield.GetCards().OfType<Creature>().ToList())
                    {
                        if (seen.Add(c)) c.TakeDamage(SweepDamage);
                    }
                }
            }));

        return effects;
    }

    /// <summary>
    /// CR 701.59 Gift delivery — the recipient draws a card ("Gift a card").
    /// Routed through <see cref="Fx.DrawCards"/> so the engine's
    /// draw-replacement bus (CR 614) + draw triggers see the gift draw.
    /// Shared shape with <see cref="LongRiversPullFactory.DeliverCardGift"/>.
    /// </summary>
    public static void DeliverCardGift(Player recipient)
    {
        ArgumentNullException.ThrowIfNull(recipient);
        Fx.DrawCards(recipient, 1);
    }

    /// <summary>
    /// Concrete card class for Wildfire Howl. Subclasses
    /// <see cref="Sorcery"/> and implements
    /// <see cref="Majik.Core.Spells.IGiftClause"/> so
    /// <see cref="Majik.Core.Game.SpellCastFlow"/> picks up the gift hook at
    /// cast time. Kept nested in the factory so the Gift wiring stays
    /// adjacent to the printed-effect implementation (mirrors
    /// <see cref="LongRiversPullFactory.LongRiversPullCard"/>).
    /// </summary>
    public sealed class WildfireHowlCard : Sorcery, IGiftClause
    {
        public WildfireHowlCard(string name, string manaCost) : base(name, manaCost) { }

        /// <summary>Simulation copy constructor. No extra runtime fields.</summary>
        private WildfireHowlCard(WildfireHowlCard src) : base(src) { }

        /// <inheritdoc cref="Majik.Core.Cards.Card.CloneForSim"/>
        internal override Majik.Core.Cards.Card CloneForSim() => new WildfireHowlCard(this);

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
