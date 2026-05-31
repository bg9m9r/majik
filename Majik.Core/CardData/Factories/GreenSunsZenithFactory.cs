using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Green Sun's Zenith (Mirrodin Besieged + Modern
/// Horizons 2, {X}{G}).
///
/// Sorcery. Oracle text:
///   "Search your library for a green creature card with mana value X or
///    less, put it onto the battlefield, then shuffle. Shuffle Green Sun's
///    Zenith into its owner's library."
///
/// ## Implemented (v1)
///
/// - Sorcery shape, printed cost <c>{X}{G}</c>.
/// - Resolve-time tutor: search the controller's library for a green
///   Creature card with mana value ≤ X (CR 701.19a; CR 202.3b mana value
///   from the printed cost; CR 105.2a colour derived from the mana cost
///   pips via <see cref="CardColors.GetColors"/>). Routed through
///   <see cref="ZoneService.MoveCard"/> when a live service is supplied
///   so the tutored permanent publishes
///   <see cref="Majik.Core.Events.CardMovedEvent"/> and ETB triggers fire
///   (CR 603.6a — mirrors ChordOfCallingFactory / EldritchEvolutionFactory
///   PR #145 / #174 wiring). When no <c>ZoneService</c> is supplied (the
///   single-arg test path) the move is done via direct zone mutation,
///   identical to <see cref="SearchSpellFactory.GreenSunsZenithSpell"/>.
/// - Agent prompt: tutor candidates are filtered to green creature cards
///   with <c>ManaCostValue.TotalValue ≤ X</c>, then the controller's
///   registered <see cref="IPlayerAgent"/> picks via
///   <c>ChooseLibraryPickAsync</c>. No agent registered = deterministic
///   first-match fallback. Empty candidates or null pick = no-op (CR
///   701.19a permits declining to find).
/// - Self-shuffle to library (CR 701.20a / CR 608.2c printed override of
///   the default sorcery-to-graveyard destination): after the tutor
///   finishes (find-and-no-find branches alike), the Green Sun's Zenith
///   card itself is moved Graveyard / Stack / wherever-it-currently-is
///   → its owner's library. Routed through <see cref="ZoneService"/>
///   when available; raw zone manipulation otherwise. Same overall shape
///   as <see cref="EldritchEvolutionFactory"/>'s self-exile move.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Replacing the spell's destination via the stack resolver</b>.
///   Same gap noted on <see cref="EldritchEvolutionFactory"/>: the
///   printed self-shuffle is implemented inside the resolve closure
///   rather than via a per-card destination-zone override on
///   <see cref="StackResolver"/>. <see cref="StackResolver"/> still tries
///   to move the card from the stack to the graveyard after the effect
///   runs; the closure moves it onward to the library AFTER the tutor
///   completes, so the visible end state is "card in owner's library"
///   regardless of which path the post-effect re-move took. A generic
///   "ShuffleSourceToLibraryOnResolve" hook in <see cref="SpellCastFlow"/>
///   would let the destination override happen in the right place — out
///   of scope for this v1 named-card factory.
/// </summary>
[CardName("Green Sun's Zenith")]
public static class GreenSunsZenithFactory
{
    public const string CardName = "Green Sun's Zenith";
    public const string PrintedManaCost = "{X}{G}";

    /// <summary>
    /// Build a Green Sun's Zenith sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/> so the caster
    /// reference matches the player resolving the spell, the source
    /// card reference for the self-shuffle is the specific instance the
    /// spell was cast from, and a live <see cref="ZoneService"/> can be
    /// threaded in.
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
    /// Build the <see cref="SpellDefinition"/> Green Sun's Zenith uses on
    /// resolution. <see cref="SpellDefinition.HasVariableX"/> is true so
    /// the engine prompts for X at cast time; the resolve-time effect
    /// reads <c>ChosenSpellParams.X</c> as the mana-value ceiling for the
    /// green-creature tutor.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library
    /// is searched and onto whose battlefield the picked creature lands.</param>
    /// <param name="card">The Green Sun's Zenith card instance whose
    /// resolve-time effect chain is being built. The closure captures
    /// this reference so the final self-shuffle targets the specific
    /// card the spell was cast from (rather than a name lookup).</param>
    /// <param name="zoneService">Optional. When supplied, the tutored
    /// creature's library→battlefield move and the Green Sun's Zenith
    /// self-shuffle move both route through the service so
    /// <c>CardMovedEvent</c> + ETB triggers fire (CR 603.6a). When null,
    /// raw zone manipulation is used (shape / dispatcher-test path).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ICard card,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(card);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect($"Green Sun's Zenith: tutor green creature with mv ≤ {x} → battlefield", async ctx =>
                    {
                        // CR 701.19a — search consults the controller's
                        // agent (if any). Pre-filter to green creature
                        // cards whose printed mana value ≤ X (CR 202.3b —
                        // mana value is computed from the printed cost;
                        // CR 105.2a — colour is derived from the cost
                        // pips via CardColors.GetColors, mirroring the
                        // existing GreenSunsZenithPatternTemplate).
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c =>
                                c.HasType(CardType.Creature) &&
                                CardColors.GetColors(c).Contains(ManaColor.Green) &&
                                ManaCost.Parse(c.ManaCost).TotalValue <= x)
                            .ToList();

