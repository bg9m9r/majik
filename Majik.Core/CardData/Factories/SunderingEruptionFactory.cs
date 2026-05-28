using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.MDFCs;
using Majik.Core.CardData.SpellTemplates.Templates.Search;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Sundering Eruption // Volcanic Fissure (Innistrad: Reawakening, {2}{R}).
///
/// Sorcery. Oracle text (front):
///   "Destroy target land. Its controller may search their library for a
///    basic land card, put it onto the battlefield tapped, then shuffle.
///    Creatures without flying can't block this turn."
///
/// Back face — <see cref="VolcanicFissureFactory"/> (Land — "As this land
/// enters, you may pay 3 life. If you don't, it enters tapped."
/// / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Two-factory dispatch: casting the front face resolves "Sundering Eruption"
/// → this factory → a <see cref="Sorcery"/> with the destroy + compensation
/// + blocking-restriction effects. Playing the back face resolves
/// "Volcanic Fissure" → <see cref="VolcanicFissureFactory"/> → a
/// painland-style <see cref="Land"/>.
///
/// ## Implemented (v1)
/// - Sorcery identity at {2}{R}, red (mono-R from the {R} pip), mana
///   value 3. Owner / controller wired.
/// - <see cref="MdfcState"/> attached (front = "Sundering Eruption",
///   back = "Volcanic Fissure"); starts on the front face.
/// - <b>Destroy target land</b> — single 1..1 <see cref="TargetRequest"/>
///   (Intent: <see cref="BotIntent.Removal"/>) whose candidate gatherer
///   enumerates every land permanent on the battlefield across all players.
///   On resolution the target land is moved to its owner's graveyard
///   (CR 701.7b) iff it is still a land on the battlefield at resolution
///   (CR 608.2b — illegal target → no-op).
/// - <b>Optional basic-land compensation</b> — after the land is destroyed,
///   the controller of the destroyed land MAY search their library for a
///   basic land card, put it onto the battlefield tapped, then shuffle
///   (CR 701.19a). Modelled as an optional agent prompt
///   (<see cref="IPlayerAgent.ChooseYesNoAsync"/> with
///   <see cref="BotIntent.Ramp"/>) followed by
///   <see cref="SearchSpellFactory"/>-style raw land tutor when accepted.
///   Declining or having no agent → no search (legal under CR 701.19a).
/// - <b>"Creatures without flying can't block this turn"</b> — one-shot
///   EOT-scoped <see cref="CombatRestrictionEffect"/> with predicate
///   <c>c =&gt; !CombatAbilities.HasFlying(c) &amp;&amp; !CombatAbilities.CanBlockFlying(c)</c>
///   registered on the supplied <see cref="ContinuousEffectsService"/>.
///   <see cref="CombatValidator.CanBlock"/> queries the service; flying /
///   reach creatures are excluded from the predicate and may still block.
///   The restriction expires at end of turn (CR 514.2).
///
/// ## References
/// - MDFC factory pair: <see cref="FellTheProfaneFactory"/> /
///   <see cref="FellMireFactory"/> (PR #1018).
/// - Destroy land: <see cref="StripMineFactory"/> / <see cref="WastelandFactory"/>.
/// - Optional basic-land tutor: <see cref="SettleTheWreckageFactory"/>'s
///   agent-prompt + raw library-search shape.
/// - Ground-can't-block one-shot: <see cref="EnsnaringBridgeFactory"/>'s
///   predicate-mode <see cref="CombatRestrictionEffect"/> shape; EOT expiry
///   mirrors <see cref="EarthshakerKhenraFactory"/>.
/// </summary>
[CardName("Sundering Eruption")]
public static class SunderingEruptionFactory
{
    public const string CardName = "Sundering Eruption";
    public const string BackName = "Volcanic Fissure";
    public const string PrintedManaCost = "{2}{R}";

