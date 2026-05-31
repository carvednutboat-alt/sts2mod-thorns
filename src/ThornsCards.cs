using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History.Entries;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace ThornsMod;

public sealed class ThornsCardPool : CustomCardPoolModel
{
    public override string Title => "thorns";

    public override string EnergyColorName => "silent";

    public override Color DeckEntryCardColor => new Color("B7D179");

    public override Color ShaderColor => new Color("B7D179");

    public override bool IsColorless => false;
}

internal static class ThornsPortraits
{
    public static readonly string AlchemyUnit = ImageHelper.GetImagePath("packed/thorns_icons/alchemy_unit_icon.png");
    public static readonly string AlchemyRelease = ImageHelper.GetImagePath("packed/thorns_icons/alchemy_release_icon.png");
    public static readonly string NeuralDamage = ImageHelper.GetImagePath("packed/thorns_icons/neural_damage_icon.png");
    public static readonly string MySea = ImageHelper.GetImagePath("packed/thorns_icons/my_sea_icon.png");
    public static readonly string Catalyst = ImageHelper.GetImagePath("packed/thorns_icons/catalyst_icon.png");
    public static readonly string Paralysis = ImageHelper.GetImagePath("packed/thorns_icons/paralysis_icon.png");
}

internal static class ThornsAlchemy
{
    public static List<Creature> Units(CombatState? combatState)
    {
        return combatState?.Enemies.Where(e => e.IsAlive && e.HasPower<AlchemyUnitPower>()).ToList() ?? new List<Creature>();
    }

    public static bool HasUnit(CombatState? combatState) => Units(combatState).Any();

    public static List<Creature> NormalEnemies(CombatState? combatState)
    {
        return combatState?.HittableEnemies
            .Where(e => e.IsAlive && !e.IsPlayer && !e.HasPower<AlchemyUnitPower>())
            .ToList() ?? new List<Creature>();
    }

    public static List<Creature> HittableEnemies(CombatState? combatState, bool includeAlchemyUnits = false)
    {
        return combatState?.HittableEnemies
            .Where(e => e.IsAlive && !e.IsPlayer && (includeAlchemyUnits || !e.HasPower<AlchemyUnitPower>()))
            .ToList() ?? new List<Creature>();
    }

    public static bool PlayedCardThisTurn(Player owner, CardType type)
    {
        return CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>()
            .Any(e => e.CardPlay.Card.Owner == owner && e.CardPlay.Card.Type == type && e.HappenedThisTurn(owner.Creature.CombatState));
    }

    public static bool PlayedAttackThisTurn(Player owner) => PlayedCardThisTurn(owner, CardType.Attack);

    public static bool PlayedSkillThisTurn(Player owner) => PlayedCardThisTurn(owner, CardType.Skill);

    public static bool HasCatalyst(Creature creature) => creature.GetPower<CatalystPower>()?.Amount > 0;

    private static bool IsAmplifiable(PowerModel p)
    {
        if (p.Amount <= 0) return false;
        if (p.StackType != PowerStackType.Counter) return false;
        // Exclude binary/one-shot effects and catalyst itself
        if (p is CatalystPower || p is BufferPower || p is IntangiblePower || p is ArtifactPower) return false;
        // Exclude mod-internal invisible tracking powers
        if (p is AlchemyUnitPower || p is MinionPower || p is AlchemyPulseThisTurnPower
            || p is AlchemyReleasedThisTurnPower || p is AlchemySummonedThisCombatPower
            || p is FirstUnitBreakRewardSpentPower || p is AncientAlchemyDrawsThisTurnPower
            || p is ConstellationLegacyBonusSpentPower || p is NeuralDamageCounterPower
            || p is NeuralShockPower) return false;
        return true;
    }

    // When applying catalyst to a target, if the target has any amplifiable effect, consume 1 catalyst to boost it
    public static async Task ApplyCatalyst(Creature target, decimal amount, Creature applier, CardModel? source)
    {
        await PowerCmd.Apply<CatalystPower>(target, amount, applier, source);
        if (amount > 0)
        {
            await TryAmplifyOne(target, applier, source);
        }
    }

    // When applying ANY stackable power to a catalyzed target, consume 1 catalyst to boost it
    public static async Task TryCatalystBoost(Creature target, PowerModel appliedPower, decimal applyAmount, Creature applier, CardModel? source)
    {
        if (!IsAmplifiable(appliedPower)) return;
        if (!HasCatalyst(target)) return;
        if (applyAmount <= 0) return;

        CatalystPower? cat = target.GetPower<CatalystPower>();
        if (cat == null || cat.Amount <= 0) return;

        await PowerCmd.ModifyAmount(appliedPower, 1m, applier, source);
        await PowerCmd.ModifyAmount(cat, -1m, applier, source);
    }

    // Amplify one random amplifiable power on the target, consuming 1 catalyst
    public static async Task TryAmplifyOne(Creature target, Creature applier, CardModel? source)
    {
        CatalystPower? cat = target.GetPower<CatalystPower>();
        if (cat == null || cat.Amount <= 0) return;

        var amplifiable = target.Powers.Where(p => IsAmplifiable(p)).ToList();
        foreach (PowerModel power in amplifiable)
        {
            cat = target.GetPower<CatalystPower>();
            if (cat == null || cat.Amount <= 0) break;
            await PowerCmd.ModifyAmount(power, 1m, applier, source);
            await PowerCmd.ModifyAmount(cat, -1m, applier, source);
        }
    }

    public static async Task ClearCatalyst(Creature target, decimal amount, Creature applier, CardModel? source)
    {
        CatalystPower? catalyst = target.GetPower<CatalystPower>();
        if (catalyst == null)
        {
            return;
        }

        await PowerCmd.ModifyAmount(catalyst, -Math.Min(amount, catalyst.Amount), applier, source);
    }

    public static async Task<Creature> SummonUnit(PlayerChoiceContext choiceContext, Player owner, CardModel? source)
    {
        CombatState? combatState = owner.Creature.CombatState;
        if (Units(combatState).Count >= UnitLimit(owner.Creature))
        {
            Creature existing = Units(combatState).First();
            await Pulse(choiceContext, combatState, owner.Creature, source);
            return existing;
        }

        Creature unit = await CreatureCmd.Add<OneHpMonster>(owner.Creature.CombatState);
        await PowerCmd.Apply<MinionPower>(unit, 1m, owner.Creature, source);
        await PowerCmd.Apply<AlchemyUnitPower>(unit, 1m, owner.Creature, source);
        await PowerCmd.Apply<AlchemySummonedThisCombatPower>(owner.Creature, 1m, owner.Creature, source, silent: true);

        foreach (AncientAlchemyPower power in owner.Creature.Powers.OfType<AncientAlchemyPower>())
            await power.AfterUnitSummoned(choiceContext, source);
        foreach (DualCorePower power in owner.Creature.Powers.OfType<DualCorePower>())
            await power.AfterUnitSummoned(choiceContext);
        return unit;
    }

    public static async Task Pulse(PlayerChoiceContext choiceContext, CombatState? combatState, Creature applier, CardModel? source)
    {
        if (combatState == null)
        {
            return;
        }

        int repeat = applier.GetPower<StarConstellationPower>() != null ? 2 : 1;
        for (int i = 0; i < repeat; i++)
        {
            await PulseOnce(choiceContext, combatState, applier, source);
        }
    }

