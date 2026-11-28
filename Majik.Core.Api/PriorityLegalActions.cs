using Majik.Core.Abilities;
using Majik.Core.Api.Dtos;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Mana;
using Majik.Core.Players;
using Majik.Core.Rules;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Api;

/// <summary>
/// Computes the specific legal priority actions for <see cref="GameContext.Self"/>
/// — shipped on the human seat's priority prompt as
/// <see cref="PromptDto.LegalActions"/>. Where <see cref="PriorityKinds"/> is the
/// coarse, permissive list of command TYPES, this names the exact lands, cards
/// and abilities, built on the same gates the live engine applies when the
/// command is dispatched (<see cref="Majik.Core.Game.TurnDriver"/>):
/// <list type="bullet">
/// <item><b>Lands</b> — CR 305.1 / 305.2: own main phase, empty stack, land drop
/// available (<see cref="GameContext.LandPlayAvailable"/>, computed from the
/// engine's <see cref="LandDropTracker"/>). Hand lands plus lands an
/// "you may play it from exile" permission covers.</item>
/// <item><b>Casts</b> — non-land cards in hand. Timing and registered casting
/// restrictions via <see cref="ActionValidator"/> (CR 117.1 / 307.1 / 601.3: a
/// non-instant, non-Flash card needs a sorcery window). A card with no mana cost
/// can't be cast (CR 118.6). The effective cost
/// (<see cref="CostReduction.GetEffectiveCost(ICard, Player, IEnumerable{Player}?)"/>,
/// X = 0) must be payable from the floating pool plus untapped mana sources
/// (CR 601.2g-h) — the same pool-then-sources path DispatchCast uses.</item>
/// <item><b>Activated</b> — non-mana <see cref="IActivatedAbility"/>s of
/// permanents Self controls: Pithing-Needle / activate-only-if / sorcery-speed
/// gates via <see cref="ActionValidator"/> (CR 602.5c / 307.5), the context-aware
/// <see cref="ActivatedAbility.CanActivateNow(GameContext?)"/>, and
/// <see cref="CostPayment.CanPayCosts(Player, IEnumerable{ICost}, ManaSpendContext)"/>
/// — exactly DispatchActivate's gate. That gate pays mana from the FLOATING POOL
/// ONLY, so an ability with a mana cost is listed only once enough mana is
/// floating ({T} costs honour CR 302.6 summoning sickness).</item>
/// <item><b>Loyalty</b> — CR 606.3: sorcery window, once per turn, loyalty cost
/// payable (<see cref="LoyaltyAbility.CanActivate"/>).</item>
/// <item><b>Mana</b> — CR 605.3a: one entry per permanent Self controls with at
/// least one <see cref="IManaAbility"/> whose <c>CanActivate()</c> holds
/// (untapped, not summoning-sick for {T} creature sources).
/// <see cref="AbilityRefDto.AbilityId"/> is <see cref="Guid.Empty"/>; the
/// client sends <c>ActivateManaAbilityCommand(permanent, colour)</c>.</item>
/// </list>
/// Pure and side-effect free: nothing is tapped, paid, moved or published; no
/// search is run. Known approximations are documented on the helpers below.
/// <para>Fault-contained: every card / ability check runs card-supplied code
/// (gates, costs, reductions, mana abilities). One that throws is left out of
/// the list and reported via <see cref="GameFacade.OnEngineDiagnostic"/>; the
/// rest of the list is still built.</para>
/// </summary>
public static class PriorityLegalActions
{
    private static readonly ActionValidator Validator = new();

    /// <summary>Kind tag for <see cref="AbilityRefDto.Kind"/>: CR 605 mana ability.</summary>
    public const string ManaKind = "Mana";
    /// <summary>Kind tag for <see cref="AbilityRefDto.Kind"/>: CR 602 activated ability.</summary>
    public const string ActivatedKind = "Activated";
    /// <summary>Kind tag for <see cref="AbilityRefDto.Kind"/>: CR 606 loyalty ability.</summary>
    public const string LoyaltyKind = "Loyalty";

