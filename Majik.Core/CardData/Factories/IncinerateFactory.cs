using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Incinerate (Mirage / 9th Edition, {1}{R}).
///
/// Instant. Oracle text (Scryfall-verified):
///   "Incinerate deals 3 damage to any target. A creature dealt damage
///    this way can't be regenerated this turn."
///
/// Same 3-damage "any target" payload as <see cref="LightningStrikeFactory"/>
/// (also {1}{R}) and <see cref="LightningBoltFactory"/>, routed through
/// <see cref="Fx.DealDamageAny"/> so creature / player / planeswalker /
/// battle targets all resolve correctly (CR 119 / CR 306.7 / CR 115.3).
///
/// <para>
/// The "A creature dealt damage this way can't be regenerated this turn"
/// rider (CR 701.15 / CR 701.18) is a <b>no-op-equivalent</b> under the
/// current engine regen posture and is therefore not modeled here. The
/// engine has no per-creature "can't be regenerated this turn" suppression
/// flag: regeneration shields are consumed either by the SBA destroy path
/// (<see cref="Majik.Core.Effects.RegenerationShieldEffect"/> on the
/// replacement bus) or by the destroy-spell gate in
/// <see cref="OracleSpellBinder"/>. Faithfully modeling the rider would
/// require adding a new regen-suppression mechanic shared across both
/// destroy paths — out of scope for a damage-spell factory. Incinerate's
/// damage payload itself uses only existing mechanics, so the spell ships
/// playing identically to Lightning Strike; the regen rider only differs
/// from Lightning Strike against the rare creature carrying a regeneration
/// shield, which the engine cannot yet suppress.
/// </para>
/// </summary>
[CardName("Incinerate")]
public static class IncinerateFactory
{
    public const string CardName = "Incinerate";
    public const string PrintedManaCost = "{1}{R}";
    public const int Damage = 3;

    /// <summary>CardDef DSL — card shape only. Damage body is supplied at
    /// cast time via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Incinerate is cast.
    /// Single 1..1 "any target" request; on resolution deals
    /// <see cref="Damage"/> (3) damage to the chosen target through
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    // CR 119 — 3 damage to any target. The "can't be
                    // regenerated this turn" rider (CR 701.15) is a
                    // no-op-equivalent under the current engine regen
                    // posture (see type-level remarks).
                    Fx.Inline("Incinerate: 3 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }
}
