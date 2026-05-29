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
/// Named-card factory for Fellwar Stone (Fallen Empires / many reprints, {2}).
///
/// Artifact. Oracle text (verified against Scryfall 2026-05-29):
///   "{T}: Add one mana of any color that a land an opponent controls could
///    produce."
///
/// ## Modelling
/// This is the opponent-facing twin of Reflecting Pool / Star Compass:
/// instead of "any type/colour that a land <i>you</i> control could
/// produce", Fellwar Stone reflects whatever colours an <b>opponent's</b>
/// lands could produce.
///
/// Like every other dynamic "any colour" source in the engine (Reflecting
/// Pool <see cref="ReflectingPoolFactory"/>, Star Compass
/// <see cref="StarCompassFactory"/>, Cavern of Souls), the clause is
/// modelled as five colour-specific <see cref="ManaAbility"/> slots — one
/// per WUBRG. The activator picks a colour by picking the matching slot, so
/// no separate colour prompt is needed (CR 605.1 — mana abilities don't use
/// the stack; CR 605.1a — each colour slot is a separate mana ability).
///
/// Colourless {C} is intentionally excluded: "any <b>color</b>" means one of
/// the five colours, and {C}/colorless is not a colour (CR 105.1 / 105.2a —
/// the same exclusion Star Compass applies to Wastes). A land an opponent
/// controls that only produces {C} therefore enables none of these slots.
///
/// ## Dynamic "could produce" gate
/// Each colour slot carries a <c>canActivateCheck</c> that is live only while
/// (a) Fellwar Stone is untapped and on the battlefield, and (b) some land an
/// opponent currently controls could produce that colour — recomputed at
/// every legality check so it tracks lands entering/leaving and control
/// changes. "Could produce" is read off each opponent land's own
/// <see cref="ManaAbility"/> outputs (CR 305.6 for basics, plus whatever
/// nonbasics print), exactly the scan <see cref="ReflectingPoolFactory"/>
/// uses for its controller-side mirror.
///
/// ## Reaching opponents at runtime
/// A <see cref="ManaAbility"/>'s <c>canActivateCheck</c> closure only has the
/// controller in scope, and <see cref="Player"/> exposes no opponent list, so
/// the opponent battlefields are supplied via an injected
/// <c>allPlayersResolver</c> — the established pattern for opponent-scanning
/// cards (<see cref="BlastZoneFactory"/>, Ashiok). The parameterless
/// <see cref="Create(Player)"/> overload passes <c>null</c>: with no resolver
/// there are no visible opponents, so no colour slot is activatable (the
/// correct degenerate "no opponents → nothing to reflect" shape). Production
/// callers wire the resolver to the live player list.
///
/// ## Base shape from JSON
/// The Artifact identity ({2}) is materialised from the embedded definition
/// (<c>fellwar-stone.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The five dynamic mana abilities
/// are layered on here because the JSON <c>ManaAbilityDefinition</c> schema
/// only carries a fixed <c>produces</c> colour — it has no field for a
/// board-state-dependent "any colour an opponent's land could produce" gate
/// (same posture as Reflecting Pool).
/// </summary>
[CardName("Fellwar Stone")]
public static class FellwarStoneFactory
{
    public const string CardName = "Fellwar Stone";
    public const string Slug = "fellwar-stone";

    // CR 105.1 — the five colours. {C}/colorless is excluded: "any color"
    // never matches colorless mana (same exclusion Star Compass applies to
    // Wastes).
    private static readonly string[] Colors = { "W", "U", "B", "R", "G" };

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Construct Fellwar Stone with no live opponent wiring. The five colour
    /// slots are attached for shape inspection but none is activatable
    /// (no resolver → no visible opponents → nothing to reflect). Suitable
    /// for shape / dispatcher tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, allPlayersResolver: null);

    /// <summary>
    /// Construct Fellwar Stone owned and controlled by <paramref name="owner"/>.
    /// When <paramref name="allPlayersResolver"/> is supplied, each colour
    /// slot is activatable only while some land an opponent (any player other
    /// than the controller) currently controls could produce that colour.
    /// </summary>
    public static Artifact Create(
        Player owner,
        Func<IReadOnlyList<Player>>? allPlayersResolver)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var stone = (Artifact)CardDefinitionFactory.Build(Definition, owner);

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
            stone.AddAbility(new ManaAbility(
                source: stone,
                controller: owner,
                manaGenerated: ManaCost.Parse(thisColor),
                canActivateCheck: () => !stone.IsTapped
                                        && stone.Zone == ZoneType.Battlefield
                                        && OpponentCanProduce(stone, allPlayersResolver, thisColor)));
        }

        return stone;
    }

    /// <summary>
    /// True when some land an opponent of <paramref name="stone"/>'s current
    /// controller controls has a mana ability producing
    /// <paramref name="colorSymbol"/> (one of W/U/B/R/G). This is the "any
    /// color that a land an opponent controls could produce" gate, recomputed
    /// live on every legality check.
    /// </summary>
    private static bool OpponentCanProduce(
        Artifact stone,
        Func<IReadOnlyList<Player>>? allPlayersResolver,
        string colorSymbol)
    {
        var controller = stone.Controller;
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
    /// (one of W/U/B/R/G) on <paramref name="stone"/>.
    /// </summary>
    public static ManaAbility AbilityForColor(Artifact stone, string colorPip)
    {
        ArgumentNullException.ThrowIfNull(stone);

        var target = ManaCost.Parse(colorPip);
        return stone.Abilities
            .OfType<ManaAbility>()
            .Single(ma => ma.ManaGenerated.Equals(target));
    }
}
