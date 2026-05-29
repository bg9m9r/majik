using System;
using System.Linq;
using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reflecting Pool (Tempest).
///
/// Land. Oracle text (verified against Scryfall 2026-05-29):
///   "{T}: Add one mana of any type that a land you control could produce."
///
/// ## Modelling
/// A "type of mana" is one of the five colours plus colorless (CR 107.4c —
/// "There are five colors of mana … plus colorless mana"; CR 106.1b). So
/// Reflecting Pool offers exactly the union of mana types its controller's
/// lands could produce, and nothing more.
///
/// Like the other dynamic-output "any type/colour" lands in the engine —
/// Cavern of Souls (<see cref="CavernOfSoulsFactory"/>), Gemstone Caverns
/// (<see cref="GemstoneCavernsFactory"/>), City of Brass — this is modelled
/// as six fixed-type <see cref="ManaAbility"/> instances (W, U, B, R, G, C),
/// one per producible type. The engine has no single "modal" mana ability
/// that emits a player-chosen type, so the five-/six-instance split is the
/// established faithful representation (CR 605.1a — each is a separate mana
/// ability).
///
/// What makes Reflecting Pool distinct from the static any-colour lands is
/// that its set of producible types is <b>not fixed</b> — it is recomputed
/// every time legality is checked, from whatever lands the controller
/// currently controls. Each per-type ability therefore carries a
/// <c>canActivateCheck</c> that is live only while some land the controller
/// controls (other than this Reflecting Pool) has a mana ability producing
/// that type. When the board has no land that could produce a given type,
/// that type's ability simply can't be activated — exactly the printed
/// "any type that a land you control could produce" gate.
///
/// ## Self-reference (CR 106.7-flavoured)
/// Reflecting Pool excludes <i>itself</i> (and, transitively, any other
/// Reflecting Pool) from the scan. A Reflecting Pool only "could produce" a
/// type by reflecting some other land, so letting Pools seed each other would
/// be circular and would manufacture types out of nothing. Excluding the
/// source land resolves the circularity to the intuitive answer: two
/// Reflecting Pools alone produce nothing. (Two Pools + a Forest: both tap
/// for {G}, because the Forest — not the other Pool — seeds {G}.)
///
/// ## Base shape from JSON
/// The plain nonbasic Land identity is materialised from the embedded JSON
/// definition (<c>reflecting-pool.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The six dynamic mana abilities
/// are layered on here because the JSON <c>ManaAbilityDefinition</c> schema
/// only carries a fixed <c>produces</c> colour — it has no field for a
/// board-state-dependent "any type a land you control could produce" gate
/// (same posture as <see cref="GhituEncampmentFactory"/>, whose animate
/// ability is also not expressible in the JSON schema).
/// </summary>
[CardName("Reflecting Pool")]
public static class ReflectingPoolFactory
{
    public const string CardName = "Reflecting Pool";
    public const string Slug = "reflecting-pool";

    // The five colours plus colorless — the complete set of mana "types"
    // (CR 107.4c / 106.1b) Reflecting Pool can ever reflect.
    private static readonly string[] ManaTypes = { "W", "U", "B", "R", "G", "C" };

    /// <summary>
    /// Construct Reflecting Pool owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base identity (name + nonbasic Land type) from the embedded JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add one mana of any type that a land you control could
        //      produce.
        //
        // CR 605.1a — six separate mana abilities (one per WUBRG + {C}),
        // each gated so it is legal ONLY while some land the controller
        // currently controls (other than this Pool) could produce that
        // type. The producible-type set is recomputed at every legality
        // check, so it tracks control changes / lands entering and leaving.
        // ----------------------------------------------------------------
        foreach (var type in ManaTypes)
        {
            var thisType = type; // capture per iteration
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(thisType),
                canActivateCheck: () => !land.IsTapped
                                        && land.Zone == ZoneType.Battlefield
                                        && ControllerCanProduce(land, thisType)));
        }

        return land;
    }

    /// <summary>
    /// True when some land the <paramref name="pool"/>'s current controller
    /// controls — other than <paramref name="pool"/> itself — has a mana
    /// ability that produces <paramref name="typeSymbol"/> (one of W/U/B/R/G/C).
    /// This is the "any type that a land you control could produce" gate
    /// (CR 605.1a), recomputed live on every legality check.
    /// </summary>
    private static bool ControllerCanProduce(Land pool, string typeSymbol)
    {
        var target = ManaCost.Parse(typeSymbol).ToString();
        var controller = pool.Controller;
        if (controller == null)
        {
            return false;
        }

        foreach (var card in controller.Zones.Battlefield.GetCards())
        {
            // Only lands count, and the Pool never seeds itself (or another
            // Reflecting Pool) — see class xmldoc on the circularity.
            if (ReferenceEquals(card, pool)
                || card is not Land otherLand
                || !otherLand.HasType(CardType.Land)
                || otherLand.Name == CardName)
            {
                continue;
            }

            var produces = otherLand.Abilities
                .OfType<ManaAbility>()
                .Any(ma => ma.ManaGenerated.ToString() == target);

            if (produces)
            {
                return true;
            }
        }

        return false;
    }
}
