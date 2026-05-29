using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Neoform (War of the Spark, {G}{U}).
///
/// Sorcery. Oracle text:
///   "As an additional cost to cast this spell, sacrifice a creature.
///    Search your library for a creature card with mana value equal to 1
///    plus the sacrificed creature's mana value, put that card onto the
///    battlefield with an additional +1/+1 counter on it, then shuffle."
///
/// ## Implemented (v1)
///
/// - Sorcery shape, mana cost <c>{G}{U}</c>.
/// - Additional cost (CR 601.2f): <see cref="SacrificeACreatureAdditionalCost"/>
///   declared on the <see cref="SpellDefinition"/>. <see cref="SpellCastFlow"/>
///   refuses the cast when the caster controls no creature (CR 601.2g —
///   additional cost that can't be paid → cast is illegal). Same posture
///   as <see cref="EldritchEvolutionFactory"/> / <see cref="BoneSplintersFactory"/>.
/// - Resolve effect:
///   1. Reads the sacrificed creature's MV from the paid additional-cost
///      payment (CR 202.3). Target MV = sac.MV + 1 — EXACT, not
///      "or less/greater" (distinguishes Neoform from Eldritch Evolution).
///   2. Prompts the caster's agent (via
///      <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>) for a creature
///      card in the library whose MV is exactly = sac.MV + 1
///      (CR 701.19a — find / no-find both legal).
///   3. Moves the picked creature Library → Battlefield, routing through
///      <see cref="ZoneService"/> when available so ETB triggers fire
///      (CR 603.6a) — same routing as <see cref="EldritchEvolutionFactory"/>
///      / <see cref="ChordOfCallingFactory"/>.
///   4. Places one +1/+1 counter on the creature that just entered the
///      battlefield. Counter is applied via
///      <see cref="CountersService.Add"/> with
///      <see cref="CounterType.PlusOnePlusOne"/> so Hardened Scales /
///      Doubling Season replacements (CR 614) can fire.
///   5. Shuffles the library (CR 701.20a — shuffle after a search,
///      whether or not a card was found).
///
/// ## Deferred (v1 gaps)
///
/// - <b>Sacrifice-target prompt</b>: <see cref="SacrificeACreatureAdditionalCost"/>
///   picks the first creature on the caster's battlefield deterministically
///   (same v1 gap as Eldritch Evolution / Bone Splinters / Fling). Full
///   agent-driven sacrifice-target prompting requires the ITarget /
///   TargetResolver pipeline.
/// - <b>Replacement-effect ordering</b>: when Doubling Season is on the
///   battlefield and Hardened Scales is also present, counter replacements
///   stack according to APNAP; the <see cref="CountersService.Add"/> call
///   passes a null <see cref="Majik.Core.Effects.ReplacementBus"/> in the
///   raw-zone test path. Full replacement wiring requires a live
///   <see cref="Majik.Core.Effects.ReplacementBus"/> reference injected
///   into <see cref="BuildSpellDefinition"/>.
/// </summary>
[CardName("Neoform")]
public static class NeoformFactory
{
    public const string CardName = "Neoform";
    public const string PrintedManaCost = "{G}{U}";

    /// <summary>
    /// Build a Neoform sorcery owned by <paramref name="owner"/>.
    /// Card shape only — the resolve-time spell definition is built on
    /// demand via <see cref="BuildSpellDefinition"/>.
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
    /// Build the <see cref="SpellDefinition"/> used when Neoform is cast.
    /// Declares the printed sacrifice-a-creature additional cost (CR 601.2f).
    /// On resolution: searches the library for a creature with MV exactly
    /// equal to sacrificed creature's MV + 1, puts it onto the battlefield
    /// with one +1/+1 counter, then shuffles.
    /// </summary>
    /// <param name="caster">Player resolving the spell — the library to
    /// search and controller of the tutored creature.</param>
    /// <param name="zoneService">Optional. When supplied the tutored
    /// creature's Library → Battlefield move routes through the service
    /// so <c>CardMovedEvent</c> and ETB triggers fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                // Resolve the sacrificed-creature reference from the paid
                // additional-cost list (populated by SpellCastFlow after
                // CR 601.2f cost payment). Tests that exercise the effect
                // directly thread a ChosenSpellParams with the
                // SacrificeACreatureAdditionalCost already paid.
                Creature? sacrificed = null;
                foreach (var paid in p.AdditionalCostPaymentsOrEmpty)
                {
                    if (paid is SacrificeACreatureAdditionalCost sac && sac.Sacrificed is Creature c)
                    {
                        sacrificed = c;
                        break;
                    }
                }

