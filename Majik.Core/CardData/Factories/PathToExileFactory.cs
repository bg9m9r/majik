using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Path to Exile (Conflux, {W}).
///
/// Instant. Oracle text:
///   "Exile target creature. Its controller may search their library for a
///    basic land card, put that card onto the battlefield tapped, then
///    shuffle their library."
///
/// ## Implementation
///
/// CR 701.21 — Exile target creature. The "its controller" pronoun refers
/// to the creature's controller at the moment Path to Exile resolves (CR
/// 608.2b reads the target's then-controller). The tutor rider runs as
/// part of the same resolution: that player may search their library for a
/// basic land (CR 305.6), put it onto the battlefield tapped, and shuffle
/// (CR 701.19a + CR 701.19c).
///
/// The library search consults the exiled creature's controller's
/// registered agent via <see cref="AgentRegistry"/> — the same primitive
/// <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/> uses
/// for basic-land tutors. The agent may decline (return <c>null</c>) per
/// CR 701.19a — the printed text says "may", and even if the player
/// commits to searching they may legally find nothing.
///
/// ## Deferred (v1 gaps)
/// - <b>Library shuffle</b>: <c>IZone.Shuffle</c> is not yet exposed; the
///   search consumes the picked card but no actual reordering happens
///   (same MVP gap as every other tutor in the codebase — see
///   <see cref="SpellTemplates.Templates.Search.SearchSpellFactory"/>).
/// - <b>Reveal-on-find</b>: tutor effects in this engine don't yet emit
///   a reveal event for the picked card. Cosmetic for engine purposes.
/// - <b>Illegal-target fizzle</b>: handled by <see cref="SpellCastFlow"/>
///   at resolution-time target legality (CR 608.2b); if all targets are
///   illegal the spell does nothing, including no tutor.
/// </summary>
public static class PathToExileFactory
{
    public const string CardName = "Path to Exile";
    public const string PrintedManaCost = "{W}";

    /// <summary>Basic land names per CR 305.6.</summary>
    private static readonly HashSet<string> BasicLandNames =
        new(StringComparer.OrdinalIgnoreCase)
        { "Plains", "Island", "Swamp", "Mountain", "Forest", "Wastes" };

    /// <summary>
    /// Build a Path to Exile instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildSpellDefinition"/> for the
    /// resolve-time exile-then-tutor effect.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Path to Exile is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature is exiled (CR 701.21) and its controller is
    /// offered the basic-land tutor (CR 701.19a).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect("Path to Exile: exile target creature + basic-land tutor", () =>
                    {
                        if (raw is not Creature target) return;
                        if (target.Zone != ZoneType.Battlefield) return; // illegal at resolution

                        // Snapshot controller BEFORE moving the creature — the
                        // tutor offer goes to "its controller" at resolution
                        // (CR 608.2b last-known-information).
                        var targetController = target.Controller ?? target.Owner;

                        // Exile (CR 701.21).
                        var fromOwner = target.Owner;
                        if (fromOwner != null)
                        {
                            fromOwner.Zones.Battlefield.RemoveCard(target);
                            fromOwner.Zones.Exile.AddCard(target);
                        }
                        target.SetZone(ZoneType.Exile);

                        // Basic-land tutor rider — "its controller may
                        // search their library for a basic land card,
                        // put that card onto the battlefield tapped,
                        // then shuffle their library" (CR 701.19a).
                        if (targetController == null) return;
                        TutorBasicLandTapped(targetController);
                    }),
                };
            });
    }

    /// <summary>
    /// Offer <paramref name="player"/>'s registered agent the basic-land
    /// tutor. Picks via <see cref="IPlayerAgent.ChooseLibraryPickAsync"/>;
    /// returning <c>null</c> declines (CR 701.19a — "may search" plus
    /// "even if a player declines, they searched"). When no agent is
    /// registered, falls back to first basic candidate (deterministic
    /// test default — mirrors <c>SearchSpellFactory</c>).
    /// </summary>
    private static void TutorBasicLandTapped(Player player)
    {
        var candidates = player.Zones.Library.GetCards()
            .Where(c => c.HasType(CardType.Land) && BasicLandNames.Contains(c.Name))
            .ToList();
        if (candidates.Count == 0) return;

        var agent = AgentRegistry.Get(player);
        ICard? pick = agent != null
            ? agent.ChooseLibraryPickAsync(ctx: null, candidates, "basic land card")
                .GetAwaiter().GetResult()
            : candidates[0];
        if (pick == null) return; // CR 701.19a — decline is legal.

        player.Zones.Library.RemoveCard(pick);
        player.Zones.Battlefield.AddCard(pick);
        pick.SetZone(ZoneType.Battlefield);
        pick.SetController(player);
        if (pick is Permanent perm)
        {
            perm.Tap();
        }
        // CR 701.19c — shuffle after a search effect. Skipped for MVP
        // (no IZone.Shuffle entry point yet; same rationale as
        // SearchSpellFactory).
    }
}