    /// <summary>
    /// Build the legal actions for <c>ctx.Self</c> at this priority window.
    /// <paramref name="layers"/> lets mana-source discovery see layer-granted /
    /// retyped mana abilities (CR 305.6 Blood Moon) exactly as auto-pay does.
    /// </summary>
    public static LegalActionsDto Build(GameContext ctx, ContinuousEffectsService? layers = null)
    {
        ArgumentNullException.ThrowIfNull(ctx);
        var self = ctx.Self;
        if (self.HasLost)
        {
            return new LegalActionsDto(Array.Empty<Guid>(), Array.Empty<Guid>(), Array.Empty<AbilityRefDto>());
        }

        // CR 307.1 / 117.1a — sorcery-speed window: own main phase, empty stack.
        var sorceryWindow = ctx.CurrentPhase is { } phase
            && phase.IsMain()
            && ReferenceEquals(ctx.ActivePlayer, self)
            && ctx.Stack.Count == 0;

        var lands = new List<Guid>();
        var casts = new List<Guid>();
        var abilities = new List<AbilityRefDto>();

        // CR 305.1 / 305.2 — land plays.
        if (sorceryWindow && ctx.LandPlayAvailable)
        {
            foreach (var card in self.Zones.Hand.GetCards())
            {
                if (Guard(card, "land play", () => card.HasType(CardType.Land))) lands.Add(card.InstanceId);
            }
            var fromExile = Guard<IReadOnlyList<ICard>>(self, "lands playable from exile",
                () => Majik.Core.Keywords.ExilePlayPermission.PlayableLandsFromExile(self).ToList(),
                fallback: Array.Empty<ICard>());
            foreach (var card in fromExile)
            {
                if (!lands.Contains(card.InstanceId)) lands.Add(card.InstanceId);
            }
        }

        // CR 601.2 — casts from hand.
        var sources = ManaSources(self, layers);
        foreach (var card in self.Zones.Hand.GetCards())
        {
            if (Guard(card, "cast", () => !card.HasType(CardType.Land) && IsCastable(card, self, ctx, sorceryWindow, sources)))
            {
                casts.Add(card.InstanceId);
            }
        }

        // CR 602 / 605 / 606 — abilities of permanents Self controls (CR 602.2:
        // only an object's controller can activate its abilities).
        foreach (var player in ctx.AllPlayers)
        {
            foreach (var card in player.Zones.Battlefield.GetCards())
            {
                if (card is not Permanent permanent) continue;
                var controller = permanent.Controller ?? permanent.Owner;
                if (!ReferenceEquals(controller, self)) continue;
                var list = Guard<IReadOnlyList<IAbility>>(permanent, "abilities", () => permanent.Abilities.ToList(), fallback: Array.Empty<IAbility>());
                CollectAbilities(permanent, list, self, ctx, sorceryWindow, abilities);
            }
        }

        return new LegalActionsDto(lands, casts, abilities);
    }

    /// <summary>Run one card-supplied check; a throw counts as "not legal" for
    /// that item only, and is reported (not swallowed, not fail-fast).</summary>
    private static T Guard<T>(object subject, string check, Func<T> evaluate, T fallback = default!)
    {
        try
        {
            return evaluate();
        }
        catch (Exception ex)
        {
            GameFacade.ReportContainedFault(ex,
                $"PriorityLegalActions: {check} check for '{Describe(subject)}' threw; left out of the legal-action hint");
            return fallback;
        }
    }

    private static string Describe(object subject) => subject switch
    {
        ICard card => card.Name,
        Player player => player.Name,
        _ => subject.GetType().Name,
    };

    private static void CollectAbilities(
        Permanent permanent, IEnumerable<IAbility> abilities, Player self, GameContext ctx, bool sorceryWindow,
        List<AbilityRefDto> into)
    {
        var manaListed = false;
        foreach (var ability in abilities)
        {
            // An ability is added only after all of its checks passed, so a
            // throw mid-check leaves nothing half-listed.
            Guard(permanent, "ability", () => { CollectAbility(ability); return true; }, fallback: false);
        }

        void CollectAbility(IAbility ability)
        {
            switch (ability)
            {
                case IManaAbility mana:
                    // CR 605.3a — mana abilities can be activated whenever the
                    // player has priority; ManaAbilityActivator requires the
                    // controller and CanActivate (untapped / CR 302.6).
                    if (!manaListed && ReferenceEquals(mana.Controller, self) && SafeCanActivate(mana))
                    {
                        into.Add(new AbilityRefDto(permanent.InstanceId, Guid.Empty, ManaKind));
                        manaListed = true;
                    }
                    break;
                case LoyaltyAbility loyalty:
                    // CR 606.3 — sorcery speed, once per turn, payable loyalty.
                    if (sorceryWindow && loyalty.CanActivate())
                    {
                        into.Add(new AbilityRefDto(permanent.InstanceId, loyalty.Id, LoyaltyKind));
                    }
                    break;
                case IActivatedAbility activated:
                    if (IsActivatable(activated, permanent, self, ctx, sorceryWindow))
                    {
                        into.Add(new AbilityRefDto(permanent.InstanceId, activated.Id, ActivatedKind));
                    }
                    break;
            }
        }
    }