                return new IEffect[]
                {
                    new Effect($"{CardName}: tutor creature mv = sac.mv + 1, put onto battlefield with +1/+1 counter, shuffle", () =>
                    {
                        // No sacrificed reference → can't compute MV;
                        // resolve as no-op to avoid tutoring at an
                        // unbounded value (CR 701.19a — "search … for a
                        // card" implicitly requires a legal cost was paid).
                        if (sacrificed == null) return;

                        // CR 202.3 — MV of a card with a mana cost is the
                        // total converted value of all mana symbols in
                        // that cost. Use ManaCost.Parse / TotalValue to
                        // match Eldritch Evolution / Birthing Ritual.
                        var sacMv = ManaCost.Parse(
                            sacrificed.ManaCost ?? string.Empty).TotalValue;
                        var targetMv = sacMv + 1;

                        // Build the candidate list: creatures in the
                        // library whose MV == targetMv (EXACT — Neoform
                        // does not say "or less" like Eldritch Evolution).
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c =>
                                c.HasType(CardType.Creature)
                                && ManaCost.Parse(c.ManaCost ?? string.Empty)
                                           .TotalValue == targetMv)
                            .ToList();

                        // CR 701.19a — prompt agent even on zero candidates
                        // so the human searcher sees the failed search.
                        var pick = Majik.Core.Zones.LibrarySearch.PromptOnly(
                            caster, candidates,
                            $"creature card with mana value exactly {targetMv}");

                        if (pick != null)
                        {
                            // CR 603.6a — prefer caller-supplied zoneService;
                            // fall back to ZoneServiceRegistry so the
                            // dispatcher-driven cast flow routes through the
                            // live ZoneService even when BuildSpellDefinition
                            // is invoked without an explicit service ref.
                            var effectiveZones = zoneService
                                ?? ZoneServiceRegistry.Get(caster);

                            if (effectiveZones != null)
                            {
                                effectiveZones.MoveCard(
                                    pick, ZoneType.Library, ZoneType.Battlefield, caster);
                            }
                            else
                            {
                                caster.Zones.Library.RemoveCard(pick);
                                caster.Zones.Battlefield.AddCard(pick);
                                pick.SetZone(ZoneType.Battlefield);
                                pick.SetController(caster);
                            }

                            // Place one +1/+1 counter on the creature that
                            // just entered the battlefield (printed text:
                            // "put that card onto the battlefield with an
                            // additional +1/+1 counter on it").
                            // Route through CountersService so Hardened
                            // Scales / Doubling Season replacements (CR 614)
                            // fire when a ReplacementBus is wired in a
                            // future integration. Null bus = direct add
                            // (same conservative shape as ThaliaLieutenant
                            // shape-tests).
                            if (pick is Permanent permanent)
                            {
                                CountersService.Add(
                                    permanent,
                                    CounterType.PlusOnePlusOne,
                                    amount: 1,
                                    replacements: null);
                            }
                        }

                        // CR 701.20a — shuffle after the search whether or
                        // not a card was found (matches Eldritch Evolution /
                        // Chord of Calling shuffle posture).
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(
                            caster, "neoform");
                    }),
                };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeACreatureAdditionalCost() });
    }
}
