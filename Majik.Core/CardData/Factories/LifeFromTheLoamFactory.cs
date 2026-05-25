using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Life from the Loam (Ravnica: City of Guilds, {1}{G}).
///
/// Sorcery. Oracle text:
///   "Return up to three target land cards from your graveyard to your
///    hand.
///    Dredge 3"
///
/// ## Implemented (v1)
/// - Sorcery {1}{G} (Green) shape, owner / controller wired.
/// - <b>Dredge 3</b> (CR 702.52) via <see cref="DredgeFactory.Build"/>.
///   Keyword marker attached + graveyard-anchored draw replacement
///   registered when a <see cref="ReplacementBus"/> is supplied.
/// - <see cref="BuildResolveEffect"/> exposes the lands-to-hand resolve
///   body. The closure picks up to three land cards from the caster's
///   graveyard (deterministic first-three fallback when no selector is
///   supplied — same posture as <see cref="EternalWitnessFactory"/>'s
///   no-agent path), validates each is still a Land in the caster's
///   graveyard at resolution time (CR 608.2b illegal-on-resolution
///   recheck), and moves them Graveyard → Hand via
///   <see cref="ZoneService.MoveCard"/> when supplied; raw-zone
///   fallback otherwise. Empty graveyard / no land cards → clean
///   no-op (CR 608.2b).
///
/// ## v1 gaps
/// - <b>Target prompt</b>: the production cast path threads
///   <see cref="IPlayerAgent.ChooseTargetsAsync"/> through SpellCastFlow
///   to supply the chosen targets. The selector-based API on
///   <see cref="BuildResolveEffect"/> is the v1 substitute for
///   factories / tests that don't run the cast flow — they pick the
///   lands explicitly. The deterministic "first three lands" fallback
///   matches the same no-agent posture as Eternal Witness +
///   Tasigur, the Golden Fang.
/// </summary>
[CardName("Life from the Loam")]
public static class LifeFromTheLoamFactory
{
    public const string CardName = "Life from the Loam";
    public const string PrintedManaCost = "{1}{G}";
    public const int DredgeValue = 3;
    public const int MaxLands = 3;

    /// <summary>
    /// Construct the Life from the Loam sorcery shape with the Dredge
    /// marker attached (shape-only). No <see cref="ReplacementBus"/>
    /// wiring — use <see cref="Create(Player, ReplacementBus?)"/> when
    /// the dredge replacement should be live.
    /// </summary>
    public static Sorcery Create(Player owner) => Create(owner, replacements: null);

    /// <summary>
    /// Construct Life from the Loam. When <paramref name="replacements"/>
    /// is supplied the Dredge 3 graveyard-anchored draw replacement is
    /// registered (CR 702.52). The lands-to-hand resolve body is NOT
    /// attached to the card directly — compose it via
    /// <see cref="BuildResolveEffect"/> into a
    /// <see cref="Majik.Core.Game.SpellDefinition"/> or
    /// <see cref="Majik.Core.Spells.Spell"/>.
    /// </summary>
    public static Sorcery Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.52 — Dredge 3. Keyword marker + graveyard-anchored draw
        // replacement (gated on Library.Count >= 3 + agent yes/no).
        DredgeFactory.Build(card, DredgeValue, replacements);

        return card;
    }

    /// <summary>
    /// Build the lands-to-hand resolve effect. <paramref name="landSelector"/>
    /// returns up to three land cards from the caster's graveyard. When
    /// null, the deterministic fallback picks the first up-to-three
    /// land cards in graveyard insertion order. Each pick is validated
    /// (still a Land, still in caster's graveyard at resolution) and
    /// moved Graveyard → Hand via <see cref="ZoneService.MoveCard"/>
    /// when supplied; raw-zone fallback otherwise.
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<Player, IReadOnlyList<ICard>>? landSelector = null,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect($"{CardName}: return up to {MaxLands} land cards from your graveyard to your hand.",
                () =>
                {
                    // Resolve target candidates. Selector path = agent /
                    // explicit pick; fallback path = deterministic first
                    // up-to-three land cards in graveyard order.
                    IReadOnlyList<ICard> picks;
                    if (landSelector != null)
                    {
                        picks = landSelector(caster) ?? Array.Empty<ICard>();
                    }
                    else
                    {
                        picks = caster.Zones.Graveyard.GetCards()
                            .Where(c => c.HasType(CardType.Land))
                            .Take(MaxLands)
                            .ToList();
                    }

                    var seen = new HashSet<ICard>();
                    var graveyard = caster.Zones.Graveyard.GetCards().ToHashSet();
                    var returned = 0;

                    foreach (var pick in picks)
                    {
                        if (returned >= MaxLands) break;
                        if (pick == null) continue;
                        // CR 608.2b — illegal-on-resolution gates. The
                        // pick must still be a Land card in the caster's
                        // graveyard at the moment of resolution.
                        if (!pick.HasType(CardType.Land)) continue;
                        if (!graveyard.Contains(pick)) continue;
                        if (!seen.Add(pick)) continue;

                        if (zoneService != null)
                        {
                            zoneService.MoveCard(pick, ZoneType.Graveyard, ZoneType.Hand, caster);
                        }
                        else
                        {
                            caster.Zones.Graveyard.RemoveCard(pick);
                            caster.Zones.Hand.AddCard(pick);
                            pick.SetZone(ZoneType.Hand);
                        }
                        returned++;
                    }
                }),
        };
    }
}
