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

    public static bool HasCatalyst(Creature creature) => creature.GetPower<AccelerantPower>()?.Amount > 0;

    public static async Task ApplyCatalyst(Creature target, decimal amount, Creature applier, CardModel? source)
    {
        await PowerCmd.Apply<AccelerantPower>(target, amount, applier, source);
        await PowerCmd.Apply<CatalystAmplifierPower>(target, 1m, applier, source, silent: true);
        if (amount > 0)
        {
            bool has = false;
            PoisonPower? p = target.GetPower<PoisonPower>();
            WeakPower? w = target.GetPower<WeakPower>();
            VulnerablePower? v = target.GetPower<VulnerablePower>();
            if (p != null && p.Amount > 0) { await PowerCmd.ModifyAmount(p, 1m, applier, source); has = true; }
            if (w != null && w.Amount > 0) { await PowerCmd.ModifyAmount(w, 1m, applier, source); has = true; }
            if (v != null && v.Amount > 0) { await PowerCmd.ModifyAmount(v, 1m, applier, source); has = true; }
            if (has)
            {
                AccelerantPower? cat = target.GetPower<AccelerantPower>();
                if (cat != null && cat.Amount > 0) { await PowerCmd.ModifyAmount(cat, -1m, applier, source); }
            }
        }
    }

    public static async Task ClearCatalyst(Creature target, decimal amount, Creature applier, CardModel? source)
    {
        AccelerantPower? catalyst = target.GetPower<AccelerantPower>();
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
        {
            await power.AfterUnitSummoned(choiceContext, source);
        }
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

        foreach (Creature enemy in NormalEnemies(combatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, 1m, applier, source);
            if (applier.GetPower<StarConstellationPower>() != null)
            {
                await ApplyCatalyst(enemy, 1m, applier, source);
            }
        }

        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive).ToList())
        {
            await ApplyCatalyst(creature, 1m, applier, source);
        }

        AlchemyPulseHealPower? healPower = applier.GetPower<AlchemyPulseHealPower>();
        if (healPower != null && applier.IsAlive)
        {
            await CreatureCmd.Heal(applier, healPower.Amount);
        }
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

        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive).ToList())
        {
            await ApplyCatalyst(creature, catalyst, applier, source);
        }

        Player? player = applier.Player ?? combatState.Players.FirstOrDefault();
        if (player != null)
        {
            await CardPileCmd.Draw(choiceContext, multiplier, player);
        }

        await PowerCmd.Apply<AlchemyReleasedThisTurnPower>(applier, 1m, applier, source, silent: true);
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

    public static async Task ApplyDebuffWithCatalystBoost<T>(Creature target, decimal amount, Creature applier, CardModel? source) where T : PowerModel
    {
        AccelerantPower? cat = target.GetPower<AccelerantPower>();
        if (cat != null && cat.Amount > 0)
        {
            await PowerCmd.Apply<T>(target, amount + 1m, applier, source);
            await PowerCmd.ModifyAmount(cat, -1m, applier, source);
        }
        else
        {
            await PowerCmd.Apply<T>(target, amount, applier, source);
        }
    }

}



// ============================================================
// CUSTOM POWERS for Thorns
// ============================================================

public sealed class ThornsPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    // Triggered when Owner takes damage - deal 1 damage back
    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (Owner.IsDead || target != Owner || dealer == null || dealer.IsPlayer || !Owner.IsAlive || result.UnblockedDamage <= 0)
            return;

        Flash();
        await DamageCmd.Attack(1m).FromCard(null).Targeting(dealer)
            .WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "When attacked, deal 1 damage back.")
    };
}
public sealed class RegenerationPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("HealAmount", 3m)
    };

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side && Owner.IsAlive)
        {
            Flash();
            await CreatureCmd.Heal(Owner, DynamicVars["HealAmount"].BaseValue);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "At end of turn, heal HP.")
    };
}

