using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skophos Reaver (Theros Beyond Death, {2}{R}).
///
/// Creature — Minotaur Warrior 2/3. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "During your turn, this creature gets +2/+0.
///    Madness {1}{R}"
///
/// ## Shape source
/// Card identity (name, {2}{R}, 2/3, Creature — Minotaur Warrior) is loaded
/// from <c>Majik.Core/CardData/Cards/skophos-reaver.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The conditional pump static is
/// attached in code below.
///
/// ## Implemented (v1)
///
/// - <b>2/3 Creature — Minotaur Warrior at {2}{R}.</b>
///
/// - <b>"During your turn, this creature gets +2/+0" (CR 613.3c / CR 611.2c).</b>
///   A Layer 7c characteristic-modifying static gated on "is it the
///   controller's turn?". Implemented via the reusable
///   <see cref="WhileControllersTurnPumpEffect"/> — the direct sibling of
///   <see cref="WhileAttackingPumpEffect"/> (Adanto Vanguard), swapping the
///   "is attacking" predicate for an "is it my turn" predicate. The buff
///   appears the instant the active player becomes this creature's controller
///   (CR 500.1) and lifts the instant the turn passes; the effect never
///   expires while the source is on the battlefield (the gate lives in
///   <see cref="WhileControllersTurnPumpEffect.AppliesTo"/>, Prune-safe).
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {1}{R} works intrinsically for every catalogued card (CR 702.35)
/// via <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the
/// central discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>;
/// "Skophos Reaver" is catalogued at {1}{R}, so the madness line needs no
/// factory code.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. No live continuous-effects
///   service, so the pump static is not registered (the card is the correct
///   base 2/3). Suitable for factory-shape / dispatch tests.
/// - <see cref="Create(Player, ContinuousEffectsService)"/> — the
///   source-generated effects-aware overload (see
///   <see cref="NamedCardFactory"/>): registers the conditional pump on the
///   supplied service so the +2/+0 surfaces on every
///   <see cref="ContinuousEffectsService.Compute(Permanent)"/> during the
///   controller's turn. The "is it my turn?" predicate reads the live active
///   player off the service's turn-state provider.
/// </summary>
[CardName("Skophos Reaver")]
public static class SkophosReaverFactory
{
    public const string CardName = "Skophos Reaver";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("skophos-reaver");

    /// <summary>Power bonus during the controller's turn (CR 613.3c).</summary>
    public const int OwnTurnPowerBonus = 2;

    /// <summary>Toughness bonus during the controller's turn (CR 613.3c).</summary>
    public const int OwnTurnToughnessBonus = 0;

    /// <summary>
    /// Construct Skophos Reaver with no live continuous-effects wiring. The
    /// conditional pump is NOT registered — the card is the correct base 2/3.
    /// Suitable for factory-shape / dispatch tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, effects: null);

    /// <summary>
    /// Construct Skophos Reaver. When <paramref name="effects"/> is supplied
    /// (the source-generated effects-aware dispatch path) the "during your
    /// turn, +2/+0" static registers so the buff surfaces during the
    /// controller's turn. The "is it my turn?" predicate reads the live active
    /// player off the service and compares it to this creature's controller.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "During your turn, this creature gets +2/+0." — CR 613.3c.
        // Registered as a conditional Layer 7c static whose gate re-reads the
        // live active player on every Compute (mirrors Adanto Vanguard's
        // while-attacking pump). The predicate asks the service "is it the
        // active player == this creature's controller?".
        // ----------------------------------------------------------------
        if (effects != null)
        {
            effects.Register(new WhileControllersTurnPumpEffect(
                card,
                OwnTurnPowerBonus,
                OwnTurnToughnessBonus,
                isControllersTurn: () =>
                    effects.ActivePlayer != null
                    && ReferenceEquals(effects.ActivePlayer, card.Controller)));
        }

        return card;
    }
}