    private static async Task PulseOnce(PlayerChoiceContext choiceContext, CombatState combatState, Creature applier, CardModel? source)
    {
        await PowerCmd.Apply<AlchemyPulseThisTurnPower>(applier, 1m, applier, source, silent: true);
        await PowerCmd.Apply<TotalPulseCountPower>(applier, 1m, applier, source, silent: true);

        foreach (Creature enemy in NormalEnemies(combatState))
        {
            decimal pAmt = applier.GetPower<StarConstellationPower>() != null ? 4m : 1m;
            await PowerCmd.Apply<PoisonPower>(enemy, pAmt, applier, source);
        }

        // Apply catalyst to ALL alive creatures + all players (multiplayer fix, no double-apply)
        var catalyzed = new HashSet<Creature>();
        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive).ToList())
        {
            catalyzed.Add(creature);
            await ApplyCatalyst(creature, 1m, applier, source);
        }
        foreach (Player player in combatState.Players)
        {
            if (player.Creature.IsAlive && catalyzed.Add(player.Creature))
            {
                await ApplyCatalyst(player.Creature, 1m, applier, source);
            }
        }

        AlchemyPulseHealPower? healPower = applier.GetPower<AlchemyPulseHealPower>();
        if (healPower != null && applier.IsAlive)
            await CreatureCmd.Heal(applier, healPower.Amount);
        // SwarmTide: deal 8 damage to all enemies on pulse
        if (applier.GetPower<SwarmTidePower>() != null)
            foreach (var e in NormalEnemies(combatState))
                await DamageCmd.Attack(8m).FromCard(null).Targeting(e).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
    }

    public static async Task Release(PlayerChoiceContext choiceContext, Creature unit, CardModel? source, int multiplier = 1)
    {
        CombatState combatState = unit.CombatState;
        Creature applier = unit.GetPower<AlchemyUnitPower>()?.Applier ?? combatState.Players.First().Creature;
        if (applier.GetPower<AbyssalFormPower>() != null)
        {
            multiplier *= 2;
        }

        int poison = 5 * multiplier;
        int catalyst = 2 * multiplier;

        foreach (Creature enemy in NormalEnemies(combatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, poison, applier, source);
        }

        // Apply catalyst to ALL alive creatures + all players (multiplayer fix, no double-apply)
        var catalyzed = new HashSet<Creature>();
        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive).ToList())
        {
            catalyzed.Add(creature);
            await ApplyCatalyst(creature, catalyst, applier, source);
        }
        foreach (Player p in combatState.Players)
        {
            if (p.Creature.IsAlive && catalyzed.Add(p.Creature))
            {
                await ApplyCatalyst(p.Creature, catalyst, applier, source);
            }
        }

        Player? player = applier.Player ?? combatState.Players.FirstOrDefault();
        if (player != null)
        {
            await CardPileCmd.Draw(choiceContext, multiplier, player);
        }

        await PowerCmd.Apply<AlchemyReleasedThisTurnPower>(applier, 1m, applier, source, silent: true);
        // SwarmTide: deal 8 damage to all enemies on release
        if (applier.GetPower<SwarmTidePower>() != null)
            foreach (var e in NormalEnemies(combatState))
                await DamageCmd.Attack(8m).FromCard(null).Targeting(e).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
        foreach (FirstUnitBreakRewardPower power in applier.Powers.OfType<FirstUnitBreakRewardPower>())
        {
            await power.AfterUnitReleased(choiceContext, source);
        }

        foreach (ConstellationLegacyPower power in applier.Powers.OfType<ConstellationLegacyPower>())
        {
            await power.AfterUnitReleased(choiceContext, source);
        }
    }

    public static int UnitLimit(Creature owner)
    {
        return 1 + (owner.GetPower<ConstellationLegacyPower>()?.Amount ?? 0);
    }

    public static async Task ApplyWithCatalystBoost<T>(Creature target, decimal amount, Creature applier, CardModel? source) where T : PowerModel
    {
        await PowerCmd.Apply<T>(target, amount, applier, source);
        // After applying, check if target has catalyst — if so, boost the applied power
        PowerModel? applied = target.GetPower<T>();
        if (applied != null)
        {
            await TryCatalystBoost(target, applied, amount, applier, source);
        }
    }

}



// ============================================================
// CUSTOM POWERS for Thorns
// ============================================================

// Thorns (荆棘) and Regen (再生) are base-game powers in STS2 — no custom implementation needed.

// Run-level counter for neural shock triggers (persists across combats)
internal static class NeuralShockRunStats
{
    public static int Count;
    public static void Increment() => Count++;
    public static void Reset() => Count = 0;
}

public sealed class TotalPulseCountPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

// 摧残 (Debilitate): modifies damage multipliers — doubles Vulnerable extra damage and Weak damage reduction
public sealed class DebilitatePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public decimal ModifyVulnerableMultiplier(Creature target, decimal amount, ValueProp props, Creature dealer, CardModel cardSource)
    {
        if (props.IsPoweredAttack() && target == Owner && Amount > 0)
            return amount + (amount - 1m);
        return amount;
    }

    public decimal ModifyWeakMultiplier(Creature target, decimal amount, ValueProp props, Creature dealer, CardModel cardSource)
    {
        if (props.IsPoweredAttack() && dealer == Owner && Amount > 0)
            return amount - (1m - amount);
        return amount;
    }

    public string? CustomPowerIconPath => ImageHelper.GetImagePath("atlases/power_atlas.sprites/debilitatepower.tres");
    public string? CustomPowerBigIconPath => ImageHelper.GetImagePath("powers/debilitatepower.png");
    public override List<(string, string)> Localization => new() { ("title", "摧残"), ("description", "易伤额外受伤翻倍，虚弱减伤翻倍。") };
}


// 摧残 (Debilitate): doubles existing Vulnerable and Weak. Applied directly in card OnPlay.

public sealed class ThornsTempStrengthDownPower : TemporaryStrengthPower
{
    public override AbstractModel OriginModel => ModelDb.Card<SeaKingProtection>();
    protected override bool IsPositive => false;
}

public sealed class PoisonMasteryPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (Owner.IsDead || target.IsPlayer || target.Side == Owner.Side || result.UnblockedDamage <= 0 || props.IsPoweredAttack())
        {
            return;
        }

        PoisonPower? poison = target.GetPower<PoisonPower>();
        if (poison == null)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<NeuralDamageCounterPower>(target, result.UnblockedDamage, Owner, null);
        NeuralDamageCounterPower? counter = target.GetPower<NeuralDamageCounterPower>();
        if (counter != null && counter.Amount >= Amount)
        {
            await PowerCmd.Remove(counter);
            await PowerCmd.Apply<NeuralShockPower>(target, 1m, Owner, null);
            NeuralShockRunStats.Increment();
            foreach (PoisonReaperPower reaper in Owner.Powers.OfType<PoisonReaperPower>())
            {
                await reaper.AfterNeuralShock(choiceContext, target);
            }
            foreach (NeuroTidePower tide in Owner.Powers.OfType<NeuroTidePower>())
            {
                await tide.AfterNeuralShock(choiceContext);
            }
            foreach (NeuroSpreadPower spread in Owner.Powers.OfType<NeuroSpreadPower>())
            {
                await spread.AfterNeuralShock(choiceContext, target);
            }
            foreach (PlagueSpreadPower plague in Owner.Powers.OfType<PlagueSpreadPower>())
            {
                await plague.AfterNeuralShock(choiceContext, target);
            }
            foreach (PainDeprivePower pain in Owner.Powers.OfType<PainDeprivePower>())
            {
                await pain.AfterNeuralShock(choiceContext, target);
            }
            foreach (DualCorePower dc in Owner.Powers.OfType<DualCorePower>())
            {
                await dc.AfterNeuralShock(choiceContext);
            }
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤"),
        ("description", "Enemies accumulate Neural Damage when they lose HP from Poison. At the threshold, reset it and make their next attack deal 0 damage.")
    };
}

public sealed class NeuralDamageCounterPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤条"),
        ("description", "At 12, resets and applies Neural Shock.")
    };
}

public sealed class NeuralShockPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer == Owner && props.IsPoweredAttack())
        {
            return 0m;
        }

        return 1m;
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer == Owner && props.IsPoweredAttack())
        {
            Flash();
            await PowerCmd.Remove(this);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经震慑"),
        ("description", "This creature's next attack deals 0 damage.")
    };
}

public sealed class PoisonReaperPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public async Task AfterNeuralShock(PlayerChoiceContext choiceContext, Creature target)
    {
        if (Owner.IsDead || !target.IsAlive)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<PoisonPower>(target, 6m * Amount, Owner, null);
        if (Owner.CombatState != null)
        {
            await ThornsAlchemy.Pulse(choiceContext, Owner.CombatState, Owner, null);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤爆发"),
        ("description", "Whenever Neural Damage applies Neural Shock, apply 6 Poison and trigger an Alchemical Unit pulse.")
    };
}

public sealed class AlchemyUnitPower : CustomPowerModel
{
    private class Data
    {
        public bool released;
    }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override bool ShouldPlayVfx => false;
    public override bool OwnerIsSecondaryEnemy => true;

    protected override object InitInternalData() => new Data();

    public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
    {
        if (side == CombatSide.Player && Owner.IsAlive)
        {
            Flash();
            await ThornsAlchemy.Pulse(new ThrowingPlayerChoiceContext(), combatState, Applier ?? combatState.Players.First().Creature, null);
        }
    }

    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (data.released || target != Owner || amount <= 0 || !Owner.IsAlive) return;

        // Trigger Release BEFORE the unit dies from lethal damage
        if (amount >= Owner.CurrentHp)
        {
            data.released = true;
            Flash();
            await ThornsAlchemy.Release(choiceContext, Owner, cardSource);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "炼金单元"),
        ("description", "A 1 HP secondary enemy. At the start of your turn, pulses. When destroyed, releases a stronger effect.")
    };
}

public sealed class VesselOfPoisonPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("PoisonAmount", 1m)
    };

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (Owner.IsDead || dealer != Owner || target.IsPlayer || result.UnblockedDamage <= 0 || !props.IsPoweredAttack() || !target.HasPower<PoisonPower>())
            return;

        Flash();
        await PowerCmd.Apply<PoisonPower>(target, DynamicVars["PoisonAmount"].BaseValue, Owner, null);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "毒剂容器"),
        ("description", "攻击对中毒敌人额外施加1层中毒。")
    };
}

public sealed class AlchemyPulseThisTurnPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class AlchemyReleasedThisTurnPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove(this);
        }
    }
}

public sealed class AlchemySummonedThisCombatPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

public sealed class FirstUnitBreakRewardPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public async Task AfterUnitReleased(PlayerChoiceContext choiceContext, CardModel? source)
    {
        if (Owner.HasPower<FirstUnitBreakRewardSpentPower>())
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<FirstUnitBreakRewardSpentPower>(Owner, 1m, Owner, source, silent: true);
        await PowerCmd.Apply<StrengthPower>(Owner, Amount, Owner, source);
        await PowerCmd.Apply<DexterityPower>(Owner, Amount, Owner, source);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "疏通航道"),
        ("description", "每回合炼金单元首次释放时获得力量和敏捷。")
    };
}

public sealed class FirstUnitBreakRewardSpentPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

public sealed class ThornsBodyPower : CustomPowerModel
{
    private class Data { public bool triggeredThisTurn; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override object InitInternalData() => new Data();

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (target != Owner || dealer == null || dealer.IsPlayer || result.UnblockedDamage <= 0 || data.triggeredThisTurn)
        {
            return;
        }

        data.triggeredThisTurn = true;
        Flash();
        await CreatureCmd.Damage(choiceContext, new[] { dealer }, 5m * Amount, ValueProp.Unpowered, Owner, null);
        await PowerCmd.Apply<PoisonPower>(dealer, 2m * Amount, Owner, null);
    }

    public override Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            GetInternalData<Data>().triggeredThisTurn = false;
        }

        return Task.CompletedTask;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身尖刺"),
        ("description", "The first time each turn you are attacked, retaliate and apply Poison.")
    };
}

public sealed class AlchemyPulseHealPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "再生气雾"),
        ("description", "炼金单元脉冲时为你回复生命。")
    };
}

public sealed class StarConstellationPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "星图"),
        ("description", "Alchemical Unit pulses trigger twice and apply Catalyst to enemies.")
    };
}

public sealed class StarBlessingPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer == Owner && props.IsPoweredAttack() && ThornsAlchemy.HasUnit(Owner.CombatState))
        {
            return Amount;
        }

        return 0m;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "引星祝福"),
        ("description", "While an Alchemical Unit exists, your attacks deal additional damage.")
    };
}

public sealed class AncientAlchemyPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public async Task AfterUnitSummoned(PlayerChoiceContext choiceContext, CardModel? source)
    {
        if (Owner.Player == null || Owner.GetPower<AncientAlchemyDrawsThisTurnPower>()?.Amount >= 2)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<AncientAlchemyDrawsThisTurnPower>(Owner, 1m, Owner, source, silent: true);
        await CardPileCmd.Draw(choiceContext, Amount, Owner.Player);
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            await PowerCmd.Remove<AncientAlchemyDrawsThisTurnPower>(Owner);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "炼金术"),
        ("description", "每当你召唤炼金单元时抽1张牌，每回合限2次。")
    };
}

public sealed class AncientAlchemyDrawsThisTurnPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

public sealed class ConstellationLegacyPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public async Task AfterUnitReleased(PlayerChoiceContext choiceContext, CardModel? source)
    {
        Flash();
        await CreatureCmd.GainBlock(Owner, 4m * Amount, ValueProp.Unpowered, null);
    }

    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner.Player || Owner.HasPower<ConstellationLegacyBonusSpentPower>() || !IsAlchemyCard(cardPlay.Card))
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<ConstellationLegacyBonusSpentPower>(Owner, 1m, Owner, cardPlay.Card, silent: true);
        if (Owner.Player != null)
        {
            await ThornsAlchemy.SummonUnit(context, Owner.Player, cardPlay.Card);
        }
    }

    private static bool IsAlchemyCard(CardModel card)
    {
        return card is AlchemicalMixture or LodestarGuidance or DeepSeaRegeneration or StarCataclysm or PrecisionMix or VoyageEnd;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "宝宝摇篮号"),
        ("description", "炼金单元上限+1。首次打出炼金牌额外召唤1个单元。单元释放时获得格挡。")
    };
}

public sealed class ConstellationLegacyBonusSpentPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

public sealed class GuidingStarPower : CustomPowerModel
{
    private class Data { public int attacksThisCombat; public bool firstAttackThisTurnDone; public bool strengthGranted; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();

    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
    {
        if (cardPlay.Card.Owner != Owner.Player || cardPlay.Card.Type != CardType.Attack)
        {
            return;
        }

        Data data = GetInternalData<Data>();
        data.attacksThisCombat++;
        if (!data.strengthGranted && data.attacksThisCombat >= 2)
        {
            data.strengthGranted = true;
            Flash();
            await PowerCmd.Apply<StrengthPower>(Owner, 1m, Owner, cardPlay.Card);
        }

        if (data.strengthGranted && !data.firstAttackThisTurnDone && cardPlay.Target != null && !cardPlay.Target.IsPlayer)
        {
            data.firstAttackThisTurnDone = true;
            await PowerCmd.Apply<PoisonPower>(cardPlay.Target, Amount, Owner, cardPlay.Card);
        }
    }

    public override Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            GetInternalData<Data>().firstAttackThisTurnDone = false;
        }

        return Task.CompletedTask;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "至高之术"),
        ("description", "The second Attack each combat grants Strength. Afterward, your first Attack each turn applies extra Poison.")
    };
}

public sealed class AbyssalFormPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        if (player != Owner.Player)
        {
            return;
        }

        foreach (Creature unit in ThornsAlchemy.Units(Owner.CombatState))
        {
            Flash();
            await ThornsAlchemy.Pulse(choiceContext, Owner.CombatState, Owner, null);
        }
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (dealer != Owner || cardSource?.Type != CardType.Attack || Owner.GetPower<AlchemyPulseThisTurnPower>() != null || target.IsPlayer || result.UnblockedDamage <= 0)
        {
            return;
        }

        Flash();
        await PowerCmd.Apply<PoisonPower>(target, 3m, Owner, cardSource);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "无垠海疆"),
        ("description", "At turn start, trigger Alchemical Unit pulses. Unit release effects are doubled. If no pulse happened this turn, your next Attack applies Poison.")
    };
}

public sealed class NavigatorForesightPower : CustomPowerModel
{
    private class Data { public bool usedThisTurn; }
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();

    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (!target.HasPower<AlchemyUnitPower>() || data.usedThisTurn || !target.IsAlive) return;
        if (amount < target.CurrentHp) return;
        data.usedThisTurn = true;
        Flash();
        // Knowledge Demon-style choice via CardSelectCmd.FromChooseACardScreen
        var cs = Owner.CombatState;
        if (cs == null || Owner.Player == null) return;
        var options = new List<CardModel>
        {
            CombatState.CreateCard(ModelDb.Card<NavigatorPulseCard>(), Owner.Player),
            CombatState.CreateCard(ModelDb.Card<NavigatorReleaseCard>(), Owner.Player),
            CombatState.CreateCard(ModelDb.Card<NavigatorBuffCard>(), Owner.Player),
        };
        var chosen = await CardSelectCmd.FromChooseACardScreen(
            new BlockingPlayerChoiceContext(), options, Owner.Player, false);
        // Execute chosen effect using the real combat context
        if (chosen is NavigatorPulseCard)
        {
            await PowerCmd.Apply<BufferPower>(target, 1m, Owner, cardSource);
            if (cs != null) await ThornsAlchemy.Pulse(choiceContext, cs, Owner, cardSource);
            var enemies = cs?.HittableEnemies.Where(e => e.IsAlive && !e.IsPlayer).ToList() ?? new List<Creature>();
            foreach (var e in enemies)
            {
                await PowerCmd.Apply<VulnerablePower>(e, 3m, Owner, cardSource);
                await PowerCmd.Apply<WeakPower>(e, 3m, Owner, cardSource);
            }
        }
        else if (chosen is NavigatorReleaseCard)
        {
            await ThornsAlchemy.Release(choiceContext, target, cardSource);
            if (Owner.Player != null) await ThornsAlchemy.SummonUnit(choiceContext, Owner.Player, cardSource);
        }
        else if (chosen is NavigatorBuffCard)
        {
            await PowerCmd.Apply<StrengthPower>(Owner, 3m, Owner, cardSource);
            await PowerCmd.Apply<DexterityPower>(Owner, 3m, Owner, cardSource);
            await PowerCmd.Apply<RegenPower>(Owner, 3m, Owner, cardSource);
        }
    }

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side) { await PowerCmd.Remove(this); }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自由的第三面"),
        ("description", "The first time each turn an Alchemical Unit would be broken, prevent it and trigger a pulse instead.")
    };
}




public sealed class CatalystPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override bool ShouldPlayVfx => true;

    // When a debuff is applied to the owner, consume 1 catalyst to amplify it
    public static async Task OnDebuffApplied(PlayerChoiceContext ctx, Creature target, PowerModel debuff, Creature applier, CardModel? source)
    {
        if (target.IsDead || !target.IsAlive) return;

        CatalystPower? cat = target.GetPower<CatalystPower>();
        if (cat == null || cat.Amount <= 0) return;

        // Don't self-trigger
        if (debuff is CatalystPower) return;

        // Amplify the debuff and consume 1 catalyst
        cat.Flash();
        await PowerCmd.ModifyAmount(debuff, 1m, applier, source);
        await PowerCmd.ModifyAmount(cat, -1m, applier, source);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "催化"),
        ("description", "目标被赋予催化时，消耗催化使每种已有层数效果各+1。目标已有催化时被赋予新效果，额外+1并消耗1层催化。")
    };
}

// CatalystAmplifierPower removed — catalyst only triggers on application, not at end of turn.

public sealed class NeuroTidePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("HealAmount", 8m)
    };

    public async Task AfterNeuralShock(PlayerChoiceContext choiceContext)
    {
        if (Owner.IsDead || !Owner.IsAlive) return;
        Flash();
        foreach (Player player in Owner.CombatState?.Players ?? Enumerable.Empty<Player>())
        {
            if (player.Creature.IsAlive)
            {
                await CreatureCmd.Heal(player.Creature, DynamicVars["HealAmount"].BaseValue);
            }
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经潮汐"),
        ("description", "Whenever Neural Shock triggers, heal all allies.")
    };
}

