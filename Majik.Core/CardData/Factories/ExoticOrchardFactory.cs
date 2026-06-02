using System;
using System.Collections.Generic;
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
/// Named-card factory for Exotic Orchard (Conflux / many reprints).
///
/// Land. Oracle text (verified against Scryfall 2026-06-02):
///   "{T}: Add one mana of any color that a land an opponent controls could
///    produce."
///
/// ## Modelling
/// Exotic Orchard is the land version of Fellwar Stone
/// (<see cref="FellwarStoneFactory"/>): instead of reflecting the colours a
/// land <i>you</i> control could produce (Reflecting Pool
/// <see cref="ReflectingPoolFactory"/>), it reflects whatever colours a land
/// an <b>opponent</b> controls could produce. The only differences from
/// Fellwar Stone are the card's identity (a nonbasic Land rather than a {2}
/// Artifact) and the printed cost ({T} only, no generic cost) — the
/// "any color a land an opponent controls could produce" gate is identical.
///
/// Like every other dynamic "any colour" source in the engine (Fellwar Stone,
/// Reflecting Pool, Star Compass, Cavern of Souls), the clause is modelled as
/// five colour-specific <see cref="ManaAbility"/> slots — one per WUBRG. The
/// activator picks a colour by picking the matching slot, so no separate
/// colour prompt is needed (CR 605.1 — mana abilities don't use the stack;
/// CR 605.1a — each colour slot is a separate mana ability).
///
/// Colourless {C} is intentionally excluded: "any <b>color</b>" means one of
/// the five colours, and {C}/colorless is not a colour (CR 105.1 / 105.2a).
/// A land an opponent controls that only produces {C} therefore enables none
/// of these slots.
///
/// ## Dynamic "could produce" gate
/// Each colour slot carries a <c>canActivateCheck</c> that is live only while
/// (a) Exotic Orchard is untapped and on the battlefield, and (b) some land an
/// opponent currently controls could produce that colour — recomputed at every
/// legality check so it tracks lands entering/leaving and control changes.
/// "Could produce" is read off each opponent land's own
/// <see cref="ManaAbility"/> outputs (CR 305.6 for basics, plus whatever
/// nonbasics print), exactly the scan <see cref="FellwarStoneFactory"/> uses.
///
/// ## Reaching opponents at runtime
/// A <see cref="ManaAbility"/>'s <c>canActivateCheck</c> closure only has the
/// controller in scope, and <see cref="Player"/> exposes no opponent list, so
/// the opponent battlefields are supplied via an injected
/// <c>allPlayersResolver</c> — the established pattern for opponent-scanning
/// cards (Fellwar Stone, Blast Zone). The parameterless
/// <see cref="Create(Player)"/> overload passes <c>null</c>: with no resolver
/// there are no visible opponents, so no colour slot is activatable (the
/// correct degenerate "no opponents → nothing to reflect" shape). Production
/// callers wire the resolver to the live player list.
///
/// ## Base shape from JSON
/// The plain nonbasic Land identity is materialised from the embedded JSON
/// definition (<c>exotic-orchard.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The five dynamic mana abilities
/// are layered on here because the JSON <c>ManaAbilityDefinition</c> schema
/// only carries a fixed <c>produces</c> colour — it has no field for a
/// board-state-dependent "any colour an opponent's land could produce" gate
/// (same posture as Fellwar Stone / Reflecting Pool).
/// </summary>
[CardName("Exotic Orchard")]
public static class ExoticOrchardFactory
{
    public const string CardName = "Exotic Orchard";
    public const string Slug = "exotic-orchard";

    // CR 105.1 — the five colours. {C}/colorless is excluded: "any color"
    // never matches colorless mana.
    private static readonly string[] Colors = { "W", "U", "B", "R", "G" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Exotic Orchard with no live opponent wiring. The five colour
    /// slots are attached for shape inspection but none is activatable
    /// (no resolver → no visible opponents → nothing to reflect). Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Land Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Exotic Orchard owned and controlled by <paramref name="owner"/>.
    /// When <paramref name="allPlayersResolver"/> is supplied, each colour slot
    /// is activatable only while some land an opponent (any player other than
    /// the controller) currently controls could produce that colour.
    /// </summary>
    public static Land Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base identity (name + nonbasic Land type) from the embedded JSON.
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // ----------------------------------------------------------------
        // {T}: Add one mana of any color that a land an opponent controls
        //      could produce.
        //
        // CR 605.1a — five separate mana abilities (one per WUBRG), each
        // gated so it is legal ONLY while some land an opponent currently
        // controls could produce that colour. The producible-colour set is
        // recomputed at every legality check, so it tracks control changes /
        // lands entering and leaving.
        // ----------------------------------------------------------------
        foreach (var color in Colors)
        {
            var thisColor = color; // capture per iteration
            land.AddAbility(new ManaAbility(
                source: land,
                controller: owner,
                manaGenerated: ManaCost.Parse(thisColor),
                canActivateCheck: () => !land.IsTapped
                                        && land.Zone == ZoneType.Battlefield
                                        && OpponentCanProduce(land, allPlayersResolver, thisColor)));
        }

        return land;
    }

    /// <summary>
    /// True when some land an opponent of <paramref name="orchard"/>'s current
    /// controller controls has a mana ability producing
    /// <paramref name="colorSymbol"/> (one of W/U/B/R/G). This is the "any
    /// color that a land an opponent controls could produce" gate, recomputed
    /// live on every legality check.
    /// </summary>
    private static bool OpponentCanProduce(
        Land orchard,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        string colorSymbol)
    {
        var controller = orchard.Controller;
        if (controller == null || allPlayersResolver == null)
        {
            return false;
        }

        var target = ManaCost.Parse(colorSymbol).ToString();
        var players = allPlayersResolver.Invoke();
        if (players == null)
        {
            return false;
        }

        foreach (var player in players)
        {
            if (ReferenceEquals(player, controller))
            {
                continue; // opponents only
            }

            foreach (var card in player.Zones.Battlefield.GetCards())
            {
                if (card is not Land land || !land.HasType(CardType.Land))
                {
                    continue;
                }

                var produces = land.Abilities
                    .OfType<ManaAbility>()
                    .Any(ma => ma.ManaGenerated.ToString() == target);

                if (produces)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Test/agent helper: return the colour slot for <paramref name="colorPip"/>
    /// (one of W/U/B/R/G) on <paramref name="orchard"/>.
    /// </summary>
    public static ManaAbility AbilityForColor(Land orchard, string colorPip)
    {
        ArgumentNullException.ThrowIfNull(orchard);

        var target = ManaCost.Parse(colorPip);
        return orchard.Abilities
            .OfType<ManaAbility>()
            .Single(ma => ma.ManaGenerated.Equals(target));
    }
}
