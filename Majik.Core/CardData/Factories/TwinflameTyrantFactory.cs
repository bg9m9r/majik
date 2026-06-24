using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Twinflame Tyrant (Outlaws of Thunder Junction,
/// {3}{R}{R}).
///
/// Creature — Dragon {3}{R}{R} 3/5. Oracle text (verified against Scryfall):
///   "Flying
///    If a source you control would deal damage to an opponent or a
///    permanent an opponent controls, it deals double that damage instead."
///
/// ## Implementation
///
/// The base shape (name, Creature, Dragon subtype, {3}{R}{R}, 3/5) is
/// materialised from the embedded JSON definition
/// (<c>twinflame-tyrant.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The Flying keyword marker and
/// the asymmetric damage-doubling replacement are layered on here — the JSON
/// <c>AbilityDefinition</c> schema expresses neither keyword markers nor
/// damage-replacement effects (same posture as
/// <see cref="MischievousMysticFactory"/> for the JSON-base + code-layered
/// split, and <see cref="AngrathsMaraudersFactory"/> for the doubling clause).
///
/// - <b>Flying (CR 702.9)</b> — keyword marker via <see cref="KeywordAbility"/>.
///   Block restrictions enforced by <see cref="Majik.Core.Combat.CombatAbilities"/>.
/// - <b>Asymmetric damage doubling</b> (CR 614) — single
///   <see cref="DamageDoubleReplacement"/> registration on the supplied
///   <see cref="ReplacementBus"/>, gated on:
///     1. Twinflame Tyrant is on the battlefield.
///     2. <see cref="DamageIntent.Source"/> is controlled by the Tyrant's
///        current controller ("a source you control").
///     3. The damage target is an opponent of the controller OR a permanent
///        an opponent controls.
///   This is the same predicate pair as Angrath's Marauders — the printed
///   text differs only by trailing redundant wording ("...to that player or
///   permanent instead" vs "...double that damage instead"), so the two
///   share <see cref="AngrathsMaraudersFactory.SourceControlledBy"/> +
///   <see cref="AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent"/>.
/// - The Tyrant's controller is read live from <see cref="Card.Controller"/>
///   rather than captured at construction, so control-change effects (Mind
///   Control, Threaten) repoint the doubling clause as soon as the controller
///   flips. The clause gates on the Tyrant being on the battlefield, so
///   blink / bounce automatically suspends it without explicit deregistration.
///
/// ## Notes
/// - Two-overload shape mirrors Angrath's Marauders / Gisela / Furnace of
///   Rath: single-arg <see cref="Create(Player)"/> is shape-only for
///   dispatcher tests (no bus → no replacement registration); the
///   <see cref="Create(Player, ReplacementBus?)"/> overload wires the live
///   doubling clause when a bus is supplied. Flying is wired on both paths.
/// </summary>
[CardName("Twinflame Tyrant")]
public static class TwinflameTyrantFactory
{
    public const string CardName = "Twinflame Tyrant";
    public const string Slug = "twinflame-tyrant";

    /// <summary>
    /// Construct Twinflame Tyrant with card identity + Flying only — no
    /// damage-doubling replacement is registered. Suitable for shape /
    /// dispatcher tests; the bus-driven doubling lives on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload. This is the
    /// overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Twinflame Tyrant. When <paramref name="replacements"/> is
    /// supplied, the asymmetric "double damage you deal to opponents or their
    /// permanents" CR 614 replacement is registered against it, gated on the
    /// Tyrant being on the battlefield. Flying is wired on both paths.
    /// </summary>
    public static Creature Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Dragon, {3}{R}{R}, 3/5). No abilities in the JSON — Flying + the
        // doubling clause are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. Block restrictions enforced by CombatAbilities.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Asymmetric doubling — controller-side source + opponent-side
        // target (CR 614). Shares the Marauders predicates.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<DamageIntent>(new DamageDoubleReplacement(
                intent =>
                    card.Zone == ZoneType.Battlefield
                    && AngrathsMaraudersFactory.SourceControlledBy(intent, card.Controller)
                    && AngrathsMaraudersFactory.TargetIsOpponentOrTheirPermanent(intent, card.Controller)));
        }

        return card;
    }
}
