using Majik.Core.Abilities;
using Majik.Core.CardData.Adventures;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;

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
/// - <b>Adventure cast pipeline (CR 715)</b>: the Battle Display half is
///   attached as an <see cref="AdventureSpec"/> on the card. The cast flow
///   (<see cref="Costs.AdventureAlternativeCost"/> + <see cref="SpellCastFlow"/>)
///   routes Battle Display through the standard Rule 601 sequence with
///   the Adventure mana cost (sorcery-speed gated — Battle Display is a
///   Sorcery), exiles the card on resolve (CR 715.3d), and grants the
///   owner a runtime "may cast from exile" permission for the printed
///   Embereth Shieldbreaker cost via
///   <see cref="Card.GrantRuntimeExileCast"/>.
///
/// ## Deferred (v1 gaps)
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

        // CR 715 — Battle Display Adventure attached for the cast pipeline.
        card.AdventureSpec = new AdventureSpec(
            Name: AdventureName,
            ManaCost: ManaCost.Parse(AdventureManaCost),
            AdventureType: CardType.Sorcery,
            BuildDefinition: BuildAdventureSpell);

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
