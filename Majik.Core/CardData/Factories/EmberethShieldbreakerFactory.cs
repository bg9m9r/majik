using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Embereth Shieldbreaker // Battle Display (Throne
/// of Eldraine, {1}{R}).
///
/// ## Card text
/// - Embereth Shieldbreaker — Creature — Human Knight {1}{R}, 2/1.
///     (Vanilla on its creature half — no keywords / triggers.)
/// - Battle Display (Adventure) — Sorcery — Adventure {R}.
///     "Destroy target artifact."
///
/// ## Implemented (v1)
/// - 2/1 Human Knight creature with mana cost {1}{R}.
/// - <b>Battle Display helper</b>: <see cref="BuildAdventureSpell"/> returns
///   a standalone <see cref="SpellDefinition"/> matching the Battle Display
///   shape — a single 1..1 "target artifact" target request whose resolve
///   effect destroys the chosen artifact permanent
///   (<see cref="OracleSpellBinder.MoveToGraveyard"/>, CR 701.7). The
///   helper is exposed so callers / tests can drive the "side spell" path
///   even though the engine has no Adventure cast pipeline yet (see
///   Deferred).
///
/// ## Deferred (v1 gaps)
/// - <b>Adventure cast-from-hand-to-exile (CR 715)</b>: matches the gap
///   documented on <see cref="BonecrusherGiantFactory"/> /
///   <see cref="MurderousRiderFactory"/>. Adventures require:
///     1. A split-card / dual-faced data model where casting the Adventure
///        face exiles the card if it resolves instead of going to the
///        graveyard (CR 715.2),
///     2. An alternative-cost / cast-from-exile rule that lets the owner
///        cast Embereth Shieldbreaker from exile until it leaves exile
///        (CR 715.3).
///   Until that pipeline exists, callers wanting the Battle Display shape
///   should invoke <see cref="BuildAdventureSpell"/> directly to obtain a
///   standalone destroy-target-artifact <see cref="SpellDefinition"/>.
/// - <b>Indestructible / regeneration riders</b> on the Battle Display
///   destroy path — inherited from
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>; same gap as
///   <see cref="SlaughterPactFactory"/> and the rest of the single-target
///   destroy family.
/// </summary>
public static class EmberethShieldbreakerFactory
{
    public const string CardName = "Embereth Shieldbreaker";
    public const string PrintedManaCost = "{1}{R}";

    public const string AdventureName = "Battle Display";
    public const string AdventureManaCost = "{R}";

    /// <summary>
    /// Construct Embereth Shieldbreaker (creature half). The card has no
    /// printed keywords or triggers on its creature face — it's a vanilla
    /// 2/1 Human Knight. The Battle Display Adventure half is exposed via
    /// <see cref="BuildAdventureSpell"/>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 2,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Knight });

        card.SetOwner(owner);
        card.SetController(owner);

        return card;
    }

    /// <summary>
    /// Build the standalone Battle Display <see cref="SpellDefinition"/>.
    /// The caller resolves the chosen target through
    /// <paramref name="targetResolver"/> (typically a
    /// <c>StackResolver</c>); on resolution the chosen artifact is
    /// destroyed (CR 701.7).
    /// </summary>
    /// <param name="caster">The controller of Battle Display.</param>
    /// <param name="targetResolver">Resolves the raw target token to a
    /// live engine object (typically a <see cref="Permanent"/>).</param>
    public static SpellDefinition BuildAdventureSpell(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect("Battle Display: destroy target artifact", () =>
                    {
                        // CR 701.7 — destroy → owner's graveyard. CR 608.2b
                        // illegal-target check: must still be an Artifact
                        // permanent at resolution. Indestructible /
                        // regeneration deferred (same gap as SlaughterPact).
                        if (resolved is Permanent permanent
                            && permanent.HasType(CardType.Artifact))
                        {
                            OracleSpellBinder.MoveToGraveyard(permanent);
                        }
                    }),
                };
            });
    }
}
