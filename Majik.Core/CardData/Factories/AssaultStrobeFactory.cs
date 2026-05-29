using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Assault Strobe (Mirrodin Besieged, {R}).
///
/// Sorcery. Oracle text (verified against Scryfall):
///   "Target creature gains double strike until end of turn."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target
///   creature" request. On resolution the targeted creature gains Double
///   strike until end of turn, registered as a
///   <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at cleanup).
///
/// Mirrors <see cref="TemurBattleRageFactory"/> exactly, minus the
/// ferocious trample rider — Assault Strobe is the plain "target creature
/// gains double strike until end of turn" effect at sorcery speed.
///
/// ## Deferred (v1 gaps)
/// - <b>Illegal-target fizzle</b>: handled by the spell-cast flow at
///   resolution-time target legality (CR 608.2b); the resolve closure
///   additionally guards against a non-Creature resolver result and a
///   missing <see cref="Creature.ActiveEffects"/> service.
/// </summary>
[CardName("Assault Strobe")]
public static class AssaultStrobeFactory
{
    public const string CardName = "Assault Strobe";
    public const string PrintedManaCost = "{R}";

    /// <summary>Granted keyword — CR 702.4 Double strike.</summary>
    public const string GrantedDoubleStrike = "Double strike";

    /// <summary>
    /// Build an Assault Strobe sorcery owned by <paramref name="owner"/>.
    /// Card shape only; the resolve-time SpellDefinition is built via
    /// <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Assault Strobe is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature gains Double strike until end of turn.
    /// </summary>
    /// <param name="resolver">Target resolver from the caller's
    /// <c>GameContext</c> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Assault Strobe: gains double strike until end of turn", () =>
                    {
                        // CR 608.2b — if the target is no longer a Creature
                        // (zone-change, type-loss, etc.) or has no live
                        // continuous-effects service wired, the spell is a no-op.
                        if (raw is not Creature target) return;
                        if (target.ActiveEffects == null) return;

                        // CR 613.1c Layer 6 — keyword grant: Double strike.
                        target.ActiveEffects.Register(
                            new GrantKeywordUntilEndOfTurnEffect(target, GrantedDoubleStrike));
                    }),
                };
            });
    }
}
