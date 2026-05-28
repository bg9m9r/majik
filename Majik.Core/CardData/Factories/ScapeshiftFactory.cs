using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scapeshift (Morningtide, {2}{G}{G}).
///
/// Sorcery. Oracle text:
///   "Sacrifice any number of lands. Search your library for that many
///    land cards, put them onto the battlefield, then shuffle."
///
/// ## Why it gets a bespoke factory
/// Variable-count sacrifice cost composed with a variable-count library
/// tutor — neither composes from the existing templates yet:
/// - <see cref="Majik.Core.Costs.SacrificeAnotherCreatureCost"/> and
///   <see cref="Majik.Core.Costs.SacrificeBasicLandCost"/> are single-
///   target additional costs paid on cast; Scapeshift's sacrifice is
///   resolve-time and unbounded ("any number").
/// - <see cref="PrimevalTitanFactory"/> tutors a fixed up-to-two lands;
///   Scapeshift's count is "that many" = number of lands sacrificed.
/// - The printed effect places fetched lands on the battlefield UNTAPPED
///   (no "tapped" rider in the printed oracle), distinct from Primeval
///   Titan / Cultivate which always enter tapped.
///
/// The factory exposes <see cref="BuildResolveEffect"/> so callers
/// (tests / bots / future cast flow) supply both the sacrifice picker
/// and the tutor picker as deterministic selectors — the engine has no
/// "choose N permanents to sacrifice" agent hook yet, and the existing
/// <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> only picks one card
/// at a time. The tutor side defaults to PrimevalTitan's per-slot agent
/// loop when no selector is supplied; the sacrifice side defaults to "no
/// lands sacrificed" (clean zero-N no-op) when no selector is supplied,
/// so the dispatcher path is safe to call without agent infrastructure.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{G}{G}.
/// - <see cref="BuildResolveEffect"/> wires the resolve closure:
///   1. <c>sacSelector(caster)</c> returns the lands on the caster's
///      battlefield to sacrifice (CR 701.16). The closure filters to
///      lands the caster controls on the battlefield, dedupes by
///      reference, and routes each to its owner's graveyard.
///   2. N = number of lands actually sacrificed.
///   3. <c>tutorSelector(caster)</c> returns up to N land CARDs from the
///      caster's library to put onto the battlefield (CR 701.19a). The
///      closure filters to lands present in the library, dedupes, and
///      clamps to N. Each pick is moved Library → Battlefield untapped.
///   4. Library shuffle (CR 701.20a) — routed through
///      <see cref="Majik.Core.Zones.LibraryShuffle.ShuffleLibrary"/>.
///
/// ## v1 gaps
/// - <b>"Any number" prompt</b>: the engine has no first-class "pick a
///   subset of permanents to sacrifice" agent hook. The selector-based
///   API is the v1 substitute. Single-arg dispatcher path = zero lands
///   sacrificed → zero lands fetched (clean no-op, faithful to the lower
///   bound of "any number" per CR 119.x).
/// - <b>Untapped vs. tapped</b>: lands enter untapped per the printed
///   oracle. Lands with their own ETB-tapped replacements (shock lands,
///   bounce lands) ride through <see cref="ZoneServiceRegistry"/> when a
///   live <see cref="ZoneService"/> is registered for the caster, so
///   their ETB-tapped replacements + ETB triggers fire on tutored arrival.
/// </summary>
[CardName("Scapeshift")]
public static class ScapeshiftFactory
{
    public const string CardName = "Scapeshift";
    public const string PrintedManaCost = "{2}{G}{G}";