                        // CR 701.19a — LibrarySearch.PromptOnly always
                        // prompts the agent (even when candidates is empty,
                        // so a human searcher sees the full library with
                        // no eligible cards and a single Acknowledge
                        // button rather than the spell silently no-opping).
                        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
                            ctx, caster, candidates,
                            $"green creature card with mana value {x} or less").ConfigureAwait(false);

                        // CR 603.6a — prefer caller-supplied zoneService;
                        // fall back to ZoneServiceRegistry so the
                        // dispatcher-driven cast flow routes through the
                        // live ZoneService even when BuildSpellDefinition
                        // is invoked without an explicit service ref.
                        var effectiveZones = zoneService
                            ?? Majik.Core.Services.ZoneServiceRegistry.Get(caster);
                        if (pick != null)
                        {
                            if (effectiveZones != null)
                            {
                                effectiveZones.MoveCard(
                                    pick, ZoneType.Library, ZoneType.Battlefield, caster);
                            }
                            else
                            {
                                // Direct mutation fallback — same shape used by
                                // SearchSpellFactory.GreenSunsZenithSpell. ETB
                                // triggers won't fire because no event publishes.
                                caster.Zones.Library.RemoveCard(pick);
                                caster.Zones.Battlefield.AddCard(pick);
                                pick.SetZone(ZoneType.Battlefield);
                                pick.SetController(caster);
                            }
                        }
                        // CR 701.20a — shuffle after the search effect,
                        // whether or not a card was actually found.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "green-suns-zenith");
                    }),
                    new Effect("Green Sun's Zenith: shuffle self into owner's library", () =>
                    {
                        // CR 701.20a / CR 608.2c — printed "Shuffle Green
                        // Sun's Zenith into its owner's library" overrides
                        // the default sorcery-to-graveyard destination.
                        // Move the card to its owner's library from
                        // wherever it currently is (stack on the live
                        // path; hand when tests bypass SpellCastFlow;
                        // graveyard if StackResolver already routed it
                        // post-effect — see class xmldoc for the
                        // sequencing note). The library shuffle itself
                        // runs via LibraryShuffle after re-insertion.
                        var ownerPlayer = card.Owner ?? caster;
                        var fromZone = card.Zone;
                        if (fromZone == ZoneType.Library) return;
                        if (zoneService != null)
                        {
                            zoneService.MoveCard(card, fromZone, ZoneType.Library, ownerPlayer);
                        }
                        else
                        {
                            switch (fromZone)
                            {
                                case ZoneType.Hand:
                                    ownerPlayer.Zones.Hand.RemoveCard(card);
                                    break;
                                case ZoneType.Graveyard:
                                    ownerPlayer.Zones.Graveyard.RemoveCard(card);
                                    break;
                                case ZoneType.Battlefield:
                                    ownerPlayer.Zones.Battlefield.RemoveCard(card);
                                    break;
                                case ZoneType.Exile:
                                    ownerPlayer.Zones.Exile.RemoveCard(card);
                                    break;
                                case ZoneType.Stack:
                                    // Card is on the stack — no per-player
                                    // collection to remove from. The stack
                                    // pops the spell separately.
                                    break;
                            }
                            ownerPlayer.Zones.Library.AddCard(card);
                            card.SetZone(ZoneType.Library);
                        }
                        // CR 701.20a / printed GSZ rider — explicit "shuffle"
                        // after re-inserting the spell itself.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(ownerPlayer, "green-suns-zenith-self");
                    }),
                };
            });
    }
}
