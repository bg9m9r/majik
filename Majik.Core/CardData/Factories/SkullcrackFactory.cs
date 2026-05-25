using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skullcrack (Avacyn Restored, {1}{R}).
///
/// Instant. Oracle text:
///   "Target player can't gain life this turn. Damage can't be prevented
///    this turn. Skullcrack deals 3 damage to that player."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{R}.
/// - 1..1 "target player" request (intent <see cref="BotIntent.Burn"/>).
/// - Resolution registers an EOT-expirable <see cref="LifeGainIntent"/>
///   replacement on the supplied <see cref="ReplacementBus"/> that scopes
///   to the chosen player and rewrites every gain to zero (CR 614 / CR 119.6).
///   Mirrors the shape of Roiling Vortex's "players can't gain life"
///   static, but per-target + EOT-expirable.
/// - Resolution then drains 3 life from the chosen player via
///   <see cref="SearingBlazeFactory.DealDamageWithPlaneswalker"/> — same
///   non-combat damage route used by every burn-instant in the pool.
///
/// ## "Damage can't be prevented" rider
/// CR 615 — Skullcrack also disables prevention effects for the turn.
/// v1 ships this as a documented no-op marker: prevention effects in
/// majik.core today are scoped to opt-in shields (Fog,
/// <see cref="PreventNextNDamageToAnyTargetShield"/>, etc.) that callers
/// register explicitly — there's no general "prevent the next N damage"
/// surface that an opponent's pre-existing replacement might pin onto
/// Skullcrack's damage. The clause is matched + dropped as a noop rider
/// by <c>ClauseCompositionTemplate</c>'s "this damage can't be prevented"
/// regex (the/this variants); ditto the broader "damage can't be
/// prevented this turn" wording — Skullcrack's named-factory binding
/// inherits the same posture. When a turn-scoped prevention surface
/// ships, this factory can register a counter-shield against it.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. Suitable for
///   dispatcher / structural tests.
/// - <see cref="BuildDefinition"/> — fully-wired SpellDefinition that
///   the caller threads through <see cref="SpellCastFlow"/>; takes the
///   live <see cref="ReplacementBus"/> for the life-gain blocker.
///   Without a bus the life-gain rider silently no-ops (mirrors
///   Roiling Vortex's single-arg dispatcher posture).
/// </summary>
[CardName("Skullcrack")]
public static class SkullcrackFactory
{
    public const string CardName = "Skullcrack";
    public const string PrintedManaCost = "{1}{R}";
    public const int DamageAmount = 3;

    /// <summary>
    /// Build a Skullcrack instant owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time SpellDefinition is built via
    /// <see cref="BuildDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Skullcrack is cast.
    /// Single 1..1 "target player" request; on resolution the chosen player
    /// is locked out of life-gain for the turn (CR 614 — EOT-expirable
    /// replacement on <paramref name="replacements"/> when supplied) and
    /// takes 3 damage.
    /// </summary>
    /// <param name="targetResolver">Resolves the chosen target slot to a
    /// live game object (typically a <c>StackResolver</c>).</param>
    /// <param name="replacements">Replacement bus — when supplied a per-target
    /// EOT-expirable <see cref="LifeGainIntent"/> replacement is registered
    /// that rewrites every gain to that player to zero (CR 614). Without a
    /// bus the life-gain rider silently no-ops.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn),
            },
            EffectFactory: chosen =>
            {
                if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0)
                {
                    // CR 608.2b — fizzle on missing target.
                    return Array.Empty<IEffect>();
                }
                var raw = targetResolver(chosen.Targets[0][0]);
                if (raw is not Player target) return Array.Empty<IEffect>();

                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: target player can't gain life this turn",
                        () =>
                        {
                            if (replacements == null) return;
                            // CR 614 / CR 119.6 — per-target EOT-expirable
                            // life-gain blocker. Mirrors Roiling Vortex's
                            // global posture, scoped to the chosen player
                            // and dropped at cleanup via IEndOfTurnExpirable.
                            replacements.Register(new EotLambdaReplacement<LifeGainIntent>(
                                applies: (intent, _) => ReferenceEquals(intent.Target, target),
                                replace: (intent, _) => intent with { Amount = 0 },
                                tag: $"Skullcrack:NoGain:{target.Name}"));
                        }),
                    new Effect(
                        $"{CardName}: deal {DamageAmount} damage to that player",
                        () =>
                        {
                            // CR 615 — "Damage can't be prevented this turn"
                            // is a no-op in v1 (see factory doc): no turn-scoped
                            // prevention surface exists for the damage path
                            // we route through. SearingBlazeFactory.DealDamage…
                            // is the standard non-combat damage route.
                            SearingBlazeFactory.DealDamageWithPlaneswalker(target, DamageAmount);
                        }),
                };
            });
    }
}

/// <summary>
/// EOT-expirable <see cref="LambdaReplacement{TIntent}"/> — same shape as
/// <see cref="LambdaReplacement{TIntent}"/> but opts into
/// <see cref="IEndOfTurnExpirable"/> so <see cref="ReplacementBus.ExpireEndOfTurn"/>
/// drops it at cleanup (CR 514.2). Used by per-turn replacement riders
/// like Skullcrack's "target player can't gain life this turn" and
/// Atarka's Command mode 0's identical clause.
/// </summary>
public sealed class EotLambdaReplacement<TIntent>
    : IReplacementEffect<TIntent>, IEndOfTurnExpirable
    where TIntent : class
{
    private readonly Func<TIntent, IReadOnlyList<object>, bool> _applies;
    private readonly Func<TIntent, IReadOnlyList<object>, TIntent?> _replace;

    public bool OneShot { get; }
    public object? Tag { get; }
    public bool ExpiresAtEndOfTurn => true;

    public EotLambdaReplacement(
        Func<TIntent, IReadOnlyList<object>, bool> applies,
        Func<TIntent, IReadOnlyList<object>, TIntent?> replace,
        bool oneShot = false,
        object? tag = null)
    {
        _applies = applies ?? throw new ArgumentNullException(nameof(applies));
        _replace = replace ?? throw new ArgumentNullException(nameof(replace));
        OneShot = oneShot;
        Tag = tag;
    }

    public bool Applies(TIntent intent, IReadOnlyList<object> history) =>
        _applies(intent, history);

    public TIntent? Replace(TIntent intent, IReadOnlyList<object> history) =>
        _replace(intent, history);
}
