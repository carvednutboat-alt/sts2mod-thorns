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
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace Weed;

public sealed class WeedCardPool : CustomCardPoolModel
{
	public override string Title => "reed";

	public override string EnergyColorName => "ironclad";

	public override Color DeckEntryCardColor => new Color("4B8F3A");

	public override Color ShaderColor => new Color("4B8F3A");

	public override bool IsColorless => false;
}

public sealed class WeedRelicPool : CustomRelicPoolModel
{
	public override string EnergyColorName => "ironclad";

	public override Color LabOutlineColor => new Color("4B8F3A");
}

public sealed class Herbalist : PlaceholderCharacterModel
{
	private const string HerbalistVisualPath = "res://scenes/creature_visuals/herbalist.tscn";
	private const string HerbalistMerchantVisualPath = "res://scenes/merchant/characters/herbalist_merchant.tscn";
	private const string HerbalistRestSiteVisualPath = "res://scenes/rest_site/characters/herbalist_rest_site.tscn";
	private const string HerbalistCharacterSelectBgPath = "res://scenes/screens/char_select/char_select_bg_herbalist.tscn";
	private const string HerbalistCharacterSelectIconPath = "packed/character_select/char_select_reed.png";
	private const string HerbalistCharacterSelectLockedIconPath = "packed/character_select/char_select_reed_locked.png";
	private const string HerbalistAtlasPath = "res://animations/characters/herbalist/char_1020_reed2.atlas";
	private const string HerbalistAtlasTexturePath = "res://animations/characters/herbalist/char_1020_reed2.png";
	private const string HerbalistSkeletonPath = "res://animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2.skel";

	public override string PlaceholderID => "ironclad";

	public override string CustomVisualPath => CanLoadHerbalistVisual() ? HerbalistVisualPath : base.CustomVisualPath;

	public override string CustomMerchantAnimPath => CanLoadHerbalistVisual() && ResourceLoader.Exists(HerbalistMerchantVisualPath) ? HerbalistMerchantVisualPath : base.CustomMerchantAnimPath;

	public override string CustomRestSiteAnimPath => CanLoadHerbalistVisual() && ResourceLoader.Exists(HerbalistRestSiteVisualPath) ? HerbalistRestSiteVisualPath : base.CustomRestSiteAnimPath;

	public override string CustomCharacterSelectBg => CanLoadHerbalistVisual() && ResourceLoader.Exists(HerbalistCharacterSelectBgPath) ? HerbalistCharacterSelectBgPath : base.CustomCharacterSelectBg;

	protected override string CharacterSelectIconPath => ImageHelper.GetImagePath(HerbalistCharacterSelectIconPath);

	protected override string CharacterSelectLockedIconPath => ImageHelper.GetImagePath(HerbalistCharacterSelectLockedIconPath);

