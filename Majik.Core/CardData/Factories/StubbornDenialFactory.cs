using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stubborn Denial (Khans of Tarkir, {U}).
///
/// Instant. Oracle text:
///   "Choose one —
///    • Counter target noncreature spell unless its controller pays {1}.
///    • Ferocious — Counter that spell if you control a creature with
///      power 4 or greater."
///
/// CR 702.114 — Ferocious is a state check on the spell's controller's
/// battlefield: if any creature they control has power 4 or greater
/// (effective, not printed), the upgraded branch applies.
///
/// ## Implementation notes
///
/// At resolution:
///   1. If the target is not a noncreature spell on the stack, do
///      nothing (CR 608.2b — illegal target).
///   2. Sample the controller's battlefield for ferocious. If a
///      <see cref="ContinuousEffectsService"/> is supplied, effective
///      power comes from <see cref="ContinuousEffectsService.Compute(Permanent)"/>
///      (so CDA cards like Tarmogoyf and pump effects participate); else
///      we fall back to <see cref="Creature.BasePower"/>.
///   3. If ferocious is active, counter unconditionally.
///   4. Otherwise, consult <paramref name="willOpponentPay"/>. If the
///      opponent elects (and could) to pay {1}, the spell resolves
///      normally (we do nothing). Else, counter.
///
/// The "unless its controller pays {1}" rider is modelled by a caller-
/// supplied <see cref="Func{Boolean}"/> because the engine has no Yes/No
/// agent prompt yet. The default callback returns <c>false</c>, matching
/// the conservative "opponent can't or won't save" reading for shape-
/// only tests.
/// </summary>
public static class StubbornDenialFactory
{
    public const string CardName = "Stubborn Denial";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Ferocious threshold (CR 702.114) — controller controls a
    /// creature with power 4 or greater.
    /// </summary>
    public const int FerociousPowerThreshold = 4;

    /// <summary>
    /// Build a Stubborn Denial instant owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Ferocious check (CR 702.114). True iff <paramref name="controller"/>
    /// controls any creature whose effective power is ≥ 4.
    /// </summary>
    /// <param name="controller">Spell controller — only their battlefield
    /// participates in ferocious.</param>
    /// <param name="effects">Optional <see cref="ContinuousEffectsService"/>.
    /// When non-null, power is read via
    /// <see cref="ContinuousEffectsService.Compute(Permanent)"/> so CDA
    /// (e.g. Tarmogoyf) and continuous pump effects participate. When
    /// null, we fall back to <see cref="Creature.BasePower"/>.</param>
    public static bool IsFerociousActive(
        Player controller,
        ContinuousEffectsService? effects = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            if (card is not Creature creature) continue;
            var power = effects != null
                ? ((CreatureCharacteristics)effects.Compute(creature)).Power
                : creature.BasePower;
            if (power >= FerociousPowerThreshold) return true;
        }
        return false;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>.
    /// </summary>
    /// <param name="controller">Spell controller — battlefield sampled
    /// for ferocious.</param>
    /// <param name="resolver">Target resolver from the caller's
    /// <see cref="GameContext"/> (chosen → live stack object).</param>
    /// <param name="stack">Live stack — required to actually remove the
    /// countered spell. Null in pure-shape tests; the effect becomes a
    /// no-op.</param>
    /// <param name="effects">Optional continuous-effects service for
    /// effective-power readings (see <see cref="IsFerociousActive"/>).</param>
    /// <param name="willOpponentPay">Optional callback consulted only
    /// when ferocious is INACTIVE; returns true if the target spell's
    /// controller elects (and can) to pay {1} to save the spell. Default
    /// is "no" (spell is countered).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver,
        Majik.Core.Stack.Stack? stack,
        ContinuousEffectsService? effects = null,
        Func<bool>? willOpponentPay = null)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target noncreature spell", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = chosen.Targets[0][0];
                var resolved = resolver(raw);
                return new IEffect[]
                {
                    new Effect("Stubborn Denial: ferocious-conditional counter", () =>
                    {
                        if (stack == null || resolved is not ISpell spell) return;

                        // CR 608.2b — target must still be a noncreature spell
                        // at resolution. Creature target → do nothing.
                        if (spell.Card.HasType(CardType.Creature)) return;

                        var ferocious = IsFerociousActive(controller, effects);
                        if (!ferocious)
                        {
                            // Unless-pay rider. Default: opponent doesn't pay.
                            var paid = willOpponentPay?.Invoke() ?? false;
                            if (paid) return;
                        }

                        OracleSpellBinder.RemoveFromStack(stack, spell);
                        spell.Card.SetZone(ZoneType.Graveyard);
                    }),
                };
            });
    }
}
