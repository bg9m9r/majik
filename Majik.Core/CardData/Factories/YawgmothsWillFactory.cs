using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Yawgmoth's Will (Urza's Saga, {2}{B}).
///
/// Sorcery. Oracle text (current Comp Rules):
///   "Until end of turn, you may play cards from your graveyard.
///    If a card would be put into your graveyard from anywhere this turn,
///    exile it instead."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}.
/// - Resolve effect (via <see cref="BuildResolveEffect"/>) does two things:
///     a) Stamps a <see cref="Card.GrantRuntimeGraveyardCast"/> on every
///        card currently in the controller's graveyard, granting the
///        "you may play this card from your graveyard" permission for the
///        rest of the turn (CR 118.9). The stamped cost is the card's own
///        printed mana cost — Yawgmoth's Will does not waive payment,
///        only the zone restriction. Callers cast a stamped card via
///        <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/>
///        built from that cost.
///     b) Registers a <see cref="YawgmothsWillGraveToExileReplacement"/>
///        on the supplied <see cref="ReplacementBus"/>, intercepting
///        every <see cref="ZoneMoveIntent"/> whose destination is
///        <see cref="ZoneType.Graveyard"/> and whose card's owner is
///        Yawgmoth's-Will's controller, rewriting the destination to
///        <see cref="ZoneType.Exile"/>. The replacement is flagged
///        <see cref="IEndOfTurnExpirable"/> so it self-removes on the
///        cleanup-step <see cref="ReplacementBus.ExpireEndOfTurn"/>
///        sweep (CR 514.2).
/// - Source-self exile: because the replacement is registered DURING the
///   sorcery's resolution, the sorcery's own routine post-resolve trip
///   from the stack to the graveyard funnels through the same
///   <see cref="ZoneMoveIntent"/> path and is exiled — matching the
///   classic Yawgmoth's-Will "exiles itself" behaviour.
///
/// ## Deferred (v1 gaps)
/// - <b>Cards entering the graveyard AFTER Yawgmoth's Will resolves</b>:
///   the replacement-effect side of the oracle ("from anywhere this turn,
///   exile it instead") catches them, so they never settle into the
///   graveyard at all. Therefore the cast-from-grave grant naturally
///   does not need to be re-stamped on each new entrant — there are no
///   new entrants. This is correct behaviour, not a gap.
/// - <b>Stamp-clear on EOT</b>: the per-card
///   <see cref="Card.RuntimeGraveyardCastCost"/> stamps are not cleared
///   at end of turn. Since the cards no longer match the
///   <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/> caster
///   ownership check after they leave the graveyard (and any card that
///   stays in graveyard is, in practice, still gated by the engine's
///   absence of "any time you could cast a sorcery" sorcery-speed-only
///   timing on instants vs sorceries from grave), the stamp is benign
///   past EOT. A bus-aware overload could clear it; deferred.
/// - <b>"During each of your turns" timing</b>: Yawgmoth's Will only
///   functions on its controller's turn (oracle: "Until end of turn,
///   you may play cards from your graveyard"). Since the sorcery itself
///   can only be cast on the controller's main phase, "until end of
///   turn" already constrains the grant to the controller's turn.
/// - <b>Sorcery-speed restriction on grave-cast instants</b>: Yawgmoth's
///   Will lets you play instants from the graveyard at instant speed and
///   sorceries at sorcery speed (CR 117). The shared
///   <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/> defers
///   timing-restriction checks to the engine's normal cast-speed
///   machinery; nothing extra is required here.
/// </summary>
public static class YawgmothsWillFactory
{
    public const string CardName = "Yawgmoth's Will";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>
    /// Build a Yawgmoth's Will sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect (graveyard-cast grants +
    /// grave-to-exile replacement) is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can
    /// splice it into a <see cref="Majik.Core.Game.SpellDefinition"/> or
    /// pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Yawgmoth's Will's resolve effect. Two halves:
    ///   1. Stamp <see cref="Card.GrantRuntimeGraveyardCast"/> on every
    ///      card in <paramref name="caster"/>'s graveyard with that
    ///      card's printed mana cost. Callers compose the stamped cost
    ///      with a <see cref="Majik.Core.Costs.GraveyardCastAlternativeCost"/>
    ///      to actually cast.
    ///   2. Register a <see cref="YawgmothsWillGraveToExileReplacement"/>
    ///      on the supplied <paramref name="replacements"/> bus. When
    ///      null, the grave-to-exile half is skipped (suitable for the
    ///      simplest shape tests). The replacement is EOT-expirable
    ///      and will be dropped by the bus's
    ///      <see cref="ReplacementBus.ExpireEndOfTurn"/> sweep.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect("Yawgmoth's Will: grant grave-cast + grave→exile replacement until EOT.", () =>
            {
                // -----------------------------------------------------------
                // CR 118.9 — "you may play cards from your graveyard". Stamp
                // a runtime grave-cast grant on every card currently in the
                // caster's graveyard. The granted cost is the card's own
                // printed mana cost (Yawgmoth's Will does not waive the
                // mana cost, only the zone restriction).
                // -----------------------------------------------------------
                foreach (var c in caster.Zones.Graveyard.GetCards().ToList())
                {
                    if (c is Card concrete)
                    {
                        concrete.GrantRuntimeGraveyardCast(concrete.ManaCostValue);
                    }
                }

                // -----------------------------------------------------------
                // CR 614 — "If a card would be put into your graveyard from
                // anywhere this turn, exile it instead." Funnel through the
                // ReplacementBus on the ZoneMoveIntent stream. The
                // replacement is EOT-expirable; the bus's cleanup-step
                // sweep removes it automatically.
                // -----------------------------------------------------------
                if (replacements != null)
                {
                    replacements.Register<ZoneMoveIntent>(
                        new YawgmothsWillGraveToExileReplacement(caster));
                }
            }),
        };
    }
}

/// <summary>
/// Replacement effect: when a card owned by Yawgmoth's-Will's controller
/// would be put into a graveyard from anywhere, rewrite the destination
/// to <see cref="ZoneType.Exile"/>. EOT-expirable — the bus's cleanup-
/// step sweep drops it at the end of the turn Yawgmoth's Will resolved.
/// </summary>
public sealed class YawgmothsWillGraveToExileReplacement
    : IReplacementEffect<ZoneMoveIntent>, IEndOfTurnExpirable
{
    private readonly Player _controller;

    public YawgmothsWillGraveToExileReplacement(Player controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    public bool OneShot => false;
    public object? Tag => this;
    public bool ExpiresAtEndOfTurn => true;

    /// <summary>The player whose cards are rewritten by this replacement.</summary>
    public Player Controller => _controller;

    public bool Applies(ZoneMoveIntent intent, IReadOnlyList<object> history)
    {
        if (intent.ToZone != ZoneType.Graveyard) return false;
        var cardOwner = intent.Card.Owner;
        if (cardOwner == null) return false;
        return ReferenceEquals(cardOwner, _controller);
    }

    public ZoneMoveIntent? Replace(ZoneMoveIntent intent, IReadOnlyList<object> history) =>
        intent with { ToZone = ZoneType.Exile };
}