public sealed class NeuroSpreadPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("SpreadAmount", 3m)
    };

    public async Task AfterNeuralShock(PlayerChoiceContext choiceContext, Creature shockedTarget)
    {
        if (Owner.IsDead || !Owner.IsAlive || Owner.CombatState == null) return;
        Flash();
        var others = ThornsAlchemy.NormalEnemies(Owner.CombatState)
            .Where(e => e.IsAlive && e != shockedTarget).ToList();
        if (others.Count > 0)
        {
            var rng = new Random();
            var target = others[rng.Next(others.Count)];
            await PowerCmd.Apply<NeuralDamageCounterPower>(target, DynamicVars["SpreadAmount"].BaseValue, Owner, null);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经蔓延"),
        ("description", "Neural Shock spreads Neural Damage to another enemy.")
    };
}

// ============================================================
// NEW POWERS for added cards
// ============================================================

public sealed class AlchemyCorePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task AfterTurnEnd(PlayerChoiceContext ctx, CombatSide side)
    {
        if (side != Owner.Side || Owner.IsDead || !Owner.IsAlive || Owner.CombatState == null) return;
        var units = ThornsAlchemy.Units(Owner.CombatState);
        if (units.Count == 0) return;
        Flash();
        await ThornsAlchemy.Pulse(ctx, Owner.CombatState, Owner, null);
        await CreatureCmd.GainBlock(Owner, 2m * units.Count, ValueProp.Move, null);
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "炼金炉心"), ("description", "回合结束时若有炼金单元则触发脉冲，每个单元获得格挡。") };
}

public sealed class SwarmTidePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task BeforeDamageReceived(PlayerChoiceContext ctx, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? source)
    {
        if (!target.HasPower<AlchemyUnitPower>() || !target.IsAlive || Owner.IsDead) return;
        if (amount < target.CurrentHp) return;
        Flash();
        // Trigger pulse, then deal damage + poison to all enemies
        if (Owner.CombatState != null)
            await ThornsAlchemy.Pulse(ctx, Owner.CombatState, Owner, source);
        foreach (var enemy in ThornsAlchemy.NormalEnemies(Owner.CombatState))
        {
            await DamageCmd.Attack(12m).FromCard(null).Targeting(enemy).WithHitFx("vfx/vfx_attack_slash").Execute(ctx);
            await PowerCmd.Apply<PoisonPower>(enemy, 3m, Owner, null);
        }
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "涌潮大群"), ("description", "炼金单元脉冲和释放时对全体敌人造成8点伤害。") };
}

public sealed class PrecisionMixPower : CustomPowerModel
{
    private class Data { public bool usedThisTurn; }
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();
    public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cp)
    {
        Data d = GetInternalData<Data>();
        if (d.usedThisTurn || cp.Card.Owner != Owner.Player || Owner.CombatState == null) return;
        if (!IsAlchemyCard(cp.Card)) return;
        d.usedThisTurn = true;
        Flash();
        await ThornsAlchemy.SummonUnit(ctx, Owner.Player!, cp.Card);
    }
    private static bool IsAlchemyCard(CardModel c) => c is AlchemicalMixture or LodestarGuidance or DeepSeaRegeneration or StarCataclysm or PrecisionMix or VoyageEnd;
    public override Task AfterTurnEnd(PlayerChoiceContext ctx, CombatSide side)
    {
        if (side == Owner.Side) GetInternalData<Data>().usedThisTurn = false;
        return Task.CompletedTask;
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "精密调配"), ("description", "下张炼金牌额外召唤1个单元。") };
}

public sealed class PlagueSpreadPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public async Task AfterNeuralShock(PlayerChoiceContext ctx, Creature target)
    {
        if (Owner.IsDead || !Owner.IsAlive || Owner.CombatState == null) return;
        Flash();
        foreach (var enemy in ThornsAlchemy.NormalEnemies(Owner.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, 3m, Owner, null);
            await PowerCmd.Apply<NeuralDamageCounterPower>(enemy, 3m, Owner, null);
        }
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "扩散污染"), ("description", "神经震慑触发时全体敌人+3中毒+3神经损伤。") };
}

public sealed class PainDeprivePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public async Task AfterNeuralShock(PlayerChoiceContext ctx, Creature target)
    {
        if (Owner.IsDead || !Owner.IsAlive || target.IsDead || !target.IsAlive) return;
        Flash();
        await PowerCmd.Apply<WeakPower>(target, 2m, Owner, null);
        await PowerCmd.Apply<VulnerablePower>(target, 2m, Owner, null);
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "痛觉剥夺"), ("description", "神经震慑触发时+2虚弱+2易伤。") };
}

public sealed class DeathInstinctPower : CustomPowerModel
{
    private class Data { public int attackCount; }
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();
    public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cp)
    {
        if (cp.Card.Owner != Owner.Player || cp.Card.Type != CardType.Attack) return;
        Data d = GetInternalData<Data>();
        d.attackCount++;
        if (d.attackCount >= 3)
        {
            d.attackCount = 0;
            Flash();
            await PowerCmd.Apply<StrengthPower>(Owner, 1m, Owner, cp.Card);
        }
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "死斗本能"), ("description", "每打出3张攻击牌获得1点力量。") };
}

public sealed class DualCorePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public async Task AfterUnitSummoned(PlayerChoiceContext ctx)
    {
        if (Owner.IsDead || !Owner.IsAlive) return;
        Flash();
        await PowerCmd.Apply<StrengthPower>(Owner, 1m, Owner, null);
    }
    public async Task AfterNeuralShock(PlayerChoiceContext ctx)
    {
        if (Owner.IsDead || !Owner.IsAlive) return;
        Flash();
        await PowerCmd.Apply<DexterityPower>(Owner, 1m, Owner, null);
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "双核驱动"), ("description", "生成炼金单元时+1力量，神经震慑时+1敏捷。") };
}

public sealed class TacticalUnityPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cp)
    {
        if (cp.Card == null || cp.Card.Type != CardType.Power) return;
        if (Owner.CombatState == null) return;
        var units = ThornsAlchemy.Units(Owner.CombatState);
        if (units.Count == 0) return;
        Flash();
        foreach (var unit in units)
            await ThornsAlchemy.Pulse(ctx, Owner.CombatState, Owner, cp.Card);
    }
    public override async Task AfterTurnEnd(PlayerChoiceContext ctx, CombatSide side)
    {
        if (side == Owner.Side) await PowerCmd.Remove(this);
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "战术协同"), ("description", "本回合友方每使用一张能力牌，场上的炼金单元触发一次脉冲。") };
}

public sealed class VoidSwordPower : CustomPowerModel
{
    private class Data { public bool triggered; }
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (player != Owner.Player || Owner.IsDead || Owner.Player == null) return;
        int atkCount = PileType.Hand.GetPile(Owner.Player).Cards.Count(c => c.Type == CardType.Attack);
        int draw = Math.Min(atkCount, 3);
        if (draw > 0) await CardPileCmd.Draw(ctx, draw, Owner.Player);
    }
    public override List<(string, string)> Localization => new List<(string, string)> { ("title", "无我剑境"), ("description", "回合开始时手牌每有1张攻击牌多抽1张（上限3张）。") };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ThornsStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move)
    };

    public ThornsStrike() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "罗德岛剑击"),
        ("description", "造成6点伤害。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ThornsDefend : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Defend };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(5m, ValueProp.Move)
    };

    public ThornsDefend() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "实验防护"),
        ("description", "获得5点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move),
        new PowerVar<PoisonPower>(2m)
    };

    public PoisonStrike() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Poison.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经腐蚀"),
        ("description", "造成5点伤害。施加2层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class QuickSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move),
        new CardsVar(1)
    };

    public QuickSlash() : base(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (p.Target.HasPower<PoisonPower>())
        {
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "迅捷斩击"),
        ("description", "造成4点伤害。若目标有中毒，抽1张牌。")
    };
}