	private static bool CanLoadHerbalistVisual()
	{
		if (!ResourceLoader.Exists(HerbalistVisualPath) ||
		    !FileAccess.FileExists(HerbalistAtlasPath) ||
		    !FileAccess.FileExists(HerbalistSkeletonPath))
		{
			return false;
		}

		try
		{
			return ResourceLoader.Load<Texture2D>(HerbalistAtlasTexturePath) != null;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
	{
		if (!controller.HasAnimation("Idle"))
		{
			return null;
		}

		return SetupAnimationState(controller, idleName: "Idle", deadName: "Die", attackName: "Attack", castName: "Skill_2");
	}

	public override CharacterGender Gender => CharacterGender.Neutral;

	public override Color NameColor => new Color("6FBF4A");

	public override int StartingHp => 72;

	public override CardPoolModel CardPool => ModelDb.CardPool<WeedCardPool>();

	public override RelicPoolModel RelicPool => ModelDb.RelicPool<WeedRelicPool>();

	public override PotionPoolModel PotionPool => ModelDb.PotionPool<SharedPotionPool>();

	public override IEnumerable<CardModel> StartingDeck => new CardModel[]
	{
		ModelDb.Card<Strike>(),
		ModelDb.Card<Strike>(),
		ModelDb.Card<Strike>(),
		ModelDb.Card<Strike>(),
		ModelDb.Card<Defend>(),
		ModelDb.Card<Defend>(),
		ModelDb.Card<Defend>(),
		ModelDb.Card<Defend>(),
		ModelDb.Card<HerbalGuard>(),
		ModelDb.Card<Scorch>()
	};

	public override IReadOnlyList<RelicModel> StartingRelics => new RelicModel[]
	{
		ModelDb.Relic<SeedCache>()
	};

	public override Color DialogueColor => new Color("2F4F2A");

	public override Color MapDrawingColor => new Color("4B8F3A");

	public override Color RemoteTargetingLineColor => new Color("6FBF4A");

	public override Color RemoteTargetingLineOutline => new Color("24451E");

	public override List<string> GetArchitectAttackVfx()
	{
		return ModelDb.Character<Ironclad>().GetArchitectAttackVfx();
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Reed"),
		("titleObject", "Reed"),
		("description", "A prototype character for the Reed mod.\nUses plant-based starter cards."),
		("cardsModifierTitle", "Reed Cards"),
		("cardsModifierDescription", "Reed cards will now appear in rewards and shops."),
		("eventDeathPrevention", "The roots still hold."),
		("possessiveAdjective", "their"),
		("pronounObject", "them"),
		("pronounPossessive", "theirs"),
		("pronounSubject", "they"),
		("unlockText", "Available from the Reed mod.")
	};
}

[HarmonyPatch(typeof(CharacterModel), "get_IconTexture")]
internal static class ReedCharacterIconTexturePatch
{
	private const string IconPath = "res://images/packed/ui/top_panel/character_icon_reed.png";

	private static void Postfix(CharacterModel __instance, ref Texture2D __result)
	{
		if (__instance is not Herbalist)
		{
			return;
		}

		Texture2D? texture = ResourceLoader.Load<Texture2D>(IconPath, null, ResourceLoader.CacheMode.Reuse);
		if (texture != null)
		{
			__result = texture;
		}
	}
}

[HarmonyPatch(typeof(CharacterModel), "get_IconOutlineTexture")]
internal static class ReedCharacterIconOutlineTexturePatch
{
	private const string IconOutlinePath = "res://images/packed/ui/top_panel/character_icon_reed_outline.png";

	private static void Postfix(CharacterModel __instance, ref Texture2D __result)
	{
		if (__instance is not Herbalist)
		{
			return;
		}

		Texture2D? texture = ResourceLoader.Load<Texture2D>(IconOutlinePath, null, ResourceLoader.CacheMode.Reuse);
		if (texture != null)
		{
			__result = texture;
		}
	}
}

[HarmonyPatch(typeof(NMerchantCharacter), "_Ready")]
internal static class ReedMerchantCharacterReadyPatch
{
	private static bool Prefix(NMerchantCharacter __instance)
	{
		if (__instance.Name != "ReedMerchant")
		{
			return true;
		}

		PlayIdleOnFirstSpineChild(__instance);
		return false;
	}

	private static void PlayIdleOnFirstSpineChild(Node node)
	{
		if (node.GetChildCount() == 0)
		{
			return;
		}

		Node child = node.GetChild(0);
		if (child.GetClass() != "SpineSprite")
		{
			return;
		}

		MegaSprite sprite = new MegaSprite(child);
		if (sprite.HasAnimation("Idle"))
		{
			sprite.GetAnimationState().SetAnimation("Idle", true, 0);
		}
	}
}

[HarmonyPatch(typeof(NRestSiteCharacter), "_Ready")]
internal static class ReedRestSiteCharacterReadyPatch
{
	private static void Postfix(NRestSiteCharacter __instance)
	{
		if (__instance.Name != "ReedRestSite")
		{
			return;
		}

		foreach (Node child in __instance.GetChildren())
		{
			if (child.GetClass() != "SpineSprite")
			{
				continue;
			}

			MegaSprite sprite = new MegaSprite(child);
			if (sprite.HasAnimation("Idle"))
			{
				sprite.GetAnimationState().SetAnimation("Idle", true, 0);
			}
		}
	}
}

[HarmonyPatch(typeof(TheArchitect), "DefineDialogues")]
internal static class TheArchitectDefineDialoguesPatch
{
	private static void Postfix(AncientDialogueSet __result)
	{
		string herbalistKey = ModelDb.Character<Herbalist>().Id.Entry;
		if (__result.CharacterDialogues.ContainsKey(herbalistKey))
		{
			return;
		}

		__result.CharacterDialogues[herbalistKey] = new AncientDialogue[]
		{
			new AncientDialogue("")
			{
				VisitIndex = 0,
				IsRepeating = true,
				EndAttackers = ArchitectAttackers.None
			}
		};
	}
}

[HarmonyPatch(typeof(NTreasureRoom), "OnProceedButtonPressed")]
internal static class HerbalistTreasureSkipPatch
{
	private static bool Prefix(NTreasureRoom __instance)
	{
		IRunState? runState = AccessTools.Field(typeof(NTreasureRoom), "_runState")?.GetValue(__instance) as IRunState;
		NProceedButton? proceedButton = AccessTools.Field(typeof(NTreasureRoom), "_proceedButton")?.GetValue(__instance) as NProceedButton;
		if (runState == null || proceedButton?.IsSkip != true)
		{
			return true;
		}

		Player? player = LocalContext.GetMe(runState);
		if (player?.Character is Herbalist && RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics?.Count > 0)
		{
			return false;
		}

		return true;
	}
}

[Pool(typeof(WeedCardPool))]
public sealed class Strike : CustomCardModel
{
	public override string? CustomPortraitPath => ImageHelper.GetImagePath("packed/card_portraits/weed/strike.png");

	protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Strike };

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DamageVar(6m, ValueProp.Move)
	};

