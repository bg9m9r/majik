using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Patched Plaything (Edge of Eternities / Toy cycle,
/// {2}{W}).
///
/// Artifact Creature — Toy 4/3. Oracle text (Scryfall, verified):
///   "Double strike
///    This creature enters with two -1/-1 counters on it if you cast it from
///    your hand."
///
/// ## Shape source
/// Card identity (name, {2}{W}, 4/3, Artifact Creature — Toy, white,
/// Double strike) is loaded from
/// <c>Majik.Core/CardData/Cards/patched-plaything.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The <c>keywords</c> array carries
/// Double strike (CR 702.4), which <see cref="CardDefinitionFactory"/> attaches
/// as a plain <see cref="Majik.Core.Abilities.KeywordAbility"/> marker; the
/// combat engine (<see cref="Majik.Core.Combat.CombatAbilities.HasDoubleStrike"/>)
/// consumes the marker to assign first- AND regular-combat-damage.
///
/// ## Enters-with-counters (CR 614.1d)
/// "This creature enters with two -1/-1 counters on it if you cast it from your
/// hand" is NOT wired by this factory. Same posture as
/// <see cref="GoldveinHydraFactory"/> / <see cref="StarPupilFactory"/>: the
/// generic <see cref="Majik.Core.CardData.EntersWithCountersBinder"/> registers
/// the conditional <see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>
/// on the production deck-build path. The replacement reads
/// <see cref="Card.WasCastFromHand"/> (the cast-from-hand sentinel
/// <see cref="Majik.Core.Game.SpellCastFlow"/> stamps and
/// <see cref="Majik.Core.Services.ZoneService"/> preserves across the
/// Stack -> Battlefield move, CR 113.5) at the moment the permanent would enter:
/// when the controller cast Patched Plaything from hand it enters WITH two -1/-1
/// counters (a 2/1 on the battlefield); a non-hand cast / blink / copy / token
/// leaves the sentinel clear and the creature enters at its full 4/3. -1/-1
/// counters route through the generic
/// <see cref="Majik.Core.Zones.ZoneMoveIntent"/> counter bag (not the +1/+1
/// channel), so they apply on entry with no transient over-statted window the
/// SBA layer would observe.
///
/// A clean keyword + ETB-counter creature — no triggers, no activated abilities.
/// The JSON definition fully describes the static identity; this factory just
/// loads it and builds through <see cref="CardDefinitionFactory"/>. Adding the
/// factory flips <c>IsImplemented</c> via the <c>[CardName]</c> registry.
/// </summary>
[CardName("Patched Plaything")]
public static class PatchedPlaythingFactory
{
    public const string CardName = "Patched Plaything";
    public const string Slug = "patched-plaything";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Constructs Patched Plaything — a {2}{W} 4/3 Artifact Creature — Toy with
    /// the Double strike keyword marker. The conditional "enters with two -1/-1
    /// counters if cast from hand" replacement is owned by the
    /// <see cref="Majik.Core.CardData.EntersWithCountersBinder"/> on the prod
    /// build path (see class docstring).
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        return (Creature)CardDefinitionFactory.Build(Definition, owner);
    }
}