public sealed class IronGuardPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    public override async Task AfterCardPlayed(PlayerChoiceContext ctx, CardPlay cp)
    {
        if (cp.Card.Owner != Owner.Player || Owner.IsDead) return;
        if (cp.Card.Type == CardType.Attack) { await PowerCmd.Remove(this); return; }
        Flash();
        await CreatureCmd.Heal(Owner, 2m);
    }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "故土潮声"),
        ("description", "Heal 2 HP whenever you play a non-Attack card, until you play an Attack.")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class IronGuard : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(7m, ValueProp.Move),
        new HealVar(2m)
    };

    public IronGuard() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<IronGuardPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "故土潮声"),
        ("description", "获得7点格挡。打出下一张攻击牌前每打出一张技能牌或能力牌，回复2点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class RegenerativeSalve : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(5m),
        new BlockVar(6m, ValueProp.Move)
    };

    public RegenerativeSalve() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        if (!ThornsAlchemy.PlayedAttackThisTurn(Owner))
        {
            await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        }
        else
        {
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Heal.UpgradeValueBy(2m); DynamicVars.Block.UpgradeValueBy(2m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身架势"),
        ("description", "炼金单元释放时，对所有敌人造成10点伤害。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DarkStarGaze : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1)
    };

    public DarkStarGaze() : base(0, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await ThornsAlchemy.ApplyCatalyst(p.Target, 1m, Owner.Creature, this);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "心相"),
        ("description", "给予目标1层催化。若场上有炼金单元，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PreciseThrust : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move),
        new CardsVar(1)
    };

    public PreciseThrust() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (ThornsAlchemy.HasCatalyst(p.Target))
        {
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "精准刺击"),
        ("description", "增加1个炼金单元槽位。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DefensiveStance : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(8m, ValueProp.Move),
        new CardsVar("ThornsAmount", 2)
    };

    public DefensiveStance() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, DynamicVars["ThornsAmount"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(3m); DynamicVars["ThornsAmount"].UpgradeValueBy(2m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身尖刺"),
        ("description", "获得8点格挡和2层荆棘。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeurotoxinStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new PowerVar<PoisonPower>(2m)
    };

    public NeurotoxinStrike() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        bool hadPoison = p.Target.HasPower<PoisonPower>();
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<PoisonPower>(p.Target, hadPoison ? DynamicVars.Poison.BaseValue : DynamicVars.Poison.BaseValue + 2m, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Poison.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经毒刃"),
        ("description", "造成6点伤害。若目标有效果则施加2层催化；否则随机施加3层中毒/2层虚弱/1层易伤。")
    };
}


// ============================================================
// COMMON CARDS (25)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class DualStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move),
        new CardsVar("SecondHitBonus", 0)
    };

    public DualStrike() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        // Two hits, each applies neural damage = unblocked damage dealt
        // (Execute returns AttackCommand, not DamageResult — use per-hit block estimate)
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<NeuralDamageCounterPower>(p.Target, DynamicVars.Damage.BaseValue, Owner.Creature, this);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue + DynamicVars["SecondHitBonus"].BaseValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<NeuralDamageCounterPower>(p.Target, DynamicVars.Damage.BaseValue + DynamicVars["SecondHitBonus"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars["SecondHitBonus"].UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "二连刺"),
        ("description", "造成4点伤害2次。每次命中施加等额神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class QuickRecovery : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(4m),
        new CardsVar(1)
    };

    public QuickRecovery() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await CreatureCmd.Heal(Owner.Creature, 2m);
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
        else
        {
            await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "度算浪波"),
        ("description", "获得7点格挡。若场上有炼金单元，改为获得11点格挡并回复4点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalBurn : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(2m),
        new CardsVar("NeuralAmount", 3)
    };

    public ChemicalBurn() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
            await PowerCmd.Apply<NeuralDamageCounterPower>(enemy, DynamicVars["NeuralAmount"].BaseValue, Owner.Creature, this);
            if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
            {
                await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
            }
        }
    }

    protected override void OnUpgrade() { DynamicVars.Poison.UpgradeValueBy(1m); DynamicVars["NeuralAmount"].UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "腐蚀雾区"),
        ("description", "对所有敌人施加3层中毒和3层神经损伤。若场上有炼金单元，额外施加2层催化。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DestrezaThrust : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(3m, ValueProp.Move),
        new RepeatVar(3)
    };

    public DestrezaThrust() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        bool wasUnit = p.Target.HasPower<AlchemyUnitPower>();
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (wasUnit && !p.Target.IsAlive)
        {
            await CardPileCmd.Draw(c, 1, Owner);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "剑术连携"),
        ("description", "随机造成3点伤害3次。若击破炼金单元，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class CrossSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new BlockVar(4m, ValueProp.Move)
    };

    public CrossSlash() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Block.UpgradeValueBy(2m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "交叉斩"),
        ("description", "造成14点伤害。若目标有催化，获得8点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class InkSplash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move)
    };

    public InkSplash() : base(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "远距挥洒"),
        ("description", "对所有敌人造成5点伤害和1层催化。")
    };
}


[Pool(typeof(ThornsCardPool))]
public sealed class CalmWaters : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(9m, ValueProp.Move)
    };

    public CalmWaters() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        CardModel? attack = PileType.Hand.GetPile(Owner).Cards.FirstOrDefault(card => card != this && card.Type == CardType.Attack);
        if (attack != null)
        {
            await CardCmd.Discard(c, attack);
        }
    }

    // TODO: CSV upgrade="不再弃牌" — needs upgrade-state check API
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "冷静调配"),
        ("description", "获得9点格挡。若手牌中有攻击牌，弃1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class OceanShield : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(14m, ValueProp.Move)
    };

    public OceanShield() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        CatalystPower? cat = Owner.Creature.GetPower<CatalystPower>();
        if (cat != null && cat.Amount > 0)
        {
            int layers = (int)cat.Amount;
            await ThornsAlchemy.ClearCatalyst(Owner.Creature, layers, Owner.Creature, this);
            await CreatureCmd.Heal(Owner.Creature, 2m * layers);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "防蚀护具"),
        ("description", "获得14点格挡。清除自身所有催化，每清除1层回复2点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class Starfall : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move)
    };

    public Starfall() : base(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        List<Creature> targets = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true);
        if (targets.Count > 0)
        {
            Creature target = Owner.RunState.Rng.CombatCardSelection.NextItem(targets);
            decimal dmg = DynamicVars.Damage.BaseValue;
            if (target.HasPower<NeuralShockPower>()) dmg *= 3;
            await DamageCmd.Attack(dmg).FromCard(this).Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "抛掷试剂"),
        ("description", "造成12点伤害，随机目标。若目标处于神经震慑，伤害x3。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeepSeaVenom : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(5m)
    };

    public DeepSeaVenom() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue + (ThornsAlchemy.HasCatalyst(p.Target) ? 2m : 0m), Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "深海毒剂"),
        ("description", "施加5层中毒。若目标有催化，额外施加2层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SwiftRetort : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(3m, ValueProp.Move),
        new BlockVar(3m, ValueProp.Move)
    };

    public SwiftRetort() : base(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (Owner.Creature.CurrentHp < Owner.Creature.MaxHp)
        {
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(1m); DynamicVars.Block.UpgradeValueBy(2m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "迅捷反刺"),
        ("description", "造成3点伤害。若上回合失去过生命，获得3点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AlchemicalMixture : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("UnitCount", 1m)
    };

    public AlchemicalMixture() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.Pulse(c, Owner.Creature.CombatState, Owner.Creature, this);
            return;
        }

        await ThornsAlchemy.SummonUnit(c, Owner, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "投放炼金单元"),
        ("description", "召唤1个炼金单元。若场上已有炼金单元，改为触发其脉冲。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationDraw : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public ConstellationDraw() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self) { }
    protected override bool HasEnergyCostX => true;

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        CombatState? cs = Owner.Creature.CombatState;
        if (cs == null) return;
        int x = ResolveEnergyXValue();
        for (int i = 0; i < x; i++)
            await ThornsAlchemy.Pulse(c, cs, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "连环释放"),
        ("description", "消耗所有能量，脉冲X次。X为费用。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AegirResilience : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(6m, ValueProp.Move),
        new HealVar(3m)
    };

    public AegirResilience() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        if (Owner.Creature.HasPower<PoisonPower>() || Owner.Creature.HasPower<CatalystPower>())
        {
            await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2m); DynamicVars.Heal.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "阿戈尔韧性"),
        ("description", "获得6点格挡。若你有中毒或催化，回复3点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move)
    };

    public AbyssalStrike() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        if (p.Target.HasPower<AlchemyUnitPower>())
        {
            await ThornsAlchemy.Release(c, p.Target, this);
            await CreatureCmd.Kill(p.Target);
            // TODO: CSV "不消耗本牌" — needs CardPileCmd API to return card to hand
            return;
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "海嗣斩击"),
        ("description", "造成{Damage}点伤害。若目标是炼金单元，改为触发释放且不消耗本牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class WaveSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new PowerVar<PoisonPower>(2m)
    };

    public WaveSlash() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        List<Creature> otherTargets = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState)
            .Where(enemy => enemy != p.Target)
            .ToList();
        if (otherTargets.Count > 0)
        {
            Creature other = Owner.RunState.Rng.CombatCardSelection.NextItem(otherTargets);
            await PowerCmd.Apply<PoisonPower>(other, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Poison.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "浪波斩"),
        ("description", "造成6点伤害。对另一个随机敌人施加2层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class LodestarGuidance : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1)
    };

    public LodestarGuidance() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.Pulse(c, Owner.Creature.CombatState, Owner.Creature, this);
        }
        else
        {
            await ThornsAlchemy.SummonUnit(c, Owner, this);
        }
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "灯塔指引"),
        ("description", "抽1张牌。若场上已有炼金单元则触发脉冲，否则召唤1个。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class Riptide : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(9m, ValueProp.Move)
    };

    public Riptide() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (Owner.Creature.GetPower<AlchemyPulseThisTurnPower>() != null)
        {
            await PlayerCmd.GainEnergy(1m, Owner);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "潮涌刺击"),
        ("description", "造成5点伤害两次。若本回合释放过炼金单元，消耗能量降为0。")
    };
}