public sealed class HomelandTidePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side != Owner.Side || Owner.IsDead)
        {
            return;
        }

        bool attacked = CombatManager.Instance.History.Entries.OfType<CardPlayFinishedEntry>()
            .Any(e => e.CardPlay.Card.Owner == Owner.Player && e.CardPlay.Card.Type == CardType.Attack && e.HappenedThisTurn(Owner.CombatState));
        if (!attacked)
        {
            Flash();
            await CreatureCmd.Heal(Owner, Amount);
        }

        await PowerCmd.Remove(this);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "故土潮声"),
        ("description", "At end of turn, if you played no Attacks this turn, heal HP.")
    };
}

public sealed class PoisonMasteryPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    private const int Threshold = 12;

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
        if (counter != null && counter.Amount >= Threshold)
        {
            await PowerCmd.Remove(counter);
            await PowerCmd.Apply<NeuralShockPower>(target, 1m, Owner, null);
            foreach (PoisonReaperPower reaper in Owner.Powers.OfType<PoisonReaperPower>())
            {
                await reaper.AfterNeuralShock(choiceContext, target);
            }
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤"),
        ("description", "Enemies accumulate Neural Damage when they lose HP from Poison. At 12, reset it and make their next attack deal 0 damage.")
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

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (target == Owner && result.UnblockedDamage > 0 && !data.released)
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

public sealed class ConstellationPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("StrengthGain", 1m)
    };

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side && Owner.IsAlive)
        {
            Flash();
            await PowerCmd.Apply<StrengthPower>(Owner, DynamicVars["StrengthGain"].BaseValue, Owner, null);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "At end of turn, gain Strength.")
    };
}

public sealed class LodestarRadiancePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("RadianceDamage", 6m)
    };

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side != Owner.Side || Owner.IsDead || !Owner.IsAlive) return;
        if (Owner.CombatState == null) return;

        Flash();
        foreach (Creature enemy in Owner.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
            {
                await DamageCmd.Attack(DynamicVars["RadianceDamage"].BaseValue).FromCard(null).Targeting(enemy)
                    .WithHitFx("vfx/vfx_spell_cast").Execute(choiceContext);
                await PowerCmd.Apply<PoisonPower>(enemy, 1m, Owner, null);
            }
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "At end of turn, deal damage to ALL enemies and apply 1 Poison.")
    };
}

public sealed class ConstellationInsightPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("CardsDrawn", 1m)
    };

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side && Owner.IsAlive && Owner.Player != null)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, (int)DynamicVars["CardsDrawn"].BaseValue, Owner.Player);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Star Chart"),
        ("description", "At end of turn, draw cards.")
    };
}

public sealed class AbyssalPoisonPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("PoisonAmount", 3m)
    };

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side != Owner.Side || Owner.IsDead || !Owner.IsAlive) return;

        Flash();
        foreach (Creature enemy in Owner.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars["PoisonAmount"].BaseValue, Owner, null);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "At end of turn, apply Poison to ALL enemies.")
    };
}

public sealed class AlchemicalBrewPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side && Owner.IsAlive && Owner.Player != null)
        {
            Flash();
            await CardPileCmd.Draw(choiceContext, 1, Owner.Player);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Alchemy"),
        ("description", "At end of turn, draw 1 additional card.")
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
        ("title", "????"),
        ("description", "Attacks apply Poison.")
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
        ("title", "Ancient Ritual"),
        ("description", "The first time an Alchemical Unit releases each combat, gain Strength and Dexterity.")
    };
}

public sealed class FirstUnitBreakRewardSpentPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;
}

public sealed class RetaliateOncePower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target != Owner || dealer == null || dealer.IsPlayer || result.UnblockedDamage <= 0 || Amount <= 0)
        {
            return;
        }

        Flash();
        await DamageCmd.Attack(4m).FromCard(null).Targeting(dealer).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
        await PowerCmd.Decrement(this);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "鍙嶅埗"),
        ("description", "The next time you take unblocked attack damage, deal damage back.")
    };
}

public sealed class ThornsBodyPower : CustomPowerModel
{
    private class Data { public bool triggeredThisTurn; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override object InitInternalData() => new Data();

