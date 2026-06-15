using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reshape (Darksteel, {X}{U}{U}).
///
/// Sorcery. Oracle text (Scryfall verified):
///   "As an additional cost to cast this spell, sacrifice an artifact.
///    Search your library for an artifact card with mana value X or less,
///    put it onto the battlefield, then shuffle."
///
/// ## Why a named factory
/// Reshape is structurally <see cref="WhirOfInventionFactory"/>'s resolve
/// body — the X-bounded artifact tutor that puts the found card onto the
/// battlefield, then shuffles (CR 701.19a / CR 701.20a) — with the cost
/// helper swapped: instead of Improvise (CR 702.127), Reshape prints a
/// <b>mandatory</b> "sacrifice an artifact" additional cost (CR 601.2f),
/// the artifact analogue of <see cref="BoneSplintersFactory"/>'s
/// "sacrifice a creature" rider. Both halves reuse existing engine
/// primitives — the artifact tutor (mirroring Whir of Invention) and the
/// <see cref="SacrificeAnArtifactAdditionalCost"/> additional-cost rail
/// (mirroring Bone Splinters' <see cref="SacrificeACreatureAdditionalCost"/>)
/// — so no new mechanic is introduced.
///
/// ## Base shape
/// Name / Sorcery / {X}{U}{U} are materialised from the embedded JSON
/// definition (<c>reshape.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same JSON-backed posture as
/// <see cref="WhirOfInventionFactory"/>. The resolve-time tutor + the
/// additional cost live in <see cref="BuildSpellDefinition"/> because a
/// <see cref="SpellDefinition"/> needs the live caster reference + an
/// optional <see cref="Majik.Core.Services.ZoneService"/> that the
/// data-only JSON schema can't express.
///
/// ## Implemented (v1)
///
/// - <b>Sorcery shape</b>, printed cost <c>{X}{U}{U}</c>.
/// - <b>Mandatory additional cost (CR 601.2f)</b>:
///   <see cref="SacrificeAnArtifactAdditionalCost"/> declared on the
///   <see cref="SpellDefinition"/>. <see cref="SpellCastFlow"/> refuses the
///   cast when the caster controls no artifact (CR 601.2g — additional cost
///   that can't be paid → cast is illegal). Same posture as Bone Splinters.
/// - <b>Resolve-time tutor (CR 701.19a / CR 701.20a)</b>: search the
///   controller's library for an <em>artifact</em> card with mana value ≤ X
///   (CR 202.3b — mana value computed from the printed cost), put it onto
///   the battlefield, then shuffle. The Library → Battlefield move routes
///   through <see cref="Majik.Core.Services.ZoneService.MoveCard"/> when a
///   live service is supplied (so the tutored artifact publishes
///   <see cref="Majik.Core.Events.CardMovedEvent"/> and ETB triggers fire —
///   CR 603.6a); otherwise it falls back to
///   <see cref="Majik.Core.Services.ZoneServiceRegistry"/>, and finally to a
///   direct zone mutation (the shape/test path). Candidate filtering, the
///   agent prompt, and the post-search shuffle are identical to
///   <see cref="WhirOfInventionFactory"/>.
///
/// Note the X ceiling is the spell's own X (CR 202.3b read off
/// <c>ChosenSpellParams.X</c>), NOT the mana value of the sacrificed
/// artifact — Reshape's tutor bound is independent of the artifact paid for
/// the additional cost.
///
/// ## Deferred (v1 gaps — same as the analogue factories)
/// - <b>Sacrifice target prompt</b>: <see cref="SacrificeAnArtifactAdditionalCost"/>
///   picks the first artifact on the caster's battlefield deterministically.
///   Full agent-driven sacrifice-target prompting awaits the
///   ITarget / TargetResolver pipeline (same v1 posture as Bone Splinters /
///   Eldritch Evolution).
/// </summary>
[CardName("Reshape")]
public static class ReshapeFactory
{
    public const string CardName = "Reshape";
    public const string Slug = "reshape";
    public const string PrintedManaCost = "{X}{U}{U}";

    /// <summary>
    /// Build the card shape from the embedded JSON definition. The
    /// resolve-time tutor + additional cost are built on demand via
    /// <see cref="BuildSpellDefinition"/> so the caster reference matches
    /// the player resolving the spell. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Sorcery)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> Reshape uses on resolution.
    /// <see cref="SpellDefinition.HasVariableX"/> is true so the engine
    /// prompts for X at cast time; the resolve-time effect reads
    /// <c>ChosenSpellParams.X</c> as the mana-value ceiling for the artifact
    /// tutor. The mandatory sacrifice-an-artifact additional cost (CR 601.2f)
    /// is declared in <c>AdditionalCosts</c> so the cast flow pays + gates it.
    /// </summary>
    /// <param name="caster">Spell controller — the player whose library is
    /// searched and onto whose battlefield the picked artifact lands.</param>
    /// <param name="zones">Optional live
    /// <see cref="Majik.Core.Services.ZoneService"/>. When supplied the
    /// Library → Battlefield move publishes a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> so ETB triggers on the
    /// tutored artifact fire (CR 603.6a). When null the move falls back to the
    /// <see cref="Majik.Core.Services.ZoneServiceRegistry"/> and ultimately to
    /// direct zone mutation (shape/test path).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Majik.Core.Services.ZoneService? zones = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: true,
            TargetRequests: Array.Empty<TargetRequest>(),
            EffectFactory: p =>
            {
                var x = p.X ?? 0;
                return new IEffect[]
                {
                    new Effect($"Reshape: tutor artifact with mv ≤ {x} → battlefield", async ctx =>
                    {
                        // CR 701.19a — search consults the controller's agent.
                        // Pre-filter to artifact cards whose printed mana value
                        // ≤ X (CR 202.3b — mana value is computed from the
                        // printed cost). Identical to Whir of Invention.
                        var candidates = caster.Zones.Library.GetCards()
                            .Where(c =>
                                c.HasType(CardType.Artifact) &&
                                ManaCost.Parse(c.ManaCost).TotalValue <= x)
                            .ToList();

                        // CR 701.19a — prompt agent even on zero candidates so
                        // the human searcher sees the failed search.
                        var pick = await Majik.Core.Zones.LibrarySearch.PromptOnlyAsync(
                            ctx, caster, candidates,
                            $"artifact card with mana value {x} or less").ConfigureAwait(false);

                        if (pick != null)
                        {
                            // CR 603.6a — prefer the caller-supplied ZoneService;
                            // fall back to ZoneServiceRegistry so the
                            // dispatcher-driven cast flow still routes through the
                            // live ZoneService.
                            var effectiveZones = zones ?? Majik.Core.Services.ZoneServiceRegistry.Get(caster);
                            if (effectiveZones != null)
                            {
                                effectiveZones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, caster);
                            }
                            else
                            {
                                // Direct mutation fallback — same shape used by
                                // WhirOfInventionFactory. ETB triggers won't fire
                                // because no event publishes.
                                caster.Zones.Library.RemoveCard(pick);
                                caster.Zones.Battlefield.AddCard(pick);
                                pick.SetZone(ZoneType.Battlefield);
                                pick.SetController(caster);
                            }
                        }

                        // CR 701.20a — shuffle after a search effect, whether or
                        // not a card was found.
                        Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(caster, "reshape");
                    }),
                };
            },
            AdditionalCosts: new IAdditionalCost[] { new SacrificeAnArtifactAdditionalCost() });
    }
}