// ============================================================
// UNCOMMON CARDS (30)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class DestrezaMastery : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move), new RepeatVar(4)
    };
    public DestrezaMastery() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        bool poisoned = p.Target.HasPower<PoisonPower>();
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (poisoned)
        {
            int amt = DynamicVars.Repeat.IntValue;
            // Flex-style temporary strength: gain now, lose at end of turn
            await PowerCmd.Apply<StrengthPower>(Owner.Creature, amt, Owner.Creature, this);
            // Flex-style temporary strength
            await PowerCmd.Apply<TemporaryStrengthPower>(Owner.Creature, amt, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(1m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "剑术专精"),
        ("description", "随机造成2点伤害4次。每次命中中毒敌人时获得1层临时力量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeurotoxinCloud : CustomCardModel
{
    public override string? CustomPortraitPath => ImageHelper.GetImagePath("atlases/card_atlas.sprites/necrobinder/debilitate.tres");
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(5m)
    };
    public NeurotoxinCloud() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
            // 摧残: apply DebilitatePower (multiplier-level effect)
            await PowerCmd.Apply<DebilitatePower>(enemy, 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经腐蚀云"),
        ("description", "对所有敌人施加5层中毒和1层摧残。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SelfReconstitution : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(4m, ValueProp.Move),
        new HealVar(1m)
    };
    public SelfReconstitution() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        int catalyst = Owner.Creature.GetPower<CatalystPower>()?.Amount ?? 0;
        if (catalyst <= 0)
        {
            return;
        }

        await ThornsAlchemy.ClearCatalyst(Owner.Creature, catalyst, Owner.Creature, this);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue * catalyst, ValueProp.Move, p);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue * catalyst);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自我重构"),
        ("description", "消耗自身所有催化。每消耗1层获得4点格挡并回复2点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AncientRitual : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m), new PowerVar<DexterityPower>(1m)
    };
    public AncientRitual() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<FirstUnitBreakRewardPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Strength.UpgradeValueBy(1m); DynamicVars.Dexterity.UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "疏通航道"),
        ("description", "每回合炼金单元第一次释放时，获得1点力量和1点敏捷。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationArmor : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(14m, ValueProp.Move)
    };
    public ConstellationArmor() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
            {
                await PowerCmd.Apply<WeakPower>(enemy, 1m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "视界护甲"),
        ("description", "获得14点格挡。若场上有炼金单元，所有敌人获得1层虚弱。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class HealingSprings : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(8m)
    };
    public HealingSprings() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        foreach (Creature creature in Owner.Creature.CombatState?.Creatures.Where(creature => creature.IsAlive).ToList() ?? new List<Creature>())
        {
            await ThornsAlchemy.ApplyCatalyst(creature, 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "再生雾"),
        ("description", "回复3点生命。对所有单位施加1层催化。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TripleStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move), new RepeatVar(3)
    };
    public TripleStrike() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var enemies = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true);
        if (enemies.Count == 0) return;
        var hitTargets = new List<Creature>();
        for (int i = 0; i < 3; i++)
        {
            var target = Owner.RunState?.Rng?.CombatCardSelection?.NextItem(enemies) ?? enemies[new Random().Next(enemies.Count)];
            decimal dmg = DynamicVars.Damage.BaseValue;
            if (target.HasPower<NeuralShockPower>()) dmg *= 3;
            await DamageCmd.Attack(dmg).FromCard(this).Targeting(target)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            hitTargets.Add(target);
        }
        // If all 3 hits landed on the same enemy, apply debuffs
        if (hitTargets.Distinct().Count() == 1 && hitTargets[0].IsAlive)
        {
            await PowerCmd.Apply<PoisonPower>(hitTargets[0], 3m, Owner.Creature, this);
            await PowerCmd.Apply<WeakPower>(hitTargets[0], 1m, Owner.Creature, this);
            await PowerCmd.Apply<VulnerablePower>(hitTargets[0], 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "三段剑术"),
        ("description", "造成3点伤害3次。若三次都命中同一敌人，施加3层中毒，1层虚弱和1层易伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeadlyVenom : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(7m)
    };
    public DeadlyVenom() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        // Check catalyst on target OR self via player's creature powers
        bool hasCat = ThornsAlchemy.HasCatalyst(p.Target);
        bool selfHasCat = Owner.Creature.HasPower<CatalystPower>() && Owner.Creature.GetPower<CatalystPower>()!.Amount > 0;
        if (hasCat || selfHasCat)
        {
            await PlayerCmd.GainEnergy(1m, Owner);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "致命毒剂"),
        ("description", "施加7层中毒。若目标或自身有催化，获得1点能量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalExplosion : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move)
    };
    public ChemicalExplosion() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        bool brokeUnit = false;
        foreach (Creature enemy in ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true))
        {
            bool wasUnit = enemy.HasPower<AlchemyUnitPower>();
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            brokeUnit |= wasUnit && !enemy.IsAlive;
        }
        if (brokeUnit)
        {
            await PlayerCmd.GainEnergy(2m, Owner);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "爆炸艺术"),
        ("description", "对所有敌人造成10点伤害。击破炼金单元时，获得2点能量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class FocusedDestreza : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move), new RepeatVar(3)
    };
    public FocusedDestreza() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        int hits = DynamicVars.Repeat.IntValue + (ThornsAlchemy.PlayedSkillThisTurn(Owner) ? 0 : 1);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(hits).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "专注剑势"),
        ("description", "造成7点伤害3次。若本回合没有打出技能牌，额外造成1次。")
    };
}