    public override async Task BeforeDamageReceived(PlayerChoiceContext choiceContext, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (target != Owner || dealer == null || dealer.IsPlayer || !props.IsPoweredAttack() || data.triggeredThisTurn)
        {
            return;
        }

        data.triggeredThisTurn = true;
        Flash();
        await DamageCmd.Attack(5m * Amount).FromCard(null).Targeting(dealer).WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
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
        ("title", "鎶よ韩灏栧埡"),
        ("description", "The first time each turn you are attacked, retaliate and apply Poison.")
    };
}

public sealed class AlchemyPulseHealPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "鍐嶇敓鍏夌幆"),
        ("description", "Alchemical Unit pulses heal you.")
    };
}

public sealed class StarConstellationPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "鏄熷浘"),
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
        ("title", "寮曟槦绁濈"),
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
        ("title", "????"),
        ("description", "Whenever you summon an Alchemical Unit, draw cards, up to twice each turn.")
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
        return card is AlchemicalMixture or LodestarGuidance or OceanCurrent or LodestarPower or DeepSeaRegeneration or StarCataclysm;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "????"),
        ("description", "Alchemical Unit limit is increased. Your first Alchemy card each combat summons an extra unit. Unit releases grant Block.")
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
        ("title", "鑷抽珮涔嬫湳"),
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
        ("title", "鏃犲灎娴风枂"),
        ("description", "At turn start, trigger Alchemical Unit pulses. Unit release effects are doubled. If no pulse happened this turn, your next Attack applies Poison.")
    };
}

public sealed class NavigatorForesightPower : CustomPowerModel
{
    private class Data { public bool usedThisTurn; }

    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Single;
    protected override object InitInternalData() => new Data();

    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        Data data = GetInternalData<Data>();
        if (!target.HasPower<AlchemyUnitPower>() || data.usedThisTurn || result.UnblockedDamage <= 0)
        {
            return;
        }

        data.usedThisTurn = true;
        Flash();
        await ThornsAlchemy.SummonUnit(choiceContext, Owner.Player ?? Owner.CombatState.Players.First(), cardSource);
    }

    public override Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (side == Owner.Side)
        {
            GetInternalData<Data>().usedThisTurn = false;
        }

        return Task.CompletedTask;
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "鑷敱鐨勭涓夐潰"),
        ("description", "The first time each turn an Alchemical Unit is broken, resummon it.")
    };
}




public sealed class CatalystAmplifierPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    protected override bool IsVisibleInternal => false;
    public override bool ShouldPlayVfx => false;

    public override async Task AfterTurnEnd(PlayerChoiceContext ctx, CombatSide side)
    {
        if (Owner.IsDead || !Owner.IsAlive) return;
        if (side != Owner.Side) return;

        AccelerantPower? cat = Owner.GetPower<AccelerantPower>();
        if (cat == null || cat.Amount <= 0) return;

        bool has = false;
        PoisonPower? p = Owner.GetPower<PoisonPower>();
        WeakPower? w = Owner.GetPower<WeakPower>();
        VulnerablePower? v = Owner.GetPower<VulnerablePower>();

        if (p != null && p.Amount > 0) has = true;
        if (w != null && w.Amount > 0) has = true;
        if (v != null && v.Amount > 0) has = true;

        if (!has) return;

        cat = Owner.GetPower<AccelerantPower>();
        if (cat == null || cat.Amount <= 0) return;

        Flash();
        if (p != null && p.Amount > 0) await PowerCmd.ModifyAmount(p, 1m, Owner, null);
        if (w != null && w.Amount > 0) await PowerCmd.ModifyAmount(w, 1m, Owner, null);
        if (v != null && v.Amount > 0) await PowerCmd.ModifyAmount(v, 1m, Owner, null);
        await PowerCmd.ModifyAmount(cat, -1m, Owner, null);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "催化增幅"),
        ("description", "催化会放大敌人身上的减益效果。")
    };
}

