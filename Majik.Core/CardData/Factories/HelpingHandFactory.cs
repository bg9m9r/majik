using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Helping Hand (Magic Origins / reprint pool, {W}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-14):
///   "Return target creature card with mana value 3 or less from your
///    graveyard to the battlefield tapped."
///
/// Helping Hand is the White, mana-value-capped sibling of
/// <see cref="FootstepsOfTheGoryoFactory"/> / <see cref="UnburialRitesFactory"/>:
/// it reanimates a creature card from the caster's own graveyard. The two
/// distinguishing clauses are:
///   - <b>Mana value 3 or less</b> filter on the legal target pool (CR 202.3b)
///     — same MV ≤ 3 gate as <see cref="UnearthFactory"/> / Priest of Fell
///     Rites' ETB.
///   - <b>Enters tapped</b> — after the graveyard → battlefield move the
///     returned permanent is tapped (CR 701.20 reanimation + CR 110.6 / the
///     "enters tapped" rider; tap via <see cref="Permanent.Tap()"/>).
///   - No haste, no end-step sacrifice, no life loss.
///
/// ## Card identity comes from JSON
/// Name / type / printed cost are loaded from the embedded JSON definition
/// (<c>helping-hand.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="FootstepsOfTheGoryoFactory"/>.
///
/// ## Implemented (v1)
/// - Sorcery shape at printed cost {W} (white, mana value 1), owner /
///   controller wired from JSON.
/// - <see cref="BuildSpellDefinition"/> — a single 1..1 "target creature card
///   with mana value 3 or less in your graveyard" <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Reanimate"/>). The candidate gatherer yields
///   creature cards in the caster's graveyard whose mana value (CR 202.3b) is
///   ≤ 3 ("your graveyard"). On resolution the target is re-checked per
///   CR 608.2b (must still be a creature card in the caster's graveyard with
///   MV ≤ 3); on success it is returned to the caster's battlefield via
///   <see cref="Fx.ReturnFromGraveyardToBattlefield"/> (ZoneService-routed when
///   supplied so ETB triggers fire — CR 603.6a) and then tapped
///   (CR 701.20 / "tapped" rider).
///
/// ## Relevant rules
/// - CR 202.3b — mana value = total of the printed mana cost's symbols.
/// - CR 701.20 — return a card from a graveyard to the battlefield.
/// - CR 110.2 — a permanent enters under the control of the player who put it
///   onto the battlefield.
/// - CR 603.6a — ETB triggers fire on the returned creature.
/// - CR 608.2b — illegal target at resolution → no-op.
///
/// ## Deferred (v1 gaps)
/// - <b>Real targeting prompt</b>: the live cast flow supplies the chosen
///   target through <see cref="ChosenSpellParams.Targets"/>; the resolver maps
///   tokens to live cards. Same posture as
///   <see cref="FootstepsOfTheGoryoFactory"/>.
/// </summary>
[CardName("Helping Hand")]
public static class HelpingHandFactory
{
    public const string CardName = "Helping Hand";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "helping-hand";

    /// <summary>Maximum mana value of the target creature card (CR 202.3b).</summary>
    public const int MaxManaValue = 3;

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {W}) from the
    /// embedded JSON definition. Resolve behaviour ("return target creature
    /// card with mana value 3 or less from your graveyard to the battlefield
    /// tapped") is built on demand via <see cref="BuildSpellDefinition"/>,
    /// mirroring <see cref="FootstepsOfTheGoryoFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the resolve-time "return target creature card with mana value 3
    /// or less from your graveyard to the battlefield tapped"
    /// <see cref="SpellDefinition"/>. Single 1..1 target request scoped to the
    /// caster's graveyard and capped at MV ≤ 3 (CR 202.3b); on resolution the
    /// target is re-validated per CR 608.2b, returned to the caster's
    /// battlefield, and tapped.
    /// </summary>
    /// <param name="caster">Spell controller — the graveyard whose creature
    /// card is returned ("your graveyard") and the destination battlefield
    /// (CR 110.2).</param>
    /// <param name="resolver">Maps the agent-supplied raw target token to the
    /// live engine object. Pass <c>o =&gt; o</c> for tests that hand cards
    /// directly.</param>
    /// <param name="zoneService">Optional. When supplied the graveyard →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player caster,
        Func<object, object> resolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature card with mana value 3 or less from your graveyard",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Reanimate,
                    // "your graveyard" + MV ≤ 3 (CR 202.3b) — only the caster's
                    // graveyard creature cards within the cap are legal sources
                    // (CR 608.2b re-checked at resolution).
                    CandidateGatherer: _ => caster.Zones.Graveyard.GetCards()
                        .OfType<Creature>()
                        .Where(c => c.ManaCostValue.TotalValue <= MaxManaValue)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen => new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: return target creature card (mana value ≤ {MaxManaValue}) from your graveyard to the battlefield tapped",
                    () => Resolve(caster, chosen, resolver, zoneService)),
            });
    }

    /// <summary>
    /// Resolve the return + tap. CR 608.2b — the target must still be a
    /// creature card in the caster's graveyard with mana value ≤
    /// <see cref="MaxManaValue"/>; otherwise the spell does nothing.
    /// </summary>
    private static void Resolve(
        Player caster,
        ChosenSpellParams chosen,
        Func<object, object> resolver,
        ZoneService? zoneService)
    {
        if (chosen.Targets.Count == 0 || chosen.Targets[0].Count == 0) return;

        var live = resolver(chosen.Targets[0][0]);

        // CR 608.2b — illegal-on-resolution checks: must be a creature card,
        // still in the graveyard, still owned by the caster ("your graveyard"),
        // and still within the MV ≤ 3 cap (CR 202.3b).
        if (live is not Creature creature) return;
        if (creature.Zone != ZoneType.Graveyard) return;
        if (!ReferenceEquals(creature.Owner, caster)) return;
        if (creature.ManaCostValue.TotalValue > MaxManaValue) return;

        // CR 701.20 — graveyard → battlefield under the caster's control
        // (CR 110.2). ZoneService-routed when supplied so ETB triggers fire
        // (CR 603.6a). No life loss — Helping Hand has no such clause.
        Fx.ReturnFromGraveyardToBattlefield(creature, caster, zoneService);

        // "... to the battlefield tapped." The returned permanent enters
        // tapped (the rider applies as the creature arrives). Tap via
        // Permanent.Tap() once it is on the battlefield.
        if (creature.Zone == ZoneType.Battlefield && !creature.IsTapped)
        {
            creature.Tap();
        }
    }
}