    // CR 305.6 — basic land names used by the compensation search.
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Construct the front face of Sundering Eruption as a Sorcery with
    /// owner / controller wired and the <see cref="MdfcState"/> face tracker
    /// attached (starts on the front face).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Volcanic Fissure) is observable from the
        // front-face card object. Starts on the front face.
        card.MdfcState = new MdfcState(CardName, BackName);
        return card;
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/> for
    /// Sundering Eruption.
    ///
    /// CR 608.2b — illegal-target re-check at resolution: if the target
    /// is no longer a land on the battlefield the destroy step is skipped
    /// (and by extension the compensation search is also skipped, because
    /// there was no destroyed land). The ground-can't-block restriction is
    /// ALWAYS registered when the spell resolves (it is not gated on the
    /// target being legal — the oracle puts no such gate on the restriction).
    /// </summary>
    /// <param name="caster">Sundering Eruption's controller; used only to
    /// satisfy the effect label and for resolving the blocking restriction
    /// registration context.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests.</param>
    /// <param name="effects">Optional <see cref="ContinuousEffectsService"/>
    /// on which the "creatures without flying can't block this turn"
    /// <see cref="CombatRestrictionEffect"/> will be registered. When null
    /// the restriction is skipped (shape-only tests).</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ContinuousEffectsService? effects = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Live gatherer: all land permanents across every player.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Land))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target land + compensation search + ground-can't-block",
                        () => Resolve(resolved, effects)),
                };
            });
    }

    // -------------------------------------------------------------------------
    // Resolution body
    // -------------------------------------------------------------------------

    private static void Resolve(
        object resolved,
        ContinuousEffectsService? effects)
    {
        // Step 1: Destroy target land (CR 701.7b / CR 608.2b).
        Player? destroyedLandController = null;
        if (resolved is Permanent target
            && target.Zone == ZoneType.Battlefield
            && target.HasType(CardType.Land))
        {
            destroyedLandController = target.Controller ?? target.Owner;
            DestroyToOwnersGraveyard(target);
        }

        // Step 2: Optional compensation search — the controller of the
        // destroyed land MAY search their library for a basic land card,
        // put it onto the battlefield tapped, then shuffle (CR 701.19a).
        // Only offered when the destroy step succeeded (destroyedLandController != null).
        if (destroyedLandController != null)
        {
            OfferCompensationSearch(destroyedLandController);
        }

        // Step 3: "Creatures without flying can't block this turn."
        // Registered unconditionally on resolution (not gated on target
        // legality). EOT-scoped (CR 514.2 — "this turn" effects expire at
        // end of turn). Flying and reach creatures are excluded because they
        // CAN block creatures with flying — the oracle restricts creatures
        // WITHOUT flying, and reach grants equivalent can-block-flying ability.
        if (effects != null)
        {
            effects.Register(new CombatRestrictionEffect(
                restriction: CombatRestriction.CannotBlock,
                predicate: c => !CombatAbilities.HasFlying(c) && !CombatAbilities.CanBlockFlying(c),
                isActiveGate: null,
                expiresAtEndOfTurn: true));
        }
    }

    /// <summary>
    /// CR 701.19a — the destroyed land's controller may search their library
    /// for a basic land card, put it onto the battlefield tapped, then
    /// shuffle. Prompt the controller's registered agent; declining or having
    /// no agent is legal (no-op).
    /// </summary>
    private static void OfferCompensationSearch(Player landController)
    {
        // CR 119.4 does not apply here — no life payment. The agent decides
        // whether to search (they always can unless library is empty).
        var agent = AgentRegistry.Get(landController);
        bool wantsToSearch;
        if (agent == null)
        {
            // No agent registered → decline (deterministic fallback).
            return;
        }

        try
        {
            wantsToSearch = agent.ChooseYesNoAsync(
                question: "Search your library for a basic land card and put it onto the battlefield tapped?",
                intent: BotIntent.Ramp,
                ct: default)
                .GetAwaiter().GetResult();
        }
        catch
        {
            // Defensive: any agent failure → decline.
            return;
        }

        if (!wantsToSearch) return;

        // Find basic land candidates in the controller's library.
        var candidates = landController.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();

        if (candidates.Count == 0)
        {
            // Nothing to find — shuffle still happens (CR 701.20a).
            LibraryShuffle.ShuffleLibrary(landController, "sundering-eruption/compensation/empty");
            return;
        }

        // Pick one basic land card.
        ICard? pick;
        try
        {
            pick = agent.ChooseLibraryPickAsync(
                ctx: null,
                candidates: candidates,
                kindLabel: "basic land card")
                .GetAwaiter().GetResult();
        }
        catch
        {
            pick = null;
        }

        if (pick != null)
        {
            // Put it onto the battlefield tapped.
            var zones = ZoneServiceRegistry.Get(landController);
            if (zones != null)
            {
                zones.MoveCard(pick, ZoneType.Library, ZoneType.Battlefield, landController);
                if (pick is Permanent perm && !perm.IsTapped) perm.Tap();
            }
            else
            {
                landController.Zones.Library.RemoveCard(pick);
                landController.Zones.Battlefield.AddCard(pick);
                pick.SetZone(ZoneType.Battlefield);
                if (pick is Permanent permRaw) permRaw.Tap();
            }
        }

        // CR 701.20a — shuffle after the search, even when no card was picked.
        LibraryShuffle.ShuffleLibrary(landController, "sundering-eruption/compensation");
    }

    /// <summary>
    /// CR 701.7b — move the destroyed land to its owner's graveyard.
    /// Mirrors <see cref="StripMineFactory"/>'s DestroyToOwnersGraveyard helper.
    /// </summary>
    private static void DestroyToOwnersGraveyard(Permanent target)
    {
        var owner = target.Owner;
        if (owner == null) return;

        var holder = target.Controller ?? owner;
        holder.Zones.Battlefield.RemoveCard(target);
        owner.Zones.Graveyard.AddCard(target);
        target.SetZone(ZoneType.Graveyard);
    }
}
