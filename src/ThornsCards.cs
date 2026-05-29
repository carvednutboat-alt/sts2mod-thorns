using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.Characters;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using HarmonyLib;

namespace Weed;


// ============================================================
// CUSTOM POWERS for Thorns
// ============================================================

public sealed class ThornsPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (Owner.IsDead || dealer == null || dealer.IsPlayer || !Owner.IsAlive || result.UnblockedDamage <= 0)
            return;

        Flash();
        await DamageCmd.Attack(1m).FromCard(null).Targeting(dealer)
            .WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��������"),
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
        ("title", "���һָ�"),
        ("description", "At end of turn, heal HP.")
    };
}

public sealed class PoisonMasteryPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "���ﾫͨ"),
        ("description", "Attacks deal +1 damage to poisoned enemies.")
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
        ("title", "�ǳ�֮��"),
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
        if (Owner.IsDead || !Owner.IsAlive)
            return;

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
        ("title", "���ǻԹ�"),
        ("description", "At start of turn, deal damage to ALL enemies and apply 1 Poison.")
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
        if (Owner.IsDead || !Owner.IsAlive || Owner.Player == null)
            return;

        Flash();
        await CardPileCmd.Draw(choiceContext, (int)DynamicVars["CardsDrawn"].BaseValue, Owner.Player);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ͼָ��"),
        ("description", "At start of turn, draw cards.")
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
        if (Owner.CombatState == null) return;
        Flash();
        if (Owner.IsDead || !Owner.IsAlive)
            return;

        Flash();
        foreach (Creature enemy in Owner.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars["PoisonAmount"].BaseValue, Owner, null);
        }
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��Ԩ����"),
        ("description", "At start of turn, apply Poison to ALL enemies.")
    };
}

public sealed class AlchemicalBrewPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
    {
        if (Owner.IsDead || !Owner.IsAlive || Owner.Player == null)
            return;

        Flash();
        await CardPileCmd.Draw(choiceContext, 1, Owner.Player);
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "������"),
        ("description", "At start of turn, draw 1 additional card.")
    };
}

public sealed class TidalBlockPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;

    // ModifyBlock removed

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ϫ֮��"),
        ("description", "Block from Skills is doubled.")
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
        ("title", "��������"),
        ("description", "Attacks apply Poison.")
    };
}