	public Strike()
		: base(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		ArgumentNullException.ThrowIfNull(cardPlay.Target);
		await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(cardPlay.Target)
			.WithHitFx("vfx/vfx_attack_slash")
			.Execute(choiceContext);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Damage.UpgradeValueBy(3m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Strike"),
		("description", "Deal {Damage:diff()} damage.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class Defend : CustomCardModel
{
	public override string? CustomPortraitPath => ImageHelper.GetImagePath("packed/card_portraits/weed/defend.png");

	protected override HashSet<CardTag> CanonicalTags => new HashSet<CardTag> { CardTag.Defend };

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(5m, ValueProp.Move)
	};

	public Defend()
		: base(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(3m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Defend"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class HerbalGuard : CustomCardModel
{
	public override string? CustomPortraitPath => ImageHelper.GetImagePath("packed/card_portraits/weed/herbal_guard.png");

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(6m, ValueProp.Move),
		new CardsVar(1)
	};

	public HerbalGuard()
		: base(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(3m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Herbal Guard"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].\nDraw {Cards:diff()} card.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class LeafCut : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DamageVar(3m, ValueProp.Move),
		new RepeatVar(3)
	};

	public LeafCut()
		: base(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		ArgumentNullException.ThrowIfNull(cardPlay.Target);
		await DamageCmd.Attack(DynamicVars.Damage.BaseValue).WithHitCount(DynamicVars.Repeat.IntValue).FromCard(this)
			.Targeting(cardPlay.Target)
			.WithHitFx("vfx/vfx_attack_slash")
			.Execute(choiceContext);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Repeat.UpgradeValueBy(1m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Leaf Cut"),
		("description", "Deal {Damage:diff()} damage {Repeat:diff()} times.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class RootWall : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(8m, ValueProp.Move)
	};

	public RootWall()
		: base(1, CardType.Skill, CardRarity.Common, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(3m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Root Wall"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class QuickSprout : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(3m, ValueProp.Move),
		new CardsVar(1)
	};

	public QuickSprout()
		: base(0, CardType.Skill, CardRarity.Common, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(2m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Quick Sprout"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].\nDraw {Cards:diff()} card.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class VineSnare : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DamageVar(10m, ValueProp.Move),
		new BlockVar(5m, ValueProp.Move)
	};

	public VineSnare()
		: base(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		ArgumentNullException.ThrowIfNull(cardPlay.Target);
		await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(cardPlay.Target)
			.WithHitFx("vfx/vfx_attack_slash")
			.Execute(choiceContext);
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Damage.UpgradeValueBy(3m);
		DynamicVars.Block.UpgradeValueBy(2m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Vine Snare"),
		("description", "Deal {Damage:diff()} damage.\nGain {Block:diff()} [gold]Block[/gold].")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class HerbalRemedy : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new HealVar(3m),
		new CardsVar(1)
	};

	public HerbalRemedy()
		: base(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Heal.UpgradeValueBy(2m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Herbal Remedy"),
		("description", "Heal {Heal:diff()} HP.\nDraw {Cards:diff()} card.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class Canopy : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(14m, ValueProp.Move),
		new CardsVar(1)
	};

	public Canopy()
		: base(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(4m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Canopy"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].\nDraw {Cards:diff()} card.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class HarvestBlow : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DamageVar(18m, ValueProp.Move),
		new HealVar(2m)
	};

	public HarvestBlow()
		: base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		ArgumentNullException.ThrowIfNull(cardPlay.Target);
		await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(cardPlay.Target)
			.WithHitFx("vfx/vfx_attack_slash")
			.Execute(choiceContext);
		await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Damage.UpgradeValueBy(6m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Harvest Blow"),
		("description", "Deal {Damage:diff()} damage.\nHeal {Heal:diff()} HP.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class VerdantWall : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new BlockVar(20m, ValueProp.Move),
		new CardsVar(2)
	};

	public VerdantWall()
		: base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, cardPlay);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Block.UpgradeValueBy(6m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Verdant Wall"),
		("description", "Gain {Block:diff()} [gold]Block[/gold].\nDraw {Cards:diff()} cards.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class OvergrowthBurst : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new CardsVar(3)
	};

	public OvergrowthBurst()
		: base(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Cards.UpgradeValueBy(1m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Overgrowth Burst"),
		("description", "Draw {Cards:diff()} cards.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class BloomReaper : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DamageVar(14m, ValueProp.Move),
		new CardsVar(1)
	};

	public BloomReaper()
		: base(2, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		ArgumentNullException.ThrowIfNull(cardPlay.Target);
		await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this).Targeting(cardPlay.Target)
			.WithHitFx("vfx/vfx_attack_slash")
			.Execute(choiceContext);
		await CardPileCmd.Draw(choiceContext, DynamicVars.Cards.BaseValue, Owner);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Damage.UpgradeValueBy(5m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Bloom Reaper"),
		("description", "Deal {Damage:diff()} damage.\nDraw {Cards:diff()} card.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class Scorch : CustomCardModel
{
	public override string? CustomPortraitPath => ImageHelper.GetImagePath("packed/card_portraits/weed/scorch.png");

	public Scorch()
		: base(2, CardType.Power, CardRarity.Basic, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
		if (!Owner.Creature.HasPower<SpreadingSporesPower>())
		{
			await PowerCmd.Apply<SpreadingSporesPower>(Owner.Creature, 1m, Owner.Creature, this);
		}
	}

	protected override void OnUpgrade()
	{
		EnergyCost.UpgradeBy(-1);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "灼痕"),
		("description", "Gain [gold]传播灼痕[/gold].")
	};
}

public sealed class SpreadingSporesPower : CustomPowerModel
{
	private const float ProcChance = 0.3f;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new PowerVar<ScorchPower>(1m)
	};

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
	{
		HoverTipFactory.FromPower<ScorchPower>()
	};

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		bool lifeSparkActive = Owner.HasPower<LifeSparkPower>();
		if (Owner.IsDead || dealer != Owner || target.IsPlayer || result.UnblockedDamage <= 0 || (!props.IsPoweredAttack() && !lifeSparkActive))
		{
			return;
		}

		if (lifeSparkActive || Owner.Player!.RunState.Rng.Niche.NextFloat() < ProcChance)
		{
			Flash();
			await PowerCmd.Apply<ScorchPower>(target, DynamicVars["ScorchPower"].BaseValue, Owner, null);
		}
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "传播灼痕"),
		("description", "Whenever you deal unblocked attack damage, there is a 30% chance to apply 1 [gold]灼痕[/gold]. While [gold]生命火种[/gold] is active, this chance is 100% and can trigger from your unblocked damage.")
	};
}

public sealed class ScorchPower : CustomPowerModel
{
	private const string DamageTakenIncrease = "DamageTakenIncrease";

	private const string AttackDamageDecrease = "AttackDamageDecrease";

	public override PowerType Type => PowerType.Debuff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DynamicVar(DamageTakenIncrease, 30m),
		new DynamicVar(AttackDamageDecrease, 20m)
	};

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (!props.IsPoweredAttack())
		{
			return 1m;
		}

		if (target == Owner)
		{
			return 1m + DynamicVars[DamageTakenIncrease].BaseValue / 100m;
		}

		if (dealer == Owner)
		{
			return (100m - DynamicVars[AttackDamageDecrease].BaseValue) / 100m;
		}

		return 1m;
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == Owner.Side)
		{
			await PowerCmd.TickDownDuration(this);
		}
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "灼痕"),
		("description", "Take 30% more attack damage and deal 20% less attack damage.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class LifeSpark : CustomCardModel
{
	private const string ScorchHpLoss = "ScorchHpLoss";

	private const string ExplosionPercent = "ExplosionPercent";

	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override bool IsPlayable => Owner.Creature.HasPower<SpreadingSporesPower>();

	protected override bool ShouldGlowGoldInternal => IsPlayable;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new PowerVar<LifeSparkPower>(2m),
		new PowerVar<StrengthPower>(3m),
		new DynamicVar(ScorchHpLoss, 2m),
		new DynamicVar(ExplosionPercent, 20m)
	};

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
	{
		HoverTipFactory.FromPower<LifeSparkPower>(),
		HoverTipFactory.FromPower<SpreadingSporesPower>(),
		HoverTipFactory.FromPower<ScorchPower>(),
		HoverTipFactory.FromPower<StrengthPower>()
	};

	public LifeSpark()
		: base(2, CardType.Skill, CardRarity.Rare, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
		await PowerCmd.Apply<LifeSparkPower>(Owner.Creature, DynamicVars["LifeSparkPower"].BaseValue, Owner.Creature, this);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Strength.UpgradeValueBy(2m);
		DynamicVars[ScorchHpLoss].UpgradeValueBy(1m);
		DynamicVars[ExplosionPercent].UpgradeValueBy(10m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "生命火种"),
		("description", "Requires [gold]传播灼痕[/gold]. Gain {LifeSparkPower:diff()} [gold]生命火种[/gold]. While active, gain {StrengthPower:diff()} [gold]Strength[/gold], copy your attack damage to another enemy, make [gold]传播灼痕[/gold] always trigger, and make [gold]灼痕[/gold] burn enemies for {ScorchHpLoss:diff()} HP. Scorched enemies explode for {ExplosionPercent}% max HP on death. When this ends, remove all [gold]灼痕[/gold].")
	};
}

public sealed class LifeSparkPower : CustomPowerModel
{
	private const string ScorchHpLoss = "ScorchHpLoss";

	private const string ExplosionPercent = "ExplosionPercent";

	private const int DefaultStrength = 3;

	private const int DefaultScorchHpLoss = 2;

	private const int DefaultExplosionPercent = 20;

	private class Data
	{
		public int StrengthApplied;

		public int ScorchHpLoss = DefaultScorchHpLoss;

		public int ExplosionPercent = DefaultExplosionPercent;

		public readonly HashSet<Creature> ExplodedCreatures = new HashSet<Creature>();
	}

	private static bool _isCopyingAttack;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Counter;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new PowerVar<StrengthPower>(DefaultStrength),
		new DynamicVar(ScorchHpLoss, DefaultScorchHpLoss),
		new DynamicVar(ExplosionPercent, DefaultExplosionPercent)
	};

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
	{
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<ScorchPower>()
	};

	protected override object InitInternalData()
	{
		return new Data();
	}

	public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
	{
		await RefreshValues(cardSource);
	}

	public override async Task AfterPowerAmountChanged(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
	{
		if (power == this && amount > 0m)
		{
			await RefreshValues(cardSource);
		}
	}

	public override async Task AfterRemoved(Creature oldOwner)
	{
		Data data = GetInternalData<Data>();
		if (data.StrengthApplied != 0)
		{
			await PowerCmd.Apply<StrengthPower>(oldOwner, -data.StrengthApplied, oldOwner, null, silent: true);
			data.StrengthApplied = 0;
		}

		CombatState? combatState = oldOwner.CombatState;
		if (combatState == null)
		{
			return;
		}

		List<ScorchPower> scorchPowers = combatState.Creatures
			.SelectMany(creature => creature.Powers.OfType<ScorchPower>())
			.ToList();
		if (scorchPowers.Count > 0)
		{
			Flash();
		}

		foreach (ScorchPower scorchPower in scorchPowers)
		{
			await PowerCmd.Remove(scorchPower);
		}
	}

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (_isCopyingAttack || Owner.IsDead || dealer != Owner || target.IsPlayer || cardSource?.Type != CardType.Attack || result.TotalDamage <= 0)
		{
			return;
		}

		Creature? copyTarget = Owner.Player!.RunState.Rng.CombatTargets.NextItem(CombatState.GetOpponentsOf(Owner)
			.Where(enemy => enemy.IsHittable && enemy != target));
		if (copyTarget == null)
		{
			return;
		}

		AttackCommand copyAttack;
		if (cardSource.DynamicVars.ContainsKey("CalculatedDamage"))
		{
			copyAttack = DamageCmd.Attack(cardSource.DynamicVars.CalculatedDamage);
		}
		else if (cardSource.DynamicVars.ContainsKey("Damage"))
		{
			copyAttack = DamageCmd.Attack(cardSource.DynamicVars.Damage.BaseValue);
		}
		else
		{
			return;
		}

		Flash();
		_isCopyingAttack = true;
		try
		{
			await copyAttack.FromCard(cardSource).Targeting(copyTarget)
				.WithHitFx("vfx/vfx_attack_slash")
				.Execute(choiceContext);
		}
		finally
		{
			_isCopyingAttack = false;
		}
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner == Owner.Player)
		{
			await TriggerScorchHpLoss(choiceContext);
		}
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == CombatSide.Enemy)
		{
			await TriggerScorchHpLoss(choiceContext);
		}

		if (side == Owner.Side)
		{
			await PowerCmd.TickDownDuration(this);
		}
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
	{
		if (wasRemovalPrevented || creature.Side == Owner.Side || !creature.HasPower<ScorchPower>())
		{
			return;
		}

		Data data = GetInternalData<Data>();
		if (!data.ExplodedCreatures.Add(creature))
		{
			return;
		}

		decimal explosionDamage = Math.Floor(creature.MaxHp * data.ExplosionPercent / 100m);
		if (explosionDamage <= 0m)
		{
			return;
		}

		List<Creature> targets = CombatState.GetOpponentsOf(Owner)
			.Where(enemy => enemy.IsAlive && enemy != creature)
			.ToList();
		if (targets.Count == 0)
		{
			return;
		}

		Flash();
		await CreatureCmd.Damage(choiceContext, targets, explosionDamage, DamageProps.nonCardHpLoss, Owner, null);
	}

	private async Task RefreshValues(CardModel? cardSource)
	{
		(int strength, int scorchHpLoss, int explosionPercent) = GetValues(cardSource);
		Data data = GetInternalData<Data>();
		if (scorchHpLoss > data.ScorchHpLoss)
		{
			data.ScorchHpLoss = scorchHpLoss;
		}

		if (explosionPercent > data.ExplosionPercent)
		{
			data.ExplosionPercent = explosionPercent;
		}

		if (strength <= data.StrengthApplied)
		{
			SyncDisplayVars(data);
			return;
		}

		int strengthDelta = strength - data.StrengthApplied;
		data.StrengthApplied = strength;
		SyncDisplayVars(data);
		await PowerCmd.Apply<StrengthPower>(Owner, strengthDelta, Owner, cardSource, silent: true);
	}

	private void SyncDisplayVars(Data data)
	{
		DynamicVars.Strength.BaseValue = data.StrengthApplied;
		DynamicVars[ScorchHpLoss].BaseValue = data.ScorchHpLoss;
		DynamicVars[ExplosionPercent].BaseValue = data.ExplosionPercent;
		InvokeDisplayAmountChanged();
	}

	private static (int Strength, int ScorchHpLoss, int ExplosionPercent) GetValues(CardModel? cardSource)
	{
		if (cardSource is LifeSpark lifeSpark)
		{
			return (
				(int)lifeSpark.DynamicVars.Strength.BaseValue,
				(int)lifeSpark.DynamicVars[ScorchHpLoss].BaseValue,
				(int)lifeSpark.DynamicVars[ExplosionPercent].BaseValue
			);
		}

		return (DefaultStrength, DefaultScorchHpLoss, DefaultExplosionPercent);
	}

	private async Task TriggerScorchHpLoss(PlayerChoiceContext choiceContext)
	{
		Data data = GetInternalData<Data>();
		List<Creature> scorchedEnemies = CombatState.GetOpponentsOf(Owner)
			.Where(enemy => enemy.IsAlive && enemy.HasPower<ScorchPower>())
			.ToList();
		if (scorchedEnemies.Count == 0)
		{
			return;
		}

		Flash();
		foreach (Creature enemy in scorchedEnemies)
		{
			if (enemy.IsAlive)
			{
				await CreatureCmd.Damage(choiceContext, enemy, data.ScorchHpLoss, DamageProps.nonCardHpLoss, Owner, null);
			}
		}
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "生命火种"),
		("description", "Gain 3 [gold]Strength[/gold] while active. Your attacks copy their damage to another enemy. Your [gold]传播灼痕[/gold] always triggers. Enemy [gold]灼痕[/gold] burns for 2 HP after your cards and enemy turns. Scorched enemies explode for 20% max HP on death. When this ends, remove all [gold]灼痕[/gold].")
	};
}


[Pool(typeof(WeedCardPool))]
public sealed class Gleam : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	public override CardMultiplayerConstraint MultiplayerConstraint => CardMultiplayerConstraint.MultiplayerOnly;

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[]
	{
		HoverTipFactory.FromPower<GleamPower>()
	};

	public Gleam()
		: base(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
		if (!Owner.Creature.HasPower<GleamPower>())
		{
			await PowerCmd.Apply<GleamPower>(Owner.Creature, 1m, Owner.Creature, this);
		}
	}

	protected override void OnUpgrade()
	{
		EnergyCost.UpgradeBy(-1);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "映耀"),
		("description", "Gain [gold]映耀[/gold].")
	};
}

public sealed class GleamPower : CustomPowerModel
{
	private class Data
	{
		public int LastCurrentHp;
	}

	private static bool _isSharingHp;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	protected override object InitInternalData()
	{
		return new Data();
	}

	public override Task AfterApplied(Creature? applier, CardModel? cardSource)
	{
		GetInternalData<Data>().LastCurrentHp = Owner.CurrentHp;
		return Task.CompletedTask;
	}

	public override async Task AfterCurrentHpChanged(Creature creature, decimal delta)
	{
		if (creature != Owner)
		{
			return;
		}

		Data data = GetInternalData<Data>();
		int hpGained = Owner.CurrentHp - data.LastCurrentHp;
		data.LastCurrentHp = Owner.CurrentHp;
		if (hpGained <= 0 || _isSharingHp)
		{
			return;
		}

		Flash();
		_isSharingHp = true;
		try
		{
			foreach (Creature teammate in CombatState.GetTeammatesOf(Owner))
			{
				if (teammate != Owner && teammate.IsAlive && teammate.IsPlayer)
				{
					await CreatureCmd.Heal(teammate, hpGained);
				}
			}
		}
		finally
		{
			_isSharingHp = false;
		}
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "映耀"),
		("description", "Whenever your HP increases, other players heal the same amount.")
	};
}

[Pool(typeof(WeedCardPool))]
public sealed class VerdantForm : CustomCardModel
{
	public override string? CustomPortraitPath => MissingPortraitPath;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new PowerVar<StrengthPower>(2m),
		new PowerVar<DexterityPower>(2m)
	};

	public VerdantForm()
		: base(2, CardType.Power, CardRarity.Rare, TargetType.Self)
	{
	}

	protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		await CreatureCmd.TriggerAnim(Owner.Creature, "Cast", Owner.Character.CastAnimDelay);
		await PowerCmd.Apply<StrengthPower>(Owner.Creature, DynamicVars.Strength.BaseValue, Owner.Creature, this);
		await PowerCmd.Apply<DexterityPower>(Owner.Creature, DynamicVars.Dexterity.BaseValue, Owner.Creature, this);
	}

	protected override void OnUpgrade()
	{
		DynamicVars.Strength.UpgradeValueBy(1m);
		DynamicVars.Dexterity.UpgradeValueBy(1m);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Verdant Form"),
		("description", "Gain {StrengthPower:diff()} [gold]Strength[/gold].\nGain {DexterityPower:diff()} [gold]Dexterity[/gold].")
	};
}

[Pool(typeof(WeedRelicPool))]
public sealed class SeedCache : CustomRelicModel
{
	public override string PackedIconPath => ImageHelper.GetImagePath("atlases/relic_atlas.sprites/burning_blood.tres");

	protected override string PackedIconOutlinePath => ImageHelper.GetImagePath("atlases/relic_outline_atlas.sprites/burning_blood.tres");

	protected override string BigIconPath => ImageHelper.GetImagePath("relics/burning_blood.png");

	public override RelicRarity Rarity => RelicRarity.Starter;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new HealVar(1m)
	};

	public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature? dealer, DamageResult result, ValueProp props, Creature target, CardModel? cardSource)
	{
		if (Owner.Creature.IsDead || dealer != Owner.Creature || target.IsPlayer || result.UnblockedDamage <= 0)
		{
			return;
		}

		Flash();
		await CreatureCmd.Heal(Owner.Creature, DynamicVars.Heal.BaseValue);
	}

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "赠予红龙的花冠"),
		("description", "Whenever damage you deal causes an enemy to lose HP, heal {Heal:diff()} HP."),
		("flavor", "A small pouch of seeds saved for the next climb.")
	};
}
