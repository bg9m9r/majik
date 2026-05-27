using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Doubling Season (Ravnica, {4}{G}).
///
/// Enchantment. Oracle text:
///   "If an effect would create one or more tokens under your control,
///    it creates twice that many of those tokens instead.
///    If an effect would put one or more counters on a permanent you
///    control, it puts twice that many of those counters on that
///    permanent instead."
///
/// ## Implementation
///
/// Two independent CR 614 replacements:
///
/// 1. <b>Token half</b> — <see cref="TokenDoublerReplacement"/> gated
///    on controller-match. Identical wiring to Parallel Lives /
///    Anointed Procession; the three doublers stack multiplicatively
///    via CR 616.1c per-effect dedup.
/// 2. <b>Counter half</b> — <see cref="CounterAddIntent"/> doubler
///    (inline <see cref="LambdaReplacement{T}"/>) gated on
///    target.Controller == owner. Stacks with Hardened Scales
///    (CR 616.1c — each fires once per intent, so Doubling Season's
///    "twice" applies before/after Hardened Scales' "+1" depending
///    on registration order; the affected-player ordering prompt is
///    a known v1 gap).
///
/// ## Caller integration
///
/// Token creation must route through the bus-aware
/// <c>TokenFactory.CreateOnBattlefield(spec, controller, count, zones, replacements)</c>
/// overload; counter placement must route through
/// <see cref="Majik.Core.Services.CountersService.Add"/>.
///
/// ## Stacking examples
///
/// - Doubling Season alone, ship-1 token → 2 tokens.
/// - Doubling Season + Parallel Lives, ship-1 → 4 tokens (multiplicative).
/// - Doubling Season + Anointed Procession + Parallel Lives, ship-1
///   → 8 tokens (three independent fires, 1 → 2 → 4 → 8).
/// - Doubling Season alone, place 1 +1/+1 counter → 2 counters.
/// - Doubling Season + Hardened Scales, place 1 → 3 counters
///   (Doubling Season doubles to 2, Hardened Scales adds 1; or
///   Hardened Scales adds 1 = 2, Doubling Season doubles to 4 —
///   ordering is the affected-player's choice, deferred to v2).
/// </summary>
[CardName("Doubling Season")]
public static class DoublingSeasonFactory
{
    public const string CardName = "Doubling Season";
    public const string PrintedManaCost = "{4}{G}";

    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(
            CardName,
            PrintedManaCost,
            supertypes: null,
            subtypes: null);
        card.SetOwner(owner);
        card.SetController(owner);

        if (replacements != null)
        {
            // Token half — CR 111.10 / "creates twice that many".
            replacements.Register<TokenCreationIntent>(new TokenDoublerReplacement(
                intent => card.Zone == ZoneType.Battlefield
                          && ReferenceEquals(intent.Controller, owner)));

            // Counter half — CR 121.2 / "puts twice that many of those
            // counters on that permanent instead". Gated on the target's
            // controller (the permanent receiving the counters).
            replacements.Register<CounterAddIntent>(new LambdaReplacement<CounterAddIntent>(
                applies: (intent, _) =>
                    intent.Amount > 0
                    && intent.Target.Zone == ZoneType.Battlefield
                    && ReferenceEquals(intent.Target.Controller, owner)
                    && card.Zone == ZoneType.Battlefield,
                replace: (intent, _) => intent with { Amount = intent.Amount * 2 },
                oneShot: false,
                tag: card));
        }

        return card;
    }
}
