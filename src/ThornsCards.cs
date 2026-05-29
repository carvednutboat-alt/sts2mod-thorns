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
    public static IEnumerable<Creature> Units(CombatState? combatState)
    {
        return combatState?.Enemies.Where(e => e.IsAlive && e.HasPower<AlchemyUnitPower>()) ?? Enumerable.Empty<Creature>();
    }

    public static bool HasUnit(CombatState? combatState) => Units(combatState).Any();

    public static async Task<Creature> SummonUnit(PlayerChoiceContext choiceContext, Player owner, CardModel? source)
    {
        Creature unit = await CreatureCmd.Add<OneHpMonster>(owner.Creature.CombatState);
        await PowerCmd.Apply<MinionPower>(unit, 1m, owner.Creature, source);
        await PowerCmd.Apply<AlchemyUnitPower>(unit, 1m, owner.Creature, source);
        return unit;
    }

    public static async Task Pulse(PlayerChoiceContext choiceContext, CombatState combatState, Creature applier, CardModel? source)
    {
        foreach (Creature enemy in combatState.HittableEnemies.Where(e => e.IsAlive && !e.HasPower<AlchemyUnitPower>()))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, 1m, applier, source);
        }

        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive))
        {
            await PowerCmd.Apply<AccelerantPower>(creature, 1m, applier, source);
        }
    }

    public static async Task Release(PlayerChoiceContext choiceContext, Creature unit, CardModel? source, int multiplier = 1)
    {
        CombatState combatState = unit.CombatState;
        Creature applier = unit.GetPower<AlchemyUnitPower>()?.Applier ?? combatState.Players.First().Creature;
        int poison = 5 * multiplier;
        int catalyst = 2 * multiplier;

        foreach (Creature enemy in combatState.HittableEnemies.Where(e => e.IsAlive && !e.HasPower<AlchemyUnitPower>()))
        {
            await PowerCmd.Apply<PoisonPower>(enemy, poison, applier, source);
        }

        foreach (Creature creature in combatState.Creatures.Where(c => c.IsAlive))
        {
            await PowerCmd.Apply<AccelerantPower>(creature, catalyst, applier, source);
        }

        Player? player = applier.Player ?? combatState.Players.FirstOrDefault();
        if (player != null)
        {
            await CardPileCmd.Draw(choiceContext, multiplier, player);
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
        ("title", "Thorns"),
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
        ("title", "Regeneration"),
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
        ("title", "Star Power"),
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
        ("title", "Lodestar Radiance"),
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
        ("title", "Abyssal Toxin"),
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
        if (Owner.IsDead || dealer != Owner || target.IsPlayer || result.UnblockedDamage <= 0 || !props.IsPoweredAttack())
            return;

        Flash();
        await PowerCmd.Apply<PoisonPower>(target, DynamicVars["PoisonAmount"].BaseValue, Owner, null);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Vessel of Poison"),
        ("description", "Attacks apply Poison.")
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
        new HealVar(4m)
    };

    public RegenerativeSalve() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
    }

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(2m);

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
        new CardsVar(2)
    };

    public DarkStarGaze() : base(0, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
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
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
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
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        new HealVar(5m)
    };

    public QuickRecovery() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
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
        new HealVar(3m),
        new CardsVar(1)
    };

    public SeaBreeze() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() { DynamicVars.Heal.UpgradeValueBy(2m); DynamicVars.Cards.UpgradeValueBy(1m); }

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
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
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
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        new CardsVar(3)
    };

    public ConstellationDraw() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
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
        new HealVar(2m)
    };

    public AegirResilience() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        new HealVar(8m)
    };
    public SelfReconstitution() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this);
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
        new HealVar(10m)
    };
    public HealingSprings() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_spell_cast").Execute(c);
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
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<RegenerationPower>(Owner.Creature, 1m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationPower>(Owner.Creature, 1m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_spell_cast").Execute(c);
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
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, 3m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        Creature? unit = ThornsAlchemy.Units(Owner.Creature.CombatState).FirstOrDefault();
        if (unit != null)
        {
            await ThornsAlchemy.Release(c, unit, this);
            await CreatureCmd.Kill(unit);
            return;
        }

        await ThornsAlchemy.SummonUnit(c, Owner, this);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer && !enemy.HasPower<AlchemyUnitPower>())
            {
                await PowerCmd.Apply<AccelerantPower>(enemy, 1m, Owner.Creature, this);
            }
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonPower>(p.Target, 3m, Owner.Creature, this);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
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
        await PowerCmd.Apply<ThornsPower>(Owner.Creature, 2m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
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
        new HealVar(12m)
    };
    public SeafoamHealing() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
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
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(enemy)
                    .WithHitFx("vfx/vfx_spell_cast").Execute(c);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<AlchemicalBrewPower>(Owner.Creature, 1m, Owner.Creature, this);
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
        new PowerVar<StrengthPower>(5m), new PowerVar<DexterityPower>(5m)
    };
    public ConstellationLegacy() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }
    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Strength.UpgradeValueBy(2m); DynamicVars.Dexterity.UpgradeValueBy(2m); }
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationInsightPower>(Owner.Creature, 1m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<AbyssalPoisonPower>(Owner.Creature, 1m, Owner.Creature, this);
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
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationInsightPower>(Owner.Creature, 2m, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "自由的第三面"),
        ("description", "每回合第一次你将击破炼金单元时，选择：改为触发脉冲；或释放后重新召唤1个炼金单元。")
    };
}