[Pool(typeof(ThornsCardPool))]
public sealed class RegenerationAura : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public RegenerationAura() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<AlchemyPulseHealPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    // TODO: CSV upgrade="固有" — needs CardTag.Innate or IsInnate API
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "再生气雾"),
        ("description", "炼金单元脉冲时，对所有敌人造成4点神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SeaKingProtection : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(20m, ValueProp.Move)
    };
    public SeaKingProtection() : base(3, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var players = Owner.Creature.CombatState?.Players ?? new List<Player>();
        int share = (int)DynamicVars.Block.BaseValue / Math.Max(players.Count, 1);
        foreach (var pl in players)
            if (pl.Creature.IsAlive) await CreatureCmd.GainBlock(pl.Creature, (decimal)share, ValueProp.Move, null);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
            {
                await PowerCmd.Apply<ThornsTempStrengthDownPower>(enemy, 10m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "海疆屏障"),
        ("description", "平分30点格挡给所有友方。若场上有炼金单元，敌人获得临时-10力量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarConstellation : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public StarConstellation() : base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<StarConstellationPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "连续航线"),
        ("description", "炼金单元脉冲的中毒层数+3。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class VesselOfPoison : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public VesselOfPoison() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<VesselOfPoisonPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "毒剂容器"),
        ("description", "你的攻击对中毒敌人额外施加1层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class WaveCrash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move)
    };
    public WaveCrash() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
            {
                await PowerCmd.Apply<WeakPower>(enemy, 1m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "涌潮冲击"),
        ("description", "对所有敌人造成12点伤害。若场上有炼金单元，施加1层虚弱。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class CoralShield : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(8m, ValueProp.Move)
    };
    public CoralShield() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        int poisonedEnemies = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState)
            .Count(enemy => enemy.HasPower<PoisonPower>());
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue + 2m * poisonedEnemies, ValueProp.Move, p);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "珊瑚护层"),
        ("description", "获得8点格挡。每有1个中毒敌人额外获得2点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalWhisper : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(3m),
        new CardsVar("NeuralAmount", 3)
    };
    public AbyssalWhisper() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        int highPoisonCount = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState)
            .Count(enemy => (enemy.GetPower<PoisonPower>()?.Amount ?? 0) >= 6);
        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
            await PowerCmd.Apply<NeuralDamageCounterPower>(enemy, DynamicVars["NeuralAmount"].BaseValue, Owner.Creature, this);
        }
        if (highPoisonCount > 0)
        {
            await CardPileCmd.Draw(c, highPoisonCount, Owner);
        }
    }
    protected override void OnUpgrade() { DynamicVars.Poison.UpgradeValueBy(2m); DynamicVars["NeuralAmount"].UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "深海低语"),
        ("description", "对所有敌人施加3层中毒和3层神经损伤。每1名敌人有6层以上中毒，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PhantomSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(10m, ValueProp.Move)
    };
    public PhantomSlash() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        Creature? front = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState).FirstOrDefault();
        if (front != null && p.Target != front)
        {
            await PowerCmd.Apply<PoisonPower>(p.Target, 3m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "远程剑影"),
        ("description", "造成10点伤害。若目标不是前排敌人，施加3层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarryNight : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(4)
    };
    public StarryNight() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            // TODO: CSV "保留其中1张直到下回合" — needs CardTag.Retain or SelfRetain API
            // For now: draw 1 extra card as approximation
            await CardPileCmd.Draw(c, 1, Owner);
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "星夜测绘"),
        ("description", "抽4张牌。若场上有炼金单元，保留其中1张直到下回合。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonMastery : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar("Threshold", 12)
    };

    public PoisonMastery() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<PoisonMasteryPower>(Owner.Creature, DynamicVars["Threshold"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Threshold"].UpgradeValueBy(-2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤"),
        ("description", "敌人每次因中毒失去生命时等量累积神经损伤。累积达12时清空并给予1层神经震慑：下一次攻击伤害变为0。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ThornsBody : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public ThornsBody() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<ThornsBodyPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身尖刺"),
        ("description", "每回合第一次受到攻击时，对攻击者造成5点伤害并施加2层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationMark : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(2m),
        new CardsVar("CatalystAmount", 1)
    };

    public ConstellationMark() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        await ThornsAlchemy.ApplyCatalyst(p.Target, DynamicVars["CatalystAmount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Poison.UpgradeValueBy(1m); DynamicVars["CatalystAmount"].UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "测绘标记"),
        ("description", "施加2层中毒和1层催化。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalCatalyst : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(1),
        new CardsVar("CatalystAmount", 3)
    };

    public ChemicalCatalyst() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        bool hadPoison = p.Target.HasPower<PoisonPower>();
        await ThornsAlchemy.ApplyCatalyst(p.Target, DynamicVars["CatalystAmount"].BaseValue, Owner.Creature, this);
        if (hadPoison)
        {
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
    }
    protected override void OnUpgrade() { DynamicVars.Cards.UpgradeValueBy(1m); DynamicVars["CatalystAmount"].UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "化学催化"),
        ("description", "施加3层催化。若目标已有任何buff类效果，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ThornsBarrier : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(12m, ValueProp.Move),
        new CardsVar("ThornsStacks", 3)
    };

    public ThornsBarrier() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, DynamicVars["ThornsStacks"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(4m); DynamicVars["ThornsStacks"].UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "尖刺壁垒"),
        ("description", "获得12点格挡。获得3层荆棘。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeepSeaRegeneration : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar("RegenAmount", 3)
    };
    public DeepSeaRegeneration() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<RegenPower>(Owner.Creature, DynamicVars["RegenAmount"].BaseValue, Owner.Creature, this);
        if (!ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.SummonUnit(c, Owner, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars["RegenAmount"].UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "深海再生"),
        ("description", "获得3点再生。若场上没有炼金单元，召唤1个炼金单元。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarBlessing : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public StarBlessing() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<StarBlessingPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "引星祝福"),
        ("description", "场上有炼金单元时，你的攻击造成额外1点伤害。")
    };
}


[Pool(typeof(ThornsCardPool))]
public sealed class SeafoamHealing : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(10m),
        new BlockVar(10m, ValueProp.Move)
    };
    public SeafoamHealing() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await ThornsAlchemy.Pulse(c, Owner.Creature.CombatState, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Heal.UpgradeValueBy(4m); DynamicVars.Block.UpgradeValueBy(4m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "度算浪波·专精"),
        ("description", "回复10点生命，获得10点格挡，并触发炼金单元脉冲。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarCataclysm : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(18m, ValueProp.Move)
    };
    public StarCataclysm() : base(3, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        CombatState? combatState = Owner.Creature.CombatState;
        if (combatState == null) { return; }

        // 1. Deal 18 damage to ALL non-AlchemyUnit enemies
        List<Creature> targets = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && !enemy.IsPlayer && !enemy.HasPower<AlchemyUnitPower>())
            .ToList();
        foreach (Creature enemy in targets)
        {
            if (enemy.IsAlive)
            {
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            }
        }

        // 2. Destroy ANY existing AlchemyUnit first via massive damage (forced re-summon)
        foreach (Creature unit in ThornsAlchemy.Units(combatState))
        {
            if (unit.IsAlive)
            {
                await DamageCmd.Attack(9999m).FromCard(this).Targeting(unit)
                    .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            }
        }

        // 3. Summon a fresh Alchemy Unit
        await ThornsAlchemy.SummonUnit(c, Owner, this);

        // 4. Immediately trigger Pulse on the fresh unit
        await ThornsAlchemy.Pulse(c, combatState, Owner.Creature, this);

        // 5. Apply Weak x2, Vulnerable x2, and Catalyst x3 to ALL alive enemies
        List<Creature> allEnemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && !enemy.IsPlayer)
            .ToList();
        foreach (Creature enemy in allEnemies)
        {
            if (enemy.IsAlive)
            {
                await PowerCmd.Apply<WeakPower>(enemy, 2m, Owner.Creature, this);
                await PowerCmd.Apply<VulnerablePower>(enemy, 2m, Owner.Creature, this);
                await ThornsAlchemy.ApplyCatalyst(enemy, 3m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(6m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "我的海疆"),
        ("description", "对所有敌人造成18点伤害。召唤1个炼金单元并触发脉冲。所有敌人获得2层虚弱、2层易伤和3层催化。")
    };
}
[Pool(typeof(ThornsCardPool))]
public sealed class AncientAlchemy : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyRelease;
    public AncientAlchemy() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<AncientAlchemyPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "炼金术"),
        ("description", "每当场上的炼金单元释放时，抽1张牌。每回合限2次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationLegacy : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("UnitLimit", 1m)
    };
    public ConstellationLegacy() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<ConstellationLegacyPower>(Owner.Creature, DynamicVars["UnitLimit"].BaseValue, Owner.Creature, this);
        await ThornsAlchemy.SummonUnit(c, Owner, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "宝宝摇篮号"),
        ("description", "炼金单元上限+1。生成1个炼金单元。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonReaper : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public PoisonReaper() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        foreach (var enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
            await PowerCmd.Apply<NeuralDamageCounterPower>(enemy, 8m, Owner.Creature, this);
        foreach (var enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            if (enemy.HasPower<NeuralShockPower>())
            {
                await PowerCmd.Apply<PoisonPower>(enemy, 10m, Owner.Creature, this);
                await ThornsAlchemy.ApplyCatalyst(enemy, 3m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤爆发"),
        ("description", "对所有敌人施加8层神经损伤。对其中处于神经震慑的敌人施加10层中毒和3层催化。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class GuidingStar : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public GuidingStar() : base(0, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<GuidingStarPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    // TODO: CSV upgrade="固有" — needs CardTag.Innate or IsInnate API
    protected override void OnUpgrade() { }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "至高之术"),
        ("description", "本场战斗第二次打出攻击牌后获得1点力量。之后每回合第一次攻击额根据其伤害累计神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalForm : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public AbyssalForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<AbyssalFormPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "海胆形态"),
        ("description", "炼金单元脉冲和释放额外触发一次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorForesight : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public NavigatorForesight() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<NavigatorForesightPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自由的第三面"),
        ("description", "本回合击破炼金单元时三选一：全体3易伤3虚弱；释放并重新召唤；获得3力量3敏捷3再生。")
    };
}

// ============================================================
// NEW COMMON CARDS
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class SpiritStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new CardsVar("NeuralAmount", 3)
    };
    public SpiritStrike() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (p.Target.HasPower<PoisonPower>())
        {
            await PowerCmd.Apply<NeuralDamageCounterPower>(p.Target, DynamicVars["NeuralAmount"].BaseValue, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars["NeuralAmount"].UpgradeValueBy(2m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "精神刺击"),
        ("description", "造成6点伤害。若目标有中毒，施加3层神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DualSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move),
        new CardsVar("SecondHitBonus", 0)
    };
    public DualSlash() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue + DynamicVars["SecondHitBonus"].BaseValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars["SecondHitBonus"].UpgradeValueBy(2m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "连斩"),
        ("description", "造成4点伤害2次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroParalyze : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<WeakPower>(2m),
        new CardsVar("NeuralAmount", 3)
    };
    public NeuroParalyze() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await PowerCmd.Apply<WeakPower>(p.Target, DynamicVars.Weak.BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<NeuralDamageCounterPower>(p.Target, DynamicVars["NeuralAmount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Weak.UpgradeValueBy(1m); DynamicVars["NeuralAmount"].UpgradeValueBy(2m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经麻痹"),
        ("description", "施加1层易伤，2层虚弱和3层神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroTide : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(8m)
    };
    public NeuroTide() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<NeuroTidePower>(Owner.Creature, DynamicVars.Heal.BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经潮汐"),
        ("description", "每当触发神经震慑时，所有友方回复8点生命。")
    };
}

// ============================================================
// NEW UNCOMMON CARDS
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroDetonate : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move)
    };
    public NeuroDetonate() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        NeuralDamageCounterPower? nd = p.Target.GetPower<NeuralDamageCounterPower>();
        if (nd != null && nd.Amount >= 6)
        {
            await PowerCmd.Remove(nd);
            await PowerCmd.Apply<NeuralShockPower>(p.Target, 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "损伤引爆"),
        ("description", "造成8点伤害。若目标神经损伤≥6，立刻触发神经震慑。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TacticalRetreat : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(12m, ValueProp.Move)
    };
    public TacticalRetreat() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PlayerCmd.GainEnergy(2m, Owner);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "战术撤退"),
        ("description", "获得14点格挡。下回合获得2点额外能量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroSpread : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("SpreadAmount", 3m)
    };
    public NeuroSpread() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<NeuroSpreadPower>(Owner.Creature, DynamicVars["SpreadAmount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["SpreadAmount"].UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经蔓延"),
        ("description", "神经震慑触发时，向随机其他敌人转移3层神经损伤。")
    };
}

// ============================================================
// NEW RARE CARDS (Alchemy)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class AlchemyCore : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(2m, ValueProp.Move)
    };
    public AlchemyCore() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<AlchemyCorePower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "炼金炉心"),
        ("description", "回合结束时若场上有炼金单元，触发1次脉冲。根据场上的催化sum值获得格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SwarmTide : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public SwarmTide() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<SwarmTidePower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "涌潮大群"),
        ("description", "炼金单元被击破时，对所有敌人造成8点伤害。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PrecisionMix : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    public PrecisionMix() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await ThornsAlchemy.SummonUnit(c, Owner, this);
        CombatState? cs = Owner.Creature.CombatState;
        if (cs != null) { await ThornsAlchemy.Pulse(c, cs, Owner.Creature, this); await ThornsAlchemy.Pulse(c, cs, Owner.Creature, this); }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "精密调配"),
        ("description", "生成1个炼金单元，并立刻触发2次脉冲。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class UnitOverload : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyRelease;
    public UnitOverload() : base(1, CardType.Attack, CardRarity.Rare, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        bool brokeAny = false;
        var targets = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true);
        foreach (var enemy in targets)
        {
            bool wasUnit = enemy.HasPower<AlchemyUnitPower>();
            await DamageCmd.Attack(8m).FromCard(this).Targeting(enemy)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
            if (wasUnit && !enemy.IsAlive) brokeAny = true;
        }
        if (brokeAny)
            foreach (var enemy in targets)
                await DamageCmd.Attack(8m).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "单元过载"),
        ("description", "对所有敌人造成8点伤害。若击破炼金单元，立刻再释放一次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class VoyageEnd : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public VoyageEnd() : base(1, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        CombatState? combatState = Owner.Creature.CombatState;
        if (combatState == null) return;
        List<Creature> units = ThornsAlchemy.Units(combatState);
        foreach (Creature unit in units)
        {
            await ThornsAlchemy.Pulse(c, combatState, Owner.Creature, this);
            await CardPileCmd.Draw(c, 2, Owner);
            await CreatureCmd.Kill(unit);
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "航线终点"),
        ("description", "破坏所有炼金单元。每消耗1个触发额外触发1次脉冲并抽2张牌。")
    };
}

// ============================================================
// NEW RARE CARDS (Neural)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class MindCrash : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public MindCrash() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        int r = new Random().Next(9);
        switch (r)
        {
            case 0: await PowerCmd.Apply<PoisonPower>(p.Target, 1m, Owner.Creature, this); break;
            case 1: await PowerCmd.Apply<WeakPower>(p.Target, 1m, Owner.Creature, this); break;
            case 2: await PowerCmd.Apply<VulnerablePower>(p.Target, 1m, Owner.Creature, this); break;
            case 3: await PowerCmd.Apply<FrailPower>(p.Target, 1m, Owner.Creature, this); break;
            case 4: await PowerCmd.Apply<StrengthPower>(p.Target, 1m, Owner.Creature, this); break;
            case 5: await PowerCmd.Apply<DexterityPower>(p.Target, 1m, Owner.Creature, this); break;
            case 6: await PowerCmd.Apply<ThornsPower>(p.Target, 1m, Owner.Creature, this); break;
            case 7: await PowerCmd.Apply<SlowPower>(p.Target, 1m, Owner.Creature, this); break;
            case 8: await PowerCmd.Apply<RitualPower>(p.Target, 1m, Owner.Creature, this); break;
        }
        await ThornsAlchemy.ApplyCatalyst(p.Target, 2m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神秘药剂"),
        ("description", "随机赋予目标（敌或友）一种可催化效果和2层催化。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PlagueSpread : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public PlagueSpread() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<PlagueSpreadPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "扩散污染"),
        ("description", "神经震慑触发时，对全体敌人施加8层中毒和3层神经损伤。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroOverload : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move)
    };
    public NeuroOverload() : base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        int shocks = NeuralShockRunStats.Count;
        await DamageCmd.Attack(15m + shocks).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<NeuralDamageCounterPower>(p.Target, 15m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经超载"),
        ("description", "造成15+本局神经震慑触发次数点伤害。施加15层神经损伤。消耗。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class BrainHijack : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public BrainHijack() : base(1, CardType.Skill, CardRarity.Rare, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        CatalystPower? cat = p.Target.GetPower<CatalystPower>();
        if (cat == null || cat.Amount <= 0) return;
        int layers = (int)cat.Amount;
        await PowerCmd.Remove(cat);
        await PlayerCmd.GainEnergy(layers, Owner);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "触媒再演"),
        ("description", "清除目标所有催化层数，每清除1层获得1点能量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PainDeprive : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public PainDeprive() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<PainDeprivePower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "痛觉剥夺"),
        ("description", "敌人触发神经震慑时，额外获得2层虚弱和2层易伤，自己获得2点力量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeuroStrip : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Move)
    };
    public NeuroStrip() : base(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(14m).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        var nd = p.Target.GetPower<NeuralDamageCounterPower>();
        bool hasShock = p.Target.HasPower<NeuralShockPower>();
        if (nd != null || hasShock)
        {
            var others = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState).Where(e => e.IsAlive && e != p.Target).ToList();
            if (others.Count > 0)
            {
                if (nd != null && nd.Amount > 0) { int amt = (int)nd.Amount; await PowerCmd.Remove(nd); foreach (var o in others) await PowerCmd.Apply<NeuralDamageCounterPower>(o, amt / others.Count, Owner.Creature, this); }
                if (hasShock) { foreach (var o in others) await PowerCmd.Apply<NeuralShockPower>(o, 1m, Owner.Creature, this); }
            }
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经剥离"),
        ("description", "造成14点伤害。将目标神经损伤和神经震慑转移给其他敌人。")
    };
}

// ============================================================
// NEW RARE CARDS (Pure Attack)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class BladeSurge : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public BladeSurge() : base(2, CardType.Attack, CardRarity.Rare, TargetType.RandomEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var enemies = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState);
        if (enemies.Count == 0) return;
        int hits = 1 + (int)(Owner.Creature.GetPower<TotalPulseCountPower>()?.Amount ?? 0);
        for (int i = 0; i < hits; i++)
        {
            var t = Owner.RunState.Rng.CombatCardSelection.NextItem(enemies);
            await DamageCmd.Attack(9m).FromCard(this).Targeting(t)
                .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        }
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "剑潮"),
        ("description", "随机造成9点伤害。本场每触发一次炼金单元脉冲，额外一次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class VitalStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public VitalStrike() : base(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        int types = 0;
        if (p.Target.HasPower<PoisonPower>()) types++;
        if (p.Target.HasPower<WeakPower>()) types++;
        if (p.Target.HasPower<VulnerablePower>()) types++;
        if (p.Target.HasPower<FrailPower>()) types++;
        if (p.Target.HasPower<CatalystPower>()) types++;
        if (p.Target.HasPower<SlowPower>()) types++;
        await DamageCmd.Attack(5m * types).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "命脉刺"),
        ("description", "造成5×敌人身上效果种类数量的伤害。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeathInstinct : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public DeathInstinct() : base(0, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    public override IEnumerable<CardKeyword> CanonicalKeywords => new[] { CardKeyword.Exhaust };
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await ThornsAlchemy.ApplyCatalyst(Owner.Creature, 1m, Owner.Creature, this);
        await PlayerCmd.GainEnergy(2m, Owner);
        await CardPileCmd.Draw(c, 1, Owner);
    }
    protected override void OnUpgrade() { }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "死斗本能"),
        ("description", "获得1层催化，获得2点能量，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ShadowPursuit : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(9m, ValueProp.Move), new RepeatVar(3)
    };
    public ShadowPursuit() : base(2, CardType.Attack, CardRarity.Rare, TargetType.RandomEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        var enemies = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState);
        if (enemies.Count == 0) return;
        var t = Owner.RunState.Rng.CombatCardSelection.NextItem(enemies);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue)
            .FromCard(this).Targeting(t).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<RegenPower>(Owner.Creature, DynamicVars.Repeat.IntValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "残影追斩"),
        ("description", "随机造成9点伤害3次，获得3点再生。")
    };
}

