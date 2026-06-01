using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Zealous Persecution (Alara Reborn / Modern
/// reprints, {W}{B}).
///
/// Instant. Oracle text:
///   "Until end of turn, creatures you control get +1/+1 and creatures your
///    opponents control get -1/-1."
///
/// ## Scryfall identity
/// <list type="bullet">
///   <item>Mana cost: {W}{B}; mana value 2; colors W, B.</item>
///   <item>Type line: Instant.</item>
/// </list>
///
/// ## Implemented (v1)
/// - <b>Card shape</b>: Instant {W}{B}, loaded from the embedded JSON
///   definition (<c>zealous-persecution.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
///   through <see cref="CardDefinitionFactory"/> — same data-backed posture
///   as <see cref="AnOfferYouCantRefuseFactory"/>.
/// - <b>Resolve-time symmetric continuous effect</b> (via
///   <see cref="BuildResolveEffect"/>): on resolution it snapshots the
///   battlefields (CR 608.2) and registers, until end of turn:
///     <ul>
///       <li><see cref="PumpUntilEndOfTurnEffect"/>(+1, +1) on every creature
///           the spell's CONTROLLER controls;</li>
///       <li><see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) on every creature
///           any OTHER player controls (CR 102.1 — "your opponents").</li>
///     </ul>
///   Both riders are Layer 7c +P/+T modifications (CR 613.1c) that expire at
///   the cleanup step (CR 514.2). The -1/-1 on a 1/1 leaves 0 toughness; the
///   state-based-action pass (CR 704.5f) moves it to its owner's graveyard on
///   the next check.
///
/// ## Scope / "you control" vs "your opponents control"
/// "Creatures you control" and "creatures your opponents control" are
/// distinct, contemporaneous snapshots (CR 109.5 / CR 700) taken at
/// resolution: the controller's side gets the buff, every other player's
/// side gets the debuff. Tokens or creatures that enter after resolution do
/// NOT pick up either rider (one-shot snapshot — same posture as
/// <see cref="PyroclasmFactory"/> / <see cref="ViolentOutburstFactory"/>).
///
/// ## Why a named factory
/// The symmetric "all creatures you control / all creatures opponents
/// control" mass +P/+T is not expressible in the single-target
/// <see cref="ResolveBuilder.PumpUntilEndOfTurn"/> DSL primitive (which
/// targets one chosen creature). The board-wide sweep takes
/// <paramref name="allPlayers"/> as a positional argument so callers can
/// apply it to every battlefield in one call — mirrors
/// <see cref="PyroclasmFactory.BuildResolveEffect"/>'s shape.
/// </summary>
[CardName("Zealous Persecution")]
public static class ZealousPersecutionFactory
{
    public const string CardName = "Zealous Persecution";
    public const string Slug = "zealous-persecution";

    /// <summary>+P/+T magnitude on the controller's creatures — +1/+1.</summary>
    public const int FriendlyPump = 1;

    /// <summary>-P/-T magnitude on opponents' creatures — -1/-1.</summary>
    public const int OpponentDebuff = -1;

    /// <summary>
    /// Build the card shape from the embedded JSON definition. Behaviour
    /// (the symmetric pump/debuff) is supplied at resolution via
    /// <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(definition, owner);
    }

    /// <summary>
    /// Build Zealous Persecution's resolve effect — until end of turn,
    /// <paramref name="controller"/>'s creatures get +1/+1 and every other
    /// player's creatures get -1/-1. Single <see cref="IEffect"/> entry so
    /// callers can splice it into a <c>SpellDefinition.EffectFactory</c>
    /// result or a <see cref="Majik.Core.Spells.Spell"/>'s effect list.
    /// </summary>
    /// <param name="controller">The spell's controller — "you" in the oracle
    /// text. Their creatures get +1/+1; everyone else's get -1/-1.</param>
    /// <param name="allPlayers">All players whose battlefields the spell
    /// should reach. Typically every player in the game.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player controller,
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect(
                $"{CardName}: until EOT, your creatures get +{FriendlyPump}/+{FriendlyPump}, " +
                $"opponents' creatures get {OpponentDebuff}/{OpponentDebuff}.",
                () =>
                {
                    // CR 608.2 — snapshot each battlefield before applying so
                    // any same-step zone-move side effects (e.g. a 1/1 going
                    // to 0 toughness) don't disturb enumeration. CR 109.5 —
                    // the controller's side is "you"; every other player is
                    // "your opponents" (CR 102.1).
                    foreach (var pl in allPlayers)
                    {
                        var isController = ReferenceEquals(pl, controller);
                        var delta = isController ? FriendlyPump : OpponentDebuff;

                        var creatures = pl.Zones.Battlefield.GetCards()
                            .OfType<Creature>()
                            .ToList();

                        foreach (var creature in creatures)
                        {
                            if (creature.Zone != ZoneType.Battlefield) continue;

                            // Shape-only safety — without a live
                            // ContinuousEffectsService wired onto the creature
                            // the rider silently no-ops rather than NRE'ing.
                            // Same posture as ViolentOutburstFactory.
                            if (creature.ActiveEffects == null) continue;

                            // CR 613.1c Layer 7c — +P/+T modification; CR
                            // 514.2 — expires at the cleanup step.
                            creature.ActiveEffects.Register(
                                new PumpUntilEndOfTurnEffect(creature, delta, delta));
                        }
                    }
                }),
        };
    }
}
