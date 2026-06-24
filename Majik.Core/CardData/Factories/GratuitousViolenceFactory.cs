using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gratuitous Violence (Gatecrash, {2}{R}{R}{R}).
///
/// Enchantment. Oracle text (verified against Scryfall):
///   "If a creature you control would deal damage to a permanent or player,
///    it deals double that damage instead."
///
/// ## Shape source
///
/// Card identity (name, {2}{R}{R}{R}, Enchantment, red) is loaded from
/// <c>Majik.Core/CardData/Cards/gratuitous-violence.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The replacement behaviour is wired in
/// code below.
///
/// ## Implementation
///
/// <b>Asymmetric damage doubling</b> (CR 614) — single
/// <see cref="DamageDoubleReplacement"/> registration on the supplied
/// <see cref="ReplacementBus"/>, gated on:
///   1. Gratuitous Violence is on the battlefield.
///   2. The damage <see cref="DamageIntent.Source"/> is a <see cref="Creature"/>
///      controlled by Gratuitous Violence's current controller — "a creature
///      you control". This narrows the family's
///      <see cref="AngrathsMaraudersFactory.SourceControlledBy"/> source gate to
///      <i>creatures only</i> (Angrath's Marauders / Furnace of Rath accept any
///      source); a non-creature source you control — an enchantment, an
///      activated artifact ability, a burn spell — does not qualify.
///   3. The target is any permanent or player ("a permanent or player"). Unlike
///      Gisela / Angrath's Marauders, there is no opponent-side target gate: any
///      of <see cref="DamageIntent.TargetCreature"/>,
///      <see cref="DamageIntent.TargetPlayer"/>, or
///      <see cref="DamageIntent.TargetPlaneswalker"/> qualifies, regardless of
///      who controls it. (The DamageDoubleReplacement bus guard already
///      short-circuits on Amount &lt;= 0, so an intent with no actual target /
///      no damage never doubles.)
///
/// The controller is read live from <see cref="Card.Controller"/> rather than
/// captured at construction, so control-change effects (Mind Control, Threaten)
/// repoint the doubling clause as soon as the controller flips. Gating on the
/// battlefield zone means blink / bounce automatically suspends the clause
/// without explicit deregistration.
///
/// Per-effect dedup in the bus (CR 616.1c) lets the clause stack with other
/// doublers (Furnace of Rath, a second Gratuitous Violence): each fires once
/// per intent, so two copies quadruple your creatures' damage.
///
/// ## Notes
/// - Two-overload shape mirrors Furnace of Rath / Angrath's Marauders / Gisela:
///   single-arg <see cref="Create(Player)"/> is shape-only for dispatcher tests
///   (no bus → no replacement registration); the
///   <see cref="Create(Player, ReplacementBus?)"/> overload wires the live
///   doubling clause when a bus is supplied.
/// </summary>
[CardName("Gratuitous Violence")]
public static class GratuitousViolenceFactory
{
    public const string CardName = "Gratuitous Violence";
    public const string Slug = "gratuitous-violence";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Gratuitous Violence with card identity only — no
    /// damage-doubling replacement is registered. Suitable for shape /
    /// dispatcher tests; bus-driven doubling lives on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Gratuitous Violence. When <paramref name="replacements"/> is
    /// supplied, the asymmetric "double the damage your creatures deal" CR 614
    /// replacement is registered against it, gated on the enchantment being on
    /// the battlefield.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Enchantment)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Asymmetric doubling — creature source you control, any
        // permanent / player target (CR 614).
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new DamageDoubleReplacement(
                intent =>
                    card.Zone == ZoneType.Battlefield
                    && SourceIsCreatureControlledBy(intent, card.Controller)));
        }

        return card;
    }

    /// <summary>
    /// "A creature you control" — true when the damage intent's source is a
    /// <see cref="Creature"/> whose <see cref="Card.Controller"/> is
    /// <paramref name="controller"/>. Narrower than
    /// <see cref="AngrathsMaraudersFactory.SourceControlledBy"/> (which accepts
    /// any controlled source, including a <see cref="Player"/>): a non-creature
    /// source — even one you control — fails closed.
    /// </summary>
    internal static bool SourceIsCreatureControlledBy(DamageIntent intent, Player? controller)
    {
        if (controller is null) return false;
        return intent.Source is Creature src
            && ReferenceEquals(src.Controller, controller);
    }
}