    /// <summary>
    /// Build a Scapeshift sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// closure via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Scapeshift's resolve effect.
    /// <paramref name="sacSelector"/> returns the lands the caster wants
    /// to sacrifice (controller's battlefield). The closure filters to
    /// lands legally on the caster's battlefield and dedupes by reference.
    /// N = number of lands actually sacrificed. <paramref name="tutorSelector"/>
    /// returns up to N land cards from the caster's library to put onto
    /// the battlefield untapped; non-lands, library-absent picks, and
    /// duplicates are filtered defensively. When <paramref name="tutorSelector"/>
    /// is null, the agent-driven per-slot loop from PrimevalTitan applies
    /// (sequential <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> with
    /// the library re-filtered each pass; null = decline). When
    /// <paramref name="sacSelector"/> is null, no lands are sacrificed
    /// (zero-N clean no-op).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        Func<Player, IReadOnlyList<ICard>>? sacSelector,
        Func<Player, IReadOnlyList<ICard>>? tutorSelector)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect($"{CardName}: sacrifice N lands, tutor N lands -> battlefield.", () =>
            {
                // ---- 1. Sacrifice phase (CR 701.16) -----------------------
                int sacrificed = 0;
                if (sacSelector != null)
                {
                    var picks = sacSelector(caster) ?? Array.Empty<ICard>();
                    var battlefield = caster.Zones.Battlefield.GetCards().ToHashSet();
                    var seen = new HashSet<ICard>();
                    foreach (var land in picks)
                    {
                        if (land == null) continue;
                        if (!land.HasType(CardType.Land)) continue;
                        if (!ReferenceEquals(land.Controller, caster)) continue;
                        if (!battlefield.Contains(land)) continue;
                        if (!seen.Add(land)) continue;
                        SacrificeToOwnerGraveyard(land);
                        sacrificed++;
                    }
                }

                if (sacrificed == 0) return; // "any number" lower bound — clean no-op.

                // ---- 2. Tutor phase (CR 701.19a) --------------------------
                if (tutorSelector != null)
                {
                    var picks = tutorSelector(caster) ?? Array.Empty<ICard>();
                    var library = caster.Zones.Library.GetCards().ToHashSet();
                    var seen = new HashSet<ICard>();
                    int placed = 0;
                    foreach (var pick in picks)
                    {
                        if (placed == sacrificed) break;
                        if (pick == null) continue;
                        if (!pick.HasType(CardType.Land)) continue;
                        if (!library.Contains(pick)) continue;
                        if (!seen.Add(pick)) continue;
                        MoveLibraryToBattlefield(caster, pick);
                        placed++;
                    }
                    return;
                }

                // Agent-driven path: sequential single-land tutors, refiltering
                // the library each pass so the agent never sees a previously
                // picked land. Mirrors PrimevalTitanFactory.TutorUpToTwoLandsTapped.
                // CR 701.19a — on the FIRST slot, always prompt the agent
                // (even with empty candidates) so a human searcher sees
                // the failed search. Subsequent slots short-circuit on
                // empty candidates (the player already acknowledged the
                // search; nothing more to surface). CR 701.20a — shuffle
                // once at the end regardless of how many cards were found.
                for (int slot = 0; slot < sacrificed; slot++)
                {
                    var candidates = caster.Zones.Library.GetCards()
                        .Where(c => c.HasType(CardType.Land))
                        .ToList();
                    if (candidates.Count == 0 && slot > 0) break;

                    var pick = Majik.Core.Zones.LibrarySearch.PromptOnly(
                        caster, candidates, "land card");
                    if (pick == null) break; // CR 701.19a — decline is legal.

                    MoveLibraryToBattlefield(caster, pick);
                }
                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(caster, "scapeshift");
            }),
        };
    }

    /// <summary>
    /// CR 701.16 — sacrifice routes a controller's permanent to its
    /// owner's graveyard, regardless of who controls it.
    /// </summary>
    private static void SacrificeToOwnerGraveyard(ICard land)
    {
        var controller = land.Controller;
        var owner = land.Owner ?? controller;
        if (controller != null)
        {
            controller.Zones.Battlefield.RemoveCard(land);
        }
        if (owner != null)
        {
            owner.Zones.Graveyard.AddCard(land);
        }
        land.SetZone(ZoneType.Graveyard);
    }

    /// <summary>
    /// Move <paramref name="pick"/> from <paramref name="caster"/>'s
    /// library to their battlefield untapped.
    /// <para>
    /// CR 603.6a / CR 614 — routes through
    /// <see cref="ZoneServiceRegistry"/> when a live
    /// <see cref="ZoneService"/> is registered for the caster so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes and ETB
    /// triggers (bounce-land bounce, Amulet of Vigor untap) +
    /// enters-tapped replacements (shock lands, bounce lands) fire on
    /// the tutored land. Falls back to raw zone mutation when no live
    /// service is registered (shape / dispatcher-test path).
    /// </para>
    /// </summary>
    private static void MoveLibraryToBattlefield(Player caster, ICard pick)
    {
        var zones = ZoneServiceRegistry.Get(caster);
        if (zones != null)
        {
            zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
        }
        else
        {
            caster.Zones.Library.RemoveCard(pick);
            caster.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(caster);
        }
    }
}
