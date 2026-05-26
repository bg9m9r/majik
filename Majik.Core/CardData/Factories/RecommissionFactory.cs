using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Recommission (Modern Horizons 3, {1}{W}).
///
/// Sorcery. Oracle text:
///   "Return target artifact or creature card with mana value 3 or less
///    from your graveyard to the battlefield. If a creature enters this
///    way, it enters with an additional +1/+1 counter on it."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}.
/// - On-resolve effect via <see cref="BuildResolveEffect"/>:
///     1. Scan the caster's graveyard for the first card that is either
///        an Artifact OR a Creature (CR 109.1 — the printed "your
///        graveyard" restricts to caster-owned only) AND has
///        <see cref="ICard.ManaCostValue"/>'s <c>TotalValue</c> ≤ 3
///        (CR 202.3b — mana value uses the printed total mana cost;
///        X = 0 in any zone other than the stack).
///     2. Move that card from caster's graveyard to caster's
///        battlefield via <see cref="Fx.ReturnFromGraveyardToBattlefield"/>.
///        ZoneService-routed when supplied so ETB triggers fire
///        (CR 603.6a / 701.20); raw-zone fallback otherwise.
///     3. If the returned card is a Creature, place exactly one +1/+1
///        counter via <see cref="CountersService.Add"/> so Hardened
///        Scales / Doubling Season replacements can rewrite the count
///        (CR 614). Non-creature artifacts skip this step. The "enters
///        with an additional" wording is modelled v1 as a post-move
///        counter placement — semantically equivalent for the common
///        case (no other "enters with +1/+1 counter" effect on the
///        same permanent stacking with this one); the CR-accurate
///        replacement-effect-on-ETB posture is deferred until the
///        engine grows an EnterWithCountersReplacement primitive.
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: "target artifact or creature card …
///   from your graveyard" needs an agent-driven choose-from-graveyard
///   prompt. v1 picks deterministically (first match) — same posture as
///   <see cref="ReanimateFactory"/> / Priest of Fell Rites.
/// - <b>"Enters with an additional +1/+1 counter"</b>: today the
///   counter is placed *after* the ETB rather than baked into the ETB
///   replacement (CR 614.1c). Hardened Scales / Doubling Season still
///   double the count because the placement goes through
///   <see cref="CountersService.Add"/>; the gap is purely interaction
///   with other "enters with +1/+1 counter" replacement effects on the
///   same card (e.g. Hardened Scales on top of Servo Schematic — both
///   read the printed enter-with-counter clause as one replacement
///   today rather than two). Not exercised by Recommission's printed
///   text in isolation.
/// - <b>Single-target only — mandatory pick</b>: if no legal
///   artifact / creature ≤3 MV exists in the caster's graveyard, the
///   spell still resolves and the effect is a clean no-op (CR 608.2b).
/// </summary>
[CardName("Recommission")]
public static class RecommissionFactory
{
    public const string CardName = "Recommission";
    public const string PrintedManaCost = "{1}{W}";

    /// <summary>Printed mana-value cap on the targeted graveyard
    /// card.</summary>
    public const int MaxManaValue = 3;

    /// <summary>Printed oracle text — kept here so the data-driven
    /// import path can cross-check the named factory against
    /// Scryfall.</summary>
    public const string OracleText =
        "Return target artifact or creature card with mana value 3 or " +
        "less from your graveyard to the battlefield. If a creature " +
        "enters this way, it enters with an additional +1/+1 counter " +
        "on it.";

    /// <summary>
    /// Build a Recommission sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve effect is built on demand via
    /// <see cref="BuildResolveEffect"/> so tests / integrations can
    /// splice it into a <see cref="Majik.Core.Game.SpellDefinition"/>
    /// or pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
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
    /// Build Recommission's resolve effect — reanimate the first legal
    /// artifact-or-creature card with MV ≤ 3 from the caster's
    /// graveyard, then add one +1/+1 counter if the returned card was
    /// a creature.
    /// </summary>
    /// <param name="caster">Spell controller — graveyard source +
    /// battlefield destination.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard
    /// → battlefield move routes through
    /// <see cref="ZoneService.MoveCard"/> so ETB triggers / replacements
    /// fire (CR 603.6a / 701.20).</param>
    /// <param name="replacements">Optional. When supplied the +1/+1
    /// counter placement routes through <see cref="CountersService.Add"/>
    /// with the bus so Hardened Scales / Doubling Season can rewrite
    /// the count (CR 614).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            Fx.Inline(
                $"{CardName}: reanimate target artifact/creature MV ≤ {MaxManaValue}; +1/+1 if creature",
                () => Resolve(caster, zoneService, replacements)),
        };
    }

    /// <summary>
    /// Shared resolution helper — picks the first artifact-or-creature
    /// card in the caster's graveyard with MV ≤ 3, moves it to the
    /// caster's battlefield, and (if a creature) adds one +1/+1
    /// counter. CR 117.x — "target" effect with no legal target is a
    /// clean no-op.
    /// </summary>
    private static void Resolve(
        Player caster,
        ZoneService? zoneService,
        ReplacementBus? replacements)
    {
        // "target artifact or creature card from your graveyard with
        // mana value 3 or less" — deterministic first-match pick across
        // the caster's graveyard only.
        var pick = caster.Zones.Graveyard.GetCards()
            .OfType<Card>()
            .FirstOrDefault(c =>
                (c.HasType(CardType.Artifact) || c.HasType(CardType.Creature))
                && c.ManaCostValue.TotalValue <= MaxManaValue);

        if (pick == null) return;

        // CR 701.20 / 603.6a — graveyard → caster's battlefield.
        // Fx.ReturnFromGraveyardToBattlefield routes through ZoneService
        // when supplied so ETB triggers / replacements fire on the
        // returned permanent.
        Fx.ReturnFromGraveyardToBattlefield(pick, caster, zoneService);

        // "If a creature enters this way, it enters with an additional
        // +1/+1 counter on it." — gate on Creature type (the picked
        // card may be a non-creature Artifact, which skips the counter
        // entirely). Routed through CountersService.Add so Hardened
        // Scales / Doubling Season can rewrite the count (CR 614).
        if (pick.HasType(CardType.Creature) && pick is Permanent perm)
        {
            CountersService.Add(perm, CounterType.PlusOnePlusOne, 1, replacements);
        }
    }
}
