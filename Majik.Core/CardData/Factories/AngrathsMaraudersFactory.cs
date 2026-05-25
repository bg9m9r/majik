using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Angrath's Marauders (Ixalan, {4}{R}{R}).
///
/// Creature — Human Pirate {4}{R}{R} 4/4. Oracle text:
///   "If a source you control would deal damage to an opponent or a
///    permanent an opponent controls, it deals double that damage to
///    that player or permanent instead."
///
/// ## Implementation
///
/// - Card identity (Creature — Human Pirate, mana cost {4}{R}{R}, 4/4,
///   owner / controller wiring).
/// - <b>Asymmetric damage doubling</b> (CR 614) — single
///   <see cref="DamageDoubleReplacement"/> registration on the supplied
///   <see cref="ReplacementBus"/>, gated on:
///     1. Angrath's Marauders is on the battlefield.
///     2. <see cref="DamageIntent.Source"/> is controlled by the
///        Marauders' current controller — a <see cref="Card"/> source
///        via <see cref="Card.Controller"/>, a <see cref="Player"/>
///        source by reference-equality with the controller.
///     3. The damage target is an opponent of the controller (the
///        target <see cref="Player"/> isn't the controller) OR a
///        permanent controlled by an opponent (target Creature /
///        Planeswalker whose <see cref="Card.Controller"/> isn't the
///        controller).
/// - Per-effect dedup in the bus (CR 616.1c) lets the clause stack with
///   other doublers: Furnace of Rath + Angrath's Marauders quadruples
///   damage your sources deal to opponents (Furnace doubles symmetrically
///   first, Angrath's predicate then re-applies on the rewritten intent).
/// - The Marauders' controller is read live from
///   <see cref="Card.Controller"/> rather than captured at construction,
///   so control-change effects (Mind Control, Threaten) repoint the
///   doubling clause as soon as the controller flips.
///
/// ## Notes
/// - Two-overload shape mirrors Inquisitor's Flail / Furnace of Rath:
///   single-arg <see cref="Create(Player)"/> is shape-only for
///   dispatcher tests (no bus → no replacement registration); the
///   <see cref="Create(Player, ReplacementBus?)"/> overload wires the
///   live doubling clause when a bus is supplied.
/// - The Marauders has no printed evergreen keywords beyond the
///   asymmetric doubling — vanilla 4/4 body modulo the trigger.
/// </summary>
[CardName("Angrath's Marauders")]
public static class AngrathsMaraudersFactory
{
    public const string CardName = "Angrath's Marauders";
    public const string Cost = "{4}{R}{R}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Angrath's Marauders with card identity only — no
    /// damage-doubling replacement is registered. Suitable for shape /
    /// dispatcher tests; bus-driven doubling lives on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Angrath's Marauders. When <paramref name="replacements"/>
    /// is supplied, the asymmetric "double damage you deal to opponents
    /// or their permanents" CR 614 replacement is registered against
    /// it, gated on the Marauders being on the battlefield.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: Cost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Pirate });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Asymmetric doubling — controller-side source + opponent-side
        // target (CR 614).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new DamageDoubleReplacement(
                intent =>
                    card.Zone == ZoneType.Battlefield
                    && SourceControlledBy(intent, card.Controller)
                    && TargetIsOpponentOrTheirPermanent(intent, card.Controller)));
        }

        return card;
    }

    /// <summary>
    /// "A source you control" — true when the damage intent's source is
    /// controlled by <paramref name="controller"/>. A <see cref="Card"/>
    /// source uses <see cref="Card.Controller"/>; a <see cref="Player"/>
    /// source is its own controller; anything else fails closed.
    /// Exposed internal for shared use by Gisela's identical predicate.
    /// </summary>
    internal static bool SourceControlledBy(DamageIntent intent, Player? controller)
    {
        if (controller is null) return false;
        return intent.Source switch
        {
            Card src => ReferenceEquals(src.Controller, controller),
            Player p => ReferenceEquals(p, controller),
            _ => false,
        };
    }

    /// <summary>
    /// "An opponent or a permanent an opponent controls" — true when the
    /// intent's target is a <see cref="Player"/> who isn't
    /// <paramref name="controller"/>, OR a Creature / Planeswalker
    /// whose <see cref="Card.Controller"/> isn't <paramref name="controller"/>.
    /// Exposed internal for shared use by Gisela's identical predicate.
    /// </summary>
    internal static bool TargetIsOpponentOrTheirPermanent(DamageIntent intent, Player? controller)
    {
        if (controller is null) return false;

        if (intent.TargetPlayer is { } p)
            return !ReferenceEquals(p, controller);

        if (intent.TargetCreature is { } c)
            return c.Controller is not null && !ReferenceEquals(c.Controller, controller);

        if (intent.TargetPlaneswalker is { } pw)
            return pw.Controller is not null && !ReferenceEquals(pw.Controller, controller);

        return false;
    }
}