// ============================================================
// NEW RARE CARDS (Universal)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class DualCore : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public DualCore() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<DualCorePower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "双核驱动"),
        ("description", "每当你生成炼金单元时获得1点力量，每当你触发神经震慑时获得1点敏捷。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TacticalUnity : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public TacticalUnity() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<TacticalUnityPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "战术协同"),
        ("description", "本回合炼金单元每次脉冲和释放结算两次。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class VoidSword : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    public VoidSword() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await PowerCmd.Apply<VoidSwordPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "无我剑境"),
        ("description", "每回合开始时手牌中每有1张攻击牌多抽1张（上限3张）。")
    };
}

// NavigatorForesight choice cards
[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorPulseCard : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public NavigatorPulseCard() : base(-1, CardType.Status, CardRarity.Basic, TargetType.None, showInCardLibrary: false) { }
    public override List<(string, string)> Localization => new() { ("title", "特殊脉冲"), ("description", "全体敌人3伤+3易伤+3虚弱") };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorReleaseCard : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyRelease;
    public NavigatorReleaseCard() : base(-1, CardType.Status, CardRarity.Basic, TargetType.None, showInCardLibrary: false) { }
    public override List<(string, string)> Localization => new() { ("title", "释放重生"), ("description", "释放并重新召唤炼金单元") };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorBuffCard : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    public NavigatorBuffCard() : base(-1, CardType.Status, CardRarity.Basic, TargetType.None, showInCardLibrary: false) { }
    public override List<(string, string)> Localization => new() { ("title", "自我强化"), ("description", "获得3力量+3敏捷+3再生") };
}