// ============================================================
// BASIC CARDS
// ============================================================

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
        if (Owner.Creature.GetPower<AccelerantPower>() != null)
        {
            await ThornsAlchemy.ClearCatalyst(Owner.Creature, 1m, Owner.Creature, this);
            await CreatureCmd.Heal(Owner.Creature, 2m);
        }
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
        ("description", "造成4点伤害。施加2层中毒。")
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
        await PowerCmd.Apply<HomelandTidePower>(Owner.Creature, DynamicVars.Heal.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身架势"),
        ("description", "获得7点格挡。下回合开始时，若本回合没有打出攻击牌，回复2点生命。")
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

    public RegenerativeSalve() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

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
        ("title", "故土潮声"),
        ("description", "若本回合没有打出攻击牌，回复5点生命；否则获得6点格挡。")
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

    public DarkStarGaze() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.ApplyCatalyst(Owner.Creature, 1m, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "心相"),
        ("description", "抽1张牌。若场上有炼金单元，获得1层催化。")
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

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Cards.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "精准刺击"),
        ("description", "造成7点伤害。若目标有催化，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DefensiveStance : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(8m, ValueProp.Move)
    };

    public DefensiveStance() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<RetaliateOncePower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "护身架势"),
        ("description", "获得8点格挡。下次受到未格挡攻击时，对随机敌人造成4点伤害。")
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
        await PowerCmd.Apply<PoisonPower>(p.Target, hadPoison ? DynamicVars.Poison.BaseValue : 4m, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Poison.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经毒刃"),
        ("description", "造成6点伤害。施加2层中毒。若目标没有中毒，改为施加4层中毒。")
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
        new RepeatVar(2)
    };

    public DualStrike() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (p.Target.IsAlive)
        {
            await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Repeat.IntValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "二连刺"),
        ("description", "造成4点伤害2次。每次命中均触发神经腐蚀。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonBlade : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new PowerVar<PoisonPower>(4m)
    };

    public PoisonBlade() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

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
        ("title", "淬毒剑刃"),
        ("description", "造成5点伤害。施加4层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarlightBarrier : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(6m, ValueProp.Move)
    };

    public StarlightBarrier() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        foreach (Creature unit in ThornsAlchemy.Units(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<BufferPower>(unit, 1m, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "炼金屏障"),
        ("description", "获得6点格挡。使炼金单元获得1层护甲，下一次受到伤害改为失去护甲。")
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

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "应急试剂"),
        ("description", "回复4点生命。若场上有炼金单元，改为回复2点生命并抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalBurn : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(2m)
    };

    public ChemicalBurn() : base(1, CardType.Skill, CardRarity.Common, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
            if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
            {
                await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
            }
        }
    }

    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "腐蚀雾区"),
        ("description", "对所有敌人施加2层中毒。若场上有炼金单元，额外施加1层催化。")
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

    protected override void OnUpgrade() => DynamicVars.Repeat.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "剑术连携"),
        ("description", "造成3点伤害3次。若击破炼金单元，抽1张牌。")
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
        ("description", "造成6点伤害。获得4点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SeaBreeze : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(5m, ValueProp.Move),
        new CardsVar(1)
    };

    public SeaBreeze() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        if (Owner.Creature.GetPower<AlchemyReleasedThisTurnPower>() == null)
        {
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2m); DynamicVars.Cards.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "潮声回稳"),
        ("description", "获得5点格挡。若本回合没有击破炼金单元，抽1张牌。")
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
                .WithHitFx("vfx/vfx_spell_cast").Execute(c);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "远距挥洒"),
        ("description", "对所有敌人造成5点伤害。炼金单元不会受到此牌伤害。")
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

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "冷静调配"),
        ("description", "获得9点格挡。若手牌中有攻击牌，弃1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ToxicEdge : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move),
        new PowerVar<PoisonPower>(3m)
    };

    public ToxicEdge() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (ThornsAlchemy.HasCatalyst(p.Target))
        {
            await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Poison.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "毒锋"),
        ("description", "造成7点伤害。若目标有催化，施加3层中毒。")
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

    public OceanShield() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "防蚀护具"),
        ("description", "获得14点格挡。清除自身1层催化，然后回复2点生命。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class Starfall : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move)
    };

    public Starfall() : base(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        List<Creature> targets = ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true);
        if (targets.Count > 0)
        {
            Creature target = Owner.RunState.Rng.CombatCardSelection.NextItem(targets);
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(target)
                .WithHitFx("vfx/vfx_spell_cast").Execute(c);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "抛掷试剂"),
        ("description", "造成8点伤害，随机目标。可以命中炼金单元。")
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

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(1m); DynamicVars.Block.UpgradeValueBy(1m); }

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
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    public ConstellationDraw() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        if (!ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            CardModel? discard = PileType.Hand.GetPile(Owner).Cards.FirstOrDefault(card => card != this);
            if (discard != null)
            {
                await CardCmd.Discard(c, discard);
            }
        }
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "航路测算"),
        ("description", "抽2张牌。若场上没有炼金单元，弃1张牌。")
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
        if (Owner.Creature.HasPower<PoisonPower>() || Owner.Creature.HasPower<AccelerantPower>())
        {
            await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        }
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2m); DynamicVars.Heal.UpgradeValueBy(2m); }

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
            return;
        }

        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "海嗣斩击"),
        ("description", "造成8点伤害。若目标是炼金单元，改为触发释放且不消耗本牌。")
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
        ("description", "选择：召唤1个炼金单元；或触发场上炼金单元的脉冲。抽1张牌。")
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
        ("description", "造成9点伤害。若本回合触发过炼金单元脉冲，返还1点能量。")
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
    public DestrezaMastery() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        bool poisoned = p.Target.HasPower<PoisonPower>();
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (poisoned)
        {
            await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Repeat.IntValue, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(1m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "剑术专精"),
        ("description", "造成4点伤害4次。每次命中中毒敌人时获得1层临时力量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeurotoxinCloud : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
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
            await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经腐蚀云"),
        ("description", "对所有敌人施加5层中毒和1层催化。")
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

        int catalyst = Owner.Creature.GetPower<AccelerantPower>()?.Amount ?? 0;
        if (catalyst <= 0)
        {
            return;
        }

        await ThornsAlchemy.ClearCatalyst(Owner.Creature, catalyst, Owner.Creature, this);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block.BaseValue * catalyst, ValueProp.Move, p);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue * catalyst);
    }
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自我重构"),
        ("description", "消耗自身所有催化。每消耗1层，获得4点格挡并回复1点生命。")
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
        ("title", "第三面的选择"),
        ("description", "每场战斗第一次击破炼金单元时，获得1点力量和1点敏捷。")
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
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(5m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "再生雾"),
        ("description", "回复8点生命。对所有单位施加1层催化。")
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
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
        if (p.Target.IsAlive)
        {
            await PowerCmd.Apply<PoisonPower>(p.Target, 3m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "三段剑术"),
        ("description", "造成5点伤害3次。若三次都命中同一敌人，施加3层中毒。")
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
        if (ThornsAlchemy.HasCatalyst(p.Target))
        {
            await PlayerCmd.GainEnergy(1m, Owner);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "致命毒剂"),
        ("description", "施加7层中毒。若目标有催化，获得1点能量。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class OceanCurrent : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyUnit;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move), new CardsVar(1)
    };
    public OceanCurrent() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
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
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2m); DynamicVars.Cards.UpgradeValueBy(1m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "移动单元"),
        ("description", "造成4点伤害。若场上有炼金单元，触发其脉冲；否则召唤1个炼金单元。抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalExplosion : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(10m, ValueProp.Move)
    };
    public ChemicalExplosion() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        bool brokeUnit = false;
        foreach (Creature enemy in ThornsAlchemy.HittableEnemies(Owner.Creature.CombatState, includeAlchemyUnits: true))
        {
            bool wasUnit = enemy.HasPower<AlchemyUnitPower>();
            await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                .WithHitFx("vfx/vfx_spell_cast").Execute(c);
            brokeUnit |= wasUnit && !enemy.IsAlive;
        }
        if (brokeUnit)
        {
            SetToFreeThisTurn();
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "爆炸艺术"),
        ("description", "对所有敌人造成10点伤害。击破炼金单元时，本牌费用本回合变为0。")
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
        ("description", "造成7点伤害3次。若你本回合没有打出技能牌，额外造成1次。")
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
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "再生气雾"),
        ("description", "炼金单元脉冲时，你回复1点生命。")
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
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
            {
                await PowerCmd.Apply<WeakPower>(enemy, 2m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(6m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "海疆屏障"),
        ("description", "获得20点格挡。若场上有炼金单元，对所有敌人施加2层虚弱。")
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
        ("description", "炼金单元脉冲效果额外触发1次，但每次也使所有敌人获得1层催化。")
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
                .WithHitFx("vfx/vfx_spell_cast").Execute(c);
            if (ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
            {
                await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "涌潮冲击"),
        ("description", "对所有敌人造成12点伤害。若场上有炼金单元，施加1层催化。")
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
        ("description", "获得8点格挡。每有1个中毒敌人，额外获得2点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalWhisper : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(3m)
    };
    public AbyssalWhisper() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        bool shouldDraw = ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState)
            .Any(enemy => (enemy.GetPower<PoisonPower>()?.Amount ?? 0) >= 6);
        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        }
        if (shouldDraw)
        {
            await CardPileCmd.Draw(c, 1, Owner);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(2m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "深海低语"),
        ("description", "对所有敌人施加3层中毒。若任意敌人有6层以上中毒，抽1张牌。")
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
public sealed class TidalSurge : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move), new BlockVar(8m, ValueProp.Move)
    };
    public TidalSurge() : base(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await ThornsAlchemy.Pulse(c, Owner.Creature.CombatState, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(3m); DynamicVars.Block.UpgradeValueBy(3m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "潮涌连斩"),
        ("description", "造成8点伤害。获得8点格挡。触发炼金单元脉冲。")
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
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);
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
    public PoisonMastery() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<PoisonMasteryPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤"),
        ("description", "能力。敌人每次因中毒失去生命时，等量累积神经损伤。累积达到12时清空，给予其1层神经震慑：下一次攻击伤害变为0。")
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
public sealed class LodestarPower : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.AlchemyRelease;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("PoisonCount", 2m)
    };
    public LodestarPower() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        Creature? unit = ThornsAlchemy.Units(Owner.Creature.CombatState).FirstOrDefault();
        if (unit != null)
        {
            await ThornsAlchemy.Release(c, unit, this);
            await CreatureCmd.Kill(unit);
            return;
        }

        await ThornsAlchemy.SummonUnit(c, Owner, this);
        foreach (Creature enemy in ThornsAlchemy.NormalEnemies(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.ApplyCatalyst(enemy, 1m, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars["PoisonCount"].UpgradeValueBy(1m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "引星实验"),
        ("description", "召唤1个炼金单元。所有敌人获得1层催化。若场上已有炼金单元，改为使其释放。")
    };
}


[Pool(typeof(ThornsCardPool))]
public sealed class SeaFoamBarrier : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(5m, ValueProp.Move)
    };
    public SeaFoamBarrier() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        bool hasAlchemyCard = PileType.Hand.GetPile(Owner).Cards.Any(card => card is AlchemicalMixture or LodestarGuidance or OceanCurrent or LodestarPower or DeepSeaRegeneration or StarCataclysm);
        if (hasAlchemyCard)
        {
            await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "浪沫防线"),
        ("description", "获得5点格挡。若手牌中有炼金单元牌，额外获得5点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationMark : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(2m), new CardsVar(1)
    };
    public ConstellationMark() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        await ThornsAlchemy.ApplyCatalyst(p.Target, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Poison.UpgradeValueBy(1m); DynamicVars.Cards.UpgradeValueBy(1m); }
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
        new CardsVar(1)
    };
    public ChemicalCatalyst() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);

        bool hadPoison = p.Target.HasPower<PoisonPower>();
        await ThornsAlchemy.ApplyCatalyst(p.Target, 3m, Owner.Creature, this);
        if (hadPoison)
        {
            await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "化学催化"),
        ("description", "施加3层催化。若目标已有中毒，抽1张牌。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ThornsBarrier : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(12m, ValueProp.Move)
    };
    public ThornsBarrier() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, 3m, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(4m); }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "尖刺壁垒"),
        ("description", "获得12点格挡。获得3层反刺。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeepSeaRegeneration : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(6m)
    };
    public DeepSeaRegeneration() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        if (!ThornsAlchemy.HasUnit(Owner.Creature.CombatState))
        {
            await ThornsAlchemy.SummonUnit(c, Owner, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(3m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "深海再生"),
        ("description", "回复6点生命。若场上没有炼金单元，召唤1个炼金单元。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarBlessing : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(2m)
    };
    public StarBlessing() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<StarBlessingPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Strength.UpgradeValueBy(1m);
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
    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(5m);
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

        List<Creature> targets = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && !enemy.IsPlayer)
            .ToList();
        foreach (Creature enemy in targets)
        {
            if (enemy.IsAlive)
            {
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_spell_cast").Execute(c);
            }
        }

        List<Creature> remainingEnemies = combatState.HittableEnemies
            .Where(enemy => enemy.IsAlive && !enemy.IsPlayer && !enemy.HasPower<AlchemyUnitPower>())
            .ToList();
        if (remainingEnemies.Count == 0) { return; }

        if (!ThornsAlchemy.HasUnit(combatState))
        {
            await ThornsAlchemy.SummonUnit(c, Owner, this);
        }
        await ThornsAlchemy.Pulse(c, combatState, Owner.Creature, this);

        foreach (Creature enemy in remainingEnemies)
        {
            if (enemy.IsAlive)
            {
                await PowerCmd.Apply<WeakPower>(enemy, 2m, Owner.Creature, this);
                await PowerCmd.Apply<AccelerantPower>(enemy, 2m, Owner.Creature, this);
            }
        }
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(8m);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "我的海疆"),
        ("description", "对所有敌人造成18点伤害。召唤1个炼金单元并立即触发其脉冲。所有敌人获得2层虚弱和2层催化。")
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
        ("description", "每当你召唤炼金单元，抽1张牌。每回合限2次。")
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
    public ConstellationLegacy() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<ConstellationLegacyPower>(Owner.Creature, DynamicVars["UnitLimit"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "宝宝摇篮号"),
        ("description", "炼金单元上限+1。每场战斗第一次打出炼金单元牌时，额外召唤1个炼金单元。炼金单元释放后，获得4点格挡。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonReaper : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.NeuralDamage;
    public PoisonReaper() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<PoisonReaperPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "神经损伤爆发"),
        ("description", "能力。神经损伤触发神经震慑时，额外对该敌人施加6层中毒并触发一次炼金单元脉冲。")
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
    protected override void OnUpgrade() { }
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "至高之术"),
        ("description", "本场战斗第二次打出攻击牌后，获得1点力量。之后每回合第一次攻击额外触发一次神经腐蚀。")
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
        ("title", "无垠海疆"),
        ("description", "回合开始时触发所有炼金单元脉冲。炼金单元释放效果翻倍。若本回合没有炼金单元脉冲，你的下一张攻击牌施加3层中毒。")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorForesight : CustomCardModel
{
    public override string? CustomPortraitPath => ThornsPortraits.MySea;
    public NavigatorForesight() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {

        await PowerCmd.Apply<NavigatorForesightPower>(Owner.Creature, 1m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自由的第三面"),
        ("description", "每回合第一次你将击破炼金单元时，选择：改为触发脉冲；或释放后重新召唤1个炼金单元。")
    };
}