    private static bool SafeCanActivate(IManaAbility mana)
    {
        try { return mana.CanActivate(); }
        catch (InvalidOperationException) { return false; }
    }

    private static bool IsActivatable(
        IActivatedAbility ability, Permanent source, Player self, GameContext ctx, bool sorceryWindow)
    {
        // CR 602.5c / 307.5 — suppression, activate-only-if, sorcery-speed rider.
        if (!Validator.ValidateAction(new ActivateAbilityAction(ability, self, sorceryWindow)).IsValid)
        {
            return false;
        }
        // CR 602.5c — the context-aware condition the live activator consults.
        if (ability is ActivatedAbility concrete && !concrete.CanActivateNow(ctx))
        {
            return false;
        }
        // CR 602.2b / 601.2h — DispatchActivate's own payability gate (floating
        // pool for mana; {T} via AdditionalCost → CR 302.6 summoning sickness).
        return new CostPayment().CanPayCosts(self, ability.Costs, ManaSpendContext.ForAbilityCost(source));
    }

    private static bool IsCastable(
        ICard card, Player self, GameContext ctx, bool sorceryWindow, IReadOnlyList<ManaSource> sources)
    {
        // CR 118.6 — an object with no mana cost can't be cast via its mana
        // cost (alternative costs are not offered here).
        if (card is Card concrete && string.IsNullOrWhiteSpace(concrete.ManaCost)) return false;

        // CR 117.1 / 307.1 / 601.3 — timing + registered casting restrictions.
        var action = new CastSpellAction(
            card, self, sorceryWindow, ZoneType.Hand, targets: null, targetSpec: null, currentStep: ctx.CurrentPhase);
        if (!Validator.ValidateAction(action).IsValid) return false;

        // CR 601.2f — effective cost after reductions / increases; X chosen as 0.
        ManaCost cost;
        try
        {
            cost = CostReduction.GetEffectiveCost(card, self, ctx.AllPlayers);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        return CanAfford(self, cost, sources);
    }

    /// <summary>One untapped permanent's mana capability (each source taps once).</summary>
    private sealed record ManaSource(IReadOnlyList<ManaCost> Outputs)
    {
        public int MaxTotal => Outputs.Count == 0 ? 0 : Outputs.Max(o => o.TotalValue);

        public int Yield(ManaColor color) => Outputs.Count == 0 ? 0 : Outputs.Max(o => Amount(o, color));

        /// <summary>Identity of the capability, so interchangeable sources (ten
        /// Mountains) are tried once per search level.</summary>
        public string Signature { get; } = string.Join("|", Outputs.Select(o => o.ToString()).OrderBy(s => s, StringComparer.Ordinal));
    }

    private static IReadOnlyList<ManaSource> ManaSources(Player self, ContinuousEffectsService? layers)
    {
        // Same candidate set ManaPaymentResolver.TryAutoSelectSources uses:
        // untapped permanents with an activatable (layer-aware) mana ability.
        var list = new List<ManaSource>();
        foreach (var card in self.Zones.Battlefield.GetCards())
        {
            if (card is not Permanent perm || perm.IsTapped) continue;
            var outputs = Guard<IReadOnlyList<ManaCost>>(perm, "mana source", () => EffectiveManaAbilities.For(perm, layers)
                .Where(SafeCanActivate)
                .Select(a => a.ManaGenerated)
                .ToList(), fallback: Array.Empty<ManaCost>());
            if (outputs.Count > 0) list.Add(new ManaSource(outputs));
        }
        return list;
    }

    /// <summary>
    /// Total mana <paramref name="player"/> could produce right now, colour-blind:
    /// floating pool plus one activation (largest output) of every untapped mana
    /// source — the same candidate set auto-pay uses. Used for the X hint.
    /// </summary>
    internal static int PotentialMana(Player player, ContinuousEffectsService? layers) =>
        player.ManaPool.Total + ManaSources(player, layers).Sum(s => s.MaxTotal);

    private static readonly ManaColor[] Colors =
        { ManaColor.White, ManaColor.Blue, ManaColor.Black, ManaColor.Red, ManaColor.Green };

    private static int Amount(ManaCost m, ManaColor color) => color switch
    {
        ManaColor.White => m.White,
        ManaColor.Blue => m.Blue,
        ManaColor.Black => m.Black,
        ManaColor.Red => m.Red,
        ManaColor.Green => m.Green,
        _ => 0,
    };

    /// <summary>
    /// CR 601.2g-h — can <paramref name="cost"/> be paid from the floating pool
    /// plus the untapped <paramref name="sources"/> (each activated at most once)?
    /// Coloured pips are matched exactly (small backtracking assignment, so a
    /// dual land is not "spent" on the wrong colour the way the greedy auto-pay
    /// can be); generic takes any leftover output (CR 106.1c / 107.4b).
    /// Approximations: hybrid / Phyrexian costs get a colour-blind total check
    /// with Phyrexian pips payable by 2 life (CR 107.4f); {C} pips are treated
    /// as generic; spend restrictions on floating mana and the "spend as any
    /// colour" permission are ignored; sources whose abilities have extra
    /// costs (filter lands, painlands' life) count as their printed output.
    /// </summary>
    private static bool CanAfford(Player self, ManaCost cost, IReadOnlyList<ManaSource> sources)
    {
        var pool = self.ManaPool;
        if (pool.CanPay(cost)) return true;
        if (pool.Total + sources.Sum(s => s.MaxTotal) < cost.TotalValue - cost.PhyrexianPips.Count) return false;

        if (cost.HybridPips.Count > 0 || cost.PhyrexianPips.Count > 0)
        {
            var units = cost.Generic + cost.White + cost.Blue + cost.Black + cost.Red + cost.Green
                + cost.HybridPips.Count + cost.PhyrexianPips.Count;
            var lifePayable = Math.Min(cost.PhyrexianPips.Count, Math.Max(0, self.LifeTotal) / 2);
            return pool.Total + sources.Sum(s => s.MaxTotal) >= units - lifePayable;
        }

        // Coloured shortfall after the floating pool absorbs what it can.
        var need = new int[Colors.Length];
        var poolUsedForColored = 0;
        for (var i = 0; i < Colors.Length; i++)
        {
            var required = Amount(cost, Colors[i]);
            var floating = PoolAmount(pool, Colors[i]);
            need[i] = Math.Max(0, required - floating);
            poolUsedForColored += Math.Min(floating, required);
        }
        var genericNeeded = Math.Max(0, cost.Generic - (pool.Total - poolUsedForColored));

        var used = new bool[sources.Count];
        return AssignColored(sources, need, 0, used, genericNeeded);
    }

    private static int PoolAmount(ManaPool pool, ManaColor color) => color switch
    {
        ManaColor.White => pool.White,
        ManaColor.Blue => pool.Blue,
        ManaColor.Black => pool.Black,
        ManaColor.Red => pool.Red,
        ManaColor.Green => pool.Green,
        _ => 0,
    };

    // Backtracking over colours: pick an unused source for the next unmet
    // colour, credit its yield of that colour, recurse. Sources tap once, so the
    // search space is bounded by (sources × coloured pips) — tiny in practice.
    private static bool AssignColored(
        IReadOnlyList<ManaSource> sources, int[] need, int surplus, bool[] used, int genericNeeded)
    {
        var colorIndex = Array.FindIndex(need, n => n > 0);
        if (colorIndex < 0)
        {
            var spare = 0;
            for (var i = 0; i < sources.Count; i++)
            {
                if (!used[i]) spare += sources[i].MaxTotal;
            }
            return surplus + spare >= genericNeeded;
        }

        var color = Colors[colorIndex];
        HashSet<string>? tried = null;
        for (var i = 0; i < sources.Count; i++)
        {
            if (used[i]) continue;
            var yield = sources[i].Yield(color);
            if (yield <= 0) continue;
            if (!(tried ??= new HashSet<string>(StringComparer.Ordinal)).Add(sources[i].Signature)) continue;
            var applied = Math.Min(yield, need[colorIndex]);
            used[i] = true;
            need[colorIndex] -= applied;
            var extra = Math.Max(0, sources[i].MaxTotal - applied);
            if (AssignColored(sources, need, surplus + extra, used, genericNeeded)) return true;
            need[colorIndex] += applied;
            used[i] = false;
        }
        return false;
    }
}