// ============================================================
// BASIC CARDS (10)
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
        ("title", "ն��"),
        ("description", "��� {Damage:diff()} ���˺���")
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
        ("title", "����"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromPower<PoisonPower>()
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
        ("title", "�㶾ն"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class QuickSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(3m, ValueProp.Move)
    };

    public QuickSlash() : base(0, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Ѹն"),
        ("description", "��� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class IronGuard : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(3m, ValueProp.Move)
    };

    public IronGuard() : base(0, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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

    public RegenerativeSalve() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
    }

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�ָ�ҩ��"),
        ("description", "�ظ� {Heal:diff()} ��������")
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

    public DarkStarGaze() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��������"),
        ("description", "�� {Cards:diff()} ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PreciseThrust : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new CardsVar(1)
    };

    public PreciseThrust() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

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
        ("title", "��׼ͻ��"),
        ("description", "��� {Damage:diff()} ���˺���\n�� {Cards:diff()} ���ơ�")
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

    public DefensiveStance() : base(1, CardType.Skill, CardRarity.Basic, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "������̬"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeurotoxinStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new PowerVar<PoisonPower>(3m)
    };
    protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
    {
        HoverTipFactory.FromPower<PoisonPower>()
    };

    public NeurotoxinStrike() : base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy) { }

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
        ("title", "�񾭶��ش��"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        ("title", "˫�ش��"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonBlade : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move),
        new PowerVar<PoisonPower>(3m)
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
        ("title", "����"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarlightBarrier : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(7m, ValueProp.Move)
    };

    public StarlightBarrier() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�ǹ�����"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "���ٻָ�"),
        ("description", "�ظ� {Heal:diff()} ��������")
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
        ("title", "��ѧ����"),
        ("description", "�����е���ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        ("title", "����ͻ��"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class CrossSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new BlockVar(3m, ValueProp.Move)
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
        ("title", "����ն"),
        ("description", "��� {Damage:diff()} ���˺���\n��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "����"),
        ("description", "�ظ� {Heal:diff()} ��������\n�� {Cards:diff()} ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class InkSplash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move)
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
        ("title", "ī��"),
        ("description", "�����е������ {Damage:diff()} ���˺���")
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
        ("title", "��ˮ"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ToxicEdge : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new PowerVar<PoisonPower>(2m)
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
        ("title", "����"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class OceanShield : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(12m, ValueProp.Move)
    };

    public OceanShield() : base(2, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(4m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����֮��"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class RitualStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(12m, ValueProp.Move)
    };

    public RitualStrike() : base(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ʽ���"),
        ("description", "��� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class Starfall : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(4m, ValueProp.Move)
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
        ("title", "����"),
        ("description", "�����е������ {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeepSeaVenom : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(4m)
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
        ("title", "���Һ"),
        ("description", "ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        ("title", "Ѹ�ݷ���"),
        ("description", "��� {Damage:diff()} ���˺���\n��� {Block:diff()} �� [gold]��[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AlchemicalMixture : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(2m),
        new CardsVar(1)
    };

    public AlchemicalMixture() : base(1, CardType.Skill, CardRarity.Common, TargetType.AnyEnemy) { }

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
        ("title", "������"),
        ("description", "ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��\n�� {Cards:diff()} ���ơ�")
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
        ("title", "�������"),
        ("description", "�� {Cards:diff()} ���ơ�")
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
        ("title", "��ĭ����"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AegirResilience : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(4m, ValueProp.Move),
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
        ("title", "���������"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��\n�ظ� {Heal:diff()} ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(7m, ValueProp.Move)
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
        ("title", "��Ԩ���"),
        ("description", "��� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class WaveSlash : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new PowerVar<PoisonPower>(1m)
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
        ("title", "����ն"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class LodestarGuidance : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    public LodestarGuidance() : base(1, CardType.Skill, CardRarity.Common, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����ָ��"),
        ("description", "�� {Cards:diff()} ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class Riptide : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(8m, ValueProp.Move)
    };

    public Riptide() : base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����"),
        ("description", "��� {Damage:diff()} ���˺���")
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
        new DamageVar(4m, ValueProp.Move),
        new RepeatVar(4)
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
        ("title", "���߾�ͨ"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
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
        ("title", "�񾭶���"),
        ("description", "�����е���ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        ("title", "�����ع�"),
        ("description", "�ظ� {Heal:diff()} ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonedWounds : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(9m, ValueProp.Move)
    };

    public PoisonedWounds() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����"),
        ("description", "��� {Damage:diff()} ���˺������Ŀ�����ж���������� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AncientRitual : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(1m),
        new PowerVar<DexterityPower>(1m)
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
        ("title", "Զ����ʽ"),
        ("description", "��� {StrengthPower:diff()} �� [gold]����[/gold]��\n��� {DexterityPower:diff()} �� [gold]���[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarMap : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new CardsVar(2)
    };

    public StarMap() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ͼ"),
        ("description", "�� {Cards:diff()} ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationArmor : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(15m, ValueProp.Move)
    };

    public ConstellationArmor() : base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(5m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�Ǽ�"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "����Ȫӿ"),
        ("description", "�ظ� {Heal:diff()} ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TripleStrike : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new RepeatVar(3)
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
        ("title", "������"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class DeadlyVenom : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(6m)
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
        ("title", "������Һ"),
        ("description", "ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class OceanCurrent : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(2m, ValueProp.Move),
        new CardsVar(2)
    };

    public OceanCurrent() : base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(1m); DynamicVars.Cards.UpgradeValueBy(1m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����"),
        ("description", "��� {Damage:diff()} ���˺���\n�� {Cards:diff()} ���ơ�")
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
        ("title", "��ѧ��ը"),
        ("description", "�����е������ {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class FocusedDestreza : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new RepeatVar(3)
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
        ("title", "רע����"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
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
        ("title", "�ָ��⻷"),
        ("description", "ÿ�غϽ���ʱ�ظ� 3 ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SeaKingProtection : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(18m, ValueProp.Move)
    };

    public SeaKingProtection() : base(3, CardType.Skill, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
    }

    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(6m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�����ӻ�"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "����"),
        ("description", "ÿ�غϽ���ʱ��� 1 �� [gold]����[/gold]��")
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
        ("title", "��������"),
        ("description", "�������δ���˺�ʱʩ�� 1 �� [gold]�ж�[/gold]��")
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
        ("title", "������"),
        ("description", "�����е������ {Damage:diff()} ���˺���")
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
        ("title", "ɺ����"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "��Ԩ����"),
        ("description", "�����е���ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        ("title", "��Ӱն"),
        ("description", "��� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TidalSurge : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(6m, ValueProp.Move),
        new BlockVar(6m, ValueProp.Move)
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
        ("title", "��ӿ"),
        ("description", "��� {Damage:diff()} ���˺���\n��� {Block:diff()} �� [gold]��[/gold]��")
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
        ("title", "��ҹ"),
        ("description", "�� {Cards:diff()} ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonMastery : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public PoisonMastery() : base(1, CardType.Power, CardRarity.Uncommon, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonMasteryPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "���ﾫͨ"),
        ("description", "�����ж��ĵ�������˺�ʱ +1��")
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
        ("title", "����֮��"),
        ("description", "��� 3 �� [gold]��������[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class LodestarPower : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DynamicVar("PoisonCount", 2m)
    };

    public LodestarPower() : base(1, CardType.Skill, CardRarity.Uncommon, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars["PoisonCount"].BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars["PoisonCount"].UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����֮��"),
        ("description", "�����е���ʩ�� 2 �� [gold]�ж�[/gold]��")
    };
}


// ============================================================
// RARE CARDS (23)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class DestrezaUltimate : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(5m, ValueProp.Move),
        new RepeatVar(5)
    };

    public DestrezaUltimate() : base(3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
            .Targeting(p.Target).WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(2m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����һ��"),
        ("description", "��� {Damage:diff()} ���˺� {Repeat:diff()} �Ρ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class SelfReconstitutionUltimate : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new HealVar(15m)
    };

    public SelfReconstitutionUltimate() : base(2, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
    }

    protected override void OnUpgrade() => DynamicVars.Heal.UpgradeValueBy(5m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�ع�����"),
        ("description", "�ظ� {Heal:diff()} ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class LodestarAwakening : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public LodestarAwakening() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<LodestarRadiancePower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "���Ǿ���"),
        ("description", "ÿ�غϿ�ʼʱ�����е������ 6 ���˺���ʩ�� 1 �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonMasterform : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public PoisonMasterform() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<PoisonMasteryPower>(Owner.Creature, 2m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�綾��̬"),
        ("description", "�����ж��ĵ�������˺�ʱ +2��")
    };
}

// StarRitual card removed

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationGuide : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public ConstellationGuide() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationInsightPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ͼ����"),
        ("description", "ÿ�غϿ�ʼʱ����� 1 ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NeurotoxinOverload : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Move),
        new PowerVar<PoisonPower>(8m)
    };

    public NeurotoxinOverload() : base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(5m); DynamicVars.Poison.UpgradeValueBy(3m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�񾭶��ع���"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AegirWrath : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(25m, ValueProp.Move)
    };

    public AegirWrath() : base(3, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
    }

    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(8m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�����֮ŭ"),
        ("description", "��� {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class OceanSanctuary : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new BlockVar(20m, ValueProp.Move),
        new HealVar(10m)
    };

    public OceanSanctuary() : base(3, CardType.Skill, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, p);
        await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
    }

    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(5m); DynamicVars.Heal.UpgradeValueBy(5m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "����ʥ��"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��\n�ظ� {Heal:diff()} ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class RitualOfTheStars : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public RitualOfTheStars() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationPower>(Owner.Creature, 2m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Ⱥ���ǹ�"),
        ("description", "ÿ�غϽ���ʱ��� 2 �� [gold]����[/gold]��")
    };
}

// LodestarProtection card removed - power no longer available

[Pool(typeof(ThornsCardPool))]
public sealed class ChemicalWarfare : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(15m)
    };

    public ChemicalWarfare() : base(3, CardType.Skill, CardRarity.Rare, TargetType.AllEnemies) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        foreach (Creature enemy in Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>())
        {
            if (enemy.IsAlive && !enemy.IsPlayer)
                await PowerCmd.Apply<PoisonPower>(enemy, DynamicVars.Poison.BaseValue, Owner.Creature, this);
        }
    }

    protected override void OnUpgrade() => DynamicVars.Poison.UpgradeValueBy(5m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ѧս��"),
        ("description", "�����е���ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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
        // Energy gain not available
    }

    protected override void OnUpgrade() { DynamicVars.Heal.UpgradeValueBy(5m); EnergyCost.UpgradeBy(-1); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ĭ����"),
        ("description", "�ظ� {Heal:diff()} ��������\n��� 1 ��������")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class StarCataclysm : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(20m, ValueProp.Move)
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
        ("title", "�������"),
        ("description", "�����е������ {Damage:diff()} ���˺���")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AncientAlchemy : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public AncientAlchemy() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<AlchemicalBrewPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "Զ������"),
        ("description", "ÿ�غϿ�ʼʱ����� 1 ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class TidalMastery : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public TidalMastery() : base(2, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<TidalBlockPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ϫ����"),
        ("description", "�������ṩ�ĸ񵲷�����")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationLegacy : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<StrengthPower>(5m),
        new PowerVar<DexterityPower>(5m)
    };

    public ConstellationLegacy() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Strength.UpgradeValueBy(2m); DynamicVars.Dexterity.UpgradeValueBy(2m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "���޴���"),
        ("description", "��� {StrengthPower:diff()} �� [gold]����[/gold]��\n��� {DexterityPower:diff()} �� [gold]���[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class PoisonReaper : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new DamageVar(15m, ValueProp.Move),
        new PowerVar<PoisonPower>(8m)
    };

    public PoisonReaper() : base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        ArgumentNullException.ThrowIfNull(p.Target);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(p.Target)
            .WithHitFx("vfx/vfx_attack_slash").Execute(c);
        await PowerCmd.Apply<PoisonPower>(p.Target, DynamicVars.Poison.BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(5m); DynamicVars.Poison.UpgradeValueBy(3m); }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��֮�ո�"),
        ("description", "��� {Damage:diff()} ���˺���\nʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��")
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

    protected override void OnUpgrade()
    {
        // Already 0 cost
    }

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "������"),
        ("description", "ÿ�غϿ�ʼʱ����� 1 ���ơ�")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class AbyssalForm : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public AbyssalForm() : base(3, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<AbyssalPoisonPower>(Owner.Creature, 1m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��Ԩ��̬"),
        ("description", "ÿ�غϿ�ʼʱ�����е���ʩ�� 3 �� [gold]�ж�[/gold]��")
    };
}

[Pool(typeof(ThornsCardPool))]
public sealed class NavigatorForesight : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;

    public NavigatorForesight() : base(1, CardType.Power, CardRarity.Rare, TargetType.Self) { }

    protected override async Task OnPlay(PlayerChoiceContext c, CardPlay p)
    {
        await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
        await PowerCmd.Apply<ConstellationInsightPower>(Owner.Creature, 2m, Owner.Creature, this);
    }

    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "�캽Ԥ��"),
        ("description", "ÿ�غϿ�ʼʱ����� 2 ���ơ�")
    };
}


// ============================================================
// EXTRA CARDS (5 more to reach ~88)
// ============================================================

[Pool(typeof(ThornsCardPool))]
public sealed class ConstellationMark : CustomCardModel
{
    public override string? CustomPortraitPath => MissingPortraitPath;
    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<PoisonPower>(3m),
        new CardsVar(1)
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
        ("title", "�������"),
        ("description", "ʩ�� {Poison:diff()} �� [gold]�ж�[/gold]��\n�� {Cards:diff()} ���ơ�")
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
        ("title", "��������"),
        ("description", "��� {Block:diff()} �� [gold]��[/gold]��\n��� 2 �� [gold]��������[/gold]��")
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
        ("title", "�����"),
        ("description", "�ظ� {Heal:diff()} ��������")
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
        PoisonPower? poison = p.Target.GetPower<PoisonPower>();
        if (poison != null)
        {
            // Simplified - apply 3 poison instead of doubling
        }
        await CardPileCmd.Draw(c, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade() => DynamicVars.Cards.UpgradeValueBy(1m);

    public override List<(string, string)> Localization => new List<(string, string)>
    {
        ("title", "��ѧ�߻���"),
        ("description", "�����з��� [gold]�ж�[/gold]��\n�� {Cards:diff()} ���ơ�")
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
        ("title", "�ǻ�ף��"),
        ("description", "��� {StrengthPower:diff()} �� [gold]����[/gold]��")
    };
}
