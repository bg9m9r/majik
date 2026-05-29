using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Spikefield Hazard // Spikefield Cave (Zendikar Rising, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Spikefield Hazard deals 1 damage to any target. If a permanent dealt
///    damage this way would die this turn, exile it instead."
///
/// Back face — <see cref="SpikefieldCaveFactory"/> (Land — "This land
/// enters tapped." / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face modelled by two independent <c>[CardName]</c>-dispatched
/// factories — same architecture as
/// <see cref="ShatterskullSmashingFactory"/> /
/// <see cref="ShatterskullTheHammerPassFactory"/> and
/// <see cref="BalaGedRecoveryFactory"/> / <see cref="BalaGedSanctuaryFactory"/>.
///
/// ## Implemented (v1)
///
/// - Instant identity at {R}, mono-red, owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Spikefield Hazard",
///   back = "Spikefield Cave"); starts on the front face.
/// - Resolve-time <see cref="SpellDefinition"/> built on demand via
///   <see cref="BuildSpellDefinition"/> (mirrors
///   <see cref="MagmaSprayFactory"/> / <see cref="PlayWithFireFactory"/>):
///     <list type="bullet">
///       <item>Single 1..1 "any target" request.</item>
///       <item><b>Damage</b>: deal 1 damage to the chosen target (creature /
///         player / planeswalker / battle) via
///         <see cref="Fx.DealDamageAny(object, int)"/> (CR 120.3 — non-combat
///         damage; CR 306.7 for planeswalker loyalty).</item>
///       <item><b>Exile rider</b>: if a <see cref="ReplacementBus"/> is
///         supplied AND the chosen target is a <see cref="Creature"/>,
///         register an EOT-expirable
///         <see cref="AngerOfTheGodsExileInsteadReplacement"/> scoped to that
///         single creature (CR 700.3 — "a permanent dealt damage this way").
///         The replacement rewrites the lethal battlefield→graveyard move
///         (CR 704.5g) to exile, expiring at end of turn (CR 514.2). Shared
///         directly with <see cref="MagmaSprayFactory"/> /
///         <see cref="AngerOfTheGodsFactory"/>.</item>
///     </list>
///
/// ## CR notes
/// - CR 700.3 — "a permanent dealt damage this way" back-references the
///   specific permanent this spell damaged; the rider is scoped to it alone.
///   For 1 damage the realistic dying permanent is a creature (a planeswalker
///   loses loyalty rather than taking marked damage), so the exile rider is
///   scoped to a creature target — the same simplification
///   <see cref="MagmaSprayFactory"/> makes.
/// - CR 119.2 — non-combat damage is marked on the creature; the SBA at
///   CR 704.5g moves a lethally-damaged creature to its graveyard, where the
///   registered replacement catches the ZoneMoveIntent.
/// - CR 514.2 — end-of-turn cleanup expires the IEndOfTurnExpirable rider.
/// </summary>
[CardName("Spikefield Hazard")]
public static class SpikefieldHazardFactory
{
    public const string CardName = "Spikefield Hazard";
    public const string BackName = "Spikefield Cave";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — fixed 1 damage to any target.</summary>
    public const int Damage = 1;

    /// <summary>
    /// Construct Spikefield Hazard as an Instant with owner / controller
    /// wired and the <see cref="MdfcState"/> face tracker attached. The
    /// resolve-time <see cref="SpellDefinition"/> is built on demand via
    /// <see cref="BuildSpellDefinition"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Spikefield Cave) is observable from the front-face
        // card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);

        return card;
    }

    /// <summary>
    /// Build the resolve-time "1 damage to any target; exile instead of die
    /// for a damaged permanent this turn" <see cref="SpellDefinition"/>.
    ///
    /// Single 1..1 "any target" request, no X. On resolution:
    ///   1. Deal <see cref="Damage"/> (1) to the chosen target via
    ///      <see cref="Fx.DealDamageAny"/> (CR 120.3).
    ///   2. If <paramref name="replacements"/> is non-null and the target is a
    ///      <see cref="Creature"/>, register an EOT-expirable
    ///      <see cref="AngerOfTheGodsExileInsteadReplacement"/> scoped to that
    ///      single creature so its death is rewritten to exile this turn
    ///      (CR 700.3 / CR 514.2).
    /// </summary>
    /// <param name="caster">Spikefield Hazard's controller (currently unused
    /// at resolution but kept for parity with the burn-factory signature and
    /// future provenance hooks).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object).</param>
    /// <param name="replacements">Optional <see cref="ReplacementBus"/> on
    /// which to register the exile-instead rider. When <c>null</c>, the rider
    /// is skipped (damage still applies — useful for shape tests).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
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
                    Fx.Inline(
                        $"{CardName}: 1 damage to any target; if a permanent dealt damage this way would die this turn, exile it instead.",
                        () =>
                        {
                            // CR 120.3 — deal 1 damage to the chosen target
                            // (creature / player / planeswalker / battle).
                            Fx.DealDamageAny(target, Damage);

                            // CR 700.3 — exile-instead rider, scoped to the
                            // single damaged creature. A planeswalker loses
                            // loyalty (not marked damage) and a player can't
                            // "die" from this clause, so only a Creature
                            // target arms the rider — same posture as
                            // MagmaSpray.
                            if (replacements != null && target is Creature creature)
                            {
                                var damaged = new HashSet<Creature> { creature };
                                replacements.Register<ZoneMoveIntent>(
                                    new AngerOfTheGodsExileInsteadReplacement(damaged));
                            }
                        }),
                };
            });
    }
}
