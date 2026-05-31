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
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace ThornsMod;

public sealed class Thorns : PlaceholderCharacterModel
{
	private const string ThornsVisualPath = "res://scenes/creature_visuals/thorns.tscn";
	private const string ThornsCharacterSelectBgPath = "res://scenes/screens/char_select/char_select_bg_thorns.tscn";
	private const string ThornsIconPath = "res://scenes/ui/character_icons/thorns_icon.tscn";
	private const string ThornsCharacterSelectIconPath = "packed/character_select/char_select_thorns.png";
	private const string ThornsCharacterSelectLockedIconPath = "packed/character_select/char_select_thorns.png";
	private const string ThornsAtlasPath = "res://animations/characters/thorns/char_293_thorns.atlas";
	private const string ThornsAtlasTexturePath = "res://animations/characters/thorns/char_293_thorns.png";
	private const string ThornsSkeletonPath = "res://animations/characters/thorns/spine_converted_front_default/char_293_thorns.skel";

	public override string PlaceholderID => "silent";
	public override string CustomVisualPath => CanLoadThornsVisual() ? ThornsVisualPath : base.CustomVisualPath;
	public override string CustomCharacterSelectBg => ResourceLoader.Exists(ThornsCharacterSelectBgPath) ? ThornsCharacterSelectBgPath : base.CustomCharacterSelectBg;
	protected override string IconPath => ResourceLoader.Exists(ThornsIconPath) ? ThornsIconPath : base.IconPath;
	protected override string CharacterSelectIconPath => ImageHelper.GetImagePath(ThornsCharacterSelectIconPath);
	protected override string CharacterSelectLockedIconPath => ImageHelper.GetImagePath(ThornsCharacterSelectLockedIconPath);

	private static bool CanLoadThornsVisual()
	{
		if (!ResourceLoader.Exists(ThornsVisualPath) || !FileAccess.FileExists(ThornsAtlasPath) || !FileAccess.FileExists(ThornsSkeletonPath))
			return false;
		try { return ResourceLoader.Load<Texture2D>(ThornsAtlasTexturePath) != null; }
		catch (Exception) { return false; }
	}

	public override CreatureAnimator? SetupCustomAnimationStates(MegaSprite controller)
	{
		if (!controller.HasAnimation("Idle")) return null;
		return SetupAnimationState(controller, idleName: "Idle", deadName: "Die", hitName: "Start", attackName: "Attack_1", castName: "Skill1_1");
	}

	public override CharacterGender Gender => CharacterGender.Masculine;
	public override Color NameColor => new Color("B7D179");
	public override int StartingHp => 70;
	public override CardPoolModel CardPool => ModelDb.CardPool<ThornsCardPool>();
	public override RelicPoolModel RelicPool => ModelDb.RelicPool<SilentRelicPool>();
	public override PotionPoolModel PotionPool => ModelDb.PotionPool<SharedPotionPool>();

	public override IEnumerable<CardModel> StartingDeck => new CardModel[]
	{
		ModelDb.Card<ThornsStrike>(), ModelDb.Card<ThornsStrike>(),
		ModelDb.Card<ThornsStrike>(), ModelDb.Card<ThornsStrike>(),
		ModelDb.Card<ThornsDefend>(), ModelDb.Card<ThornsDefend>(),
		ModelDb.Card<ThornsDefend>(), ModelDb.Card<ThornsDefend>(),
		ModelDb.Card<ThornsDefend>(),
		ModelDb.Card<PoisonStrike>(), ModelDb.Card<IronGuard>(),
		ModelDb.Card<AlchemicalMixture>(),
	};

	public override IReadOnlyList<RelicModel> StartingRelics => new RelicModel[]
	{
		ModelDb.Relic<NeurotoxinCoating>(),
	};

	public override Color DialogueColor => new Color("506828");
	public override Color MapDrawingColor => new Color("8AA84F");
	public override Color RemoteTargetingLineColor => new Color("B7D179");
	public override Color RemoteTargetingLineOutline => new Color("35451A");
	public override List<string> GetArchitectAttackVfx() => ModelDb.Character<Silent>().GetArchitectAttackVfx();

	public override List<(string, string)> Localization => new List<(string, string)>
	{
		("title", "Thorns"), ("titleObject", "Thorns"),
		("description", "A prototype Arknights character.\nUses Thorns cards and a poison-focused relic."),
		("cardsModifierTitle", "Thorns Cards"), ("cardsModifierDescription", "Thorns cards will now appear in rewards and shops."),
		("eventDeathPrevention", "The black tide still recedes."),
		("possessiveAdjective", "his"), ("pronounObject", "him"), ("pronounPossessive", "his"), ("pronounSubject", "he"),
		("unlockText", "Available from the Thorns mod.")
	};
}

[HarmonyPatch(typeof(CharacterModel), "get_IconTexture")]
internal static class ThornsCharacterIconTexturePatch
{
	private const string IconPath = "res://images/packed/character_select/char_select_thorns.png";
	private static void Postfix(CharacterModel __instance, ref Texture2D __result) { if (__instance is Thorns && ResourceLoader.Exists(IconPath)) __result = ResourceLoader.Load<Texture2D>(IconPath); }
}

[HarmonyPatch(typeof(CharacterModel), "get_IconOutlineTexture")]
internal static class ThornsCharacterIconOutlineTexturePatch
{
	private const string IconOutlinePath = "res://images/packed/character_select/char_select_thorns.png";
	private static void Postfix(CharacterModel __instance, ref Texture2D __result) { if (__instance is Thorns && ResourceLoader.Exists(IconOutlinePath)) __result = ResourceLoader.Load<Texture2D>(IconOutlinePath); }
}

[HarmonyPatch(typeof(TheArchitect), "DefineDialogues")]
internal static class ThornsArchitectDefineDialoguesPatch
{
	private static void Postfix(AncientDialogueSet __result)
	{
		string thornsKey = ModelDb.Character<Thorns>().Id.Entry;
		if (__result.CharacterDialogues.ContainsKey(thornsKey)) return;
		__result.CharacterDialogues[thornsKey] = new AncientDialogue[] { new AncientDialogue("") { VisitIndex = 0, IsRepeating = true, EndAttackers = ArchitectAttackers.None } };
	}
}

[Pool(typeof(SilentRelicPool))]
public sealed class NeurotoxinCoating : CustomRelicModel
{
	private readonly HashSet<Creature> _poisonedThisTurn = new();
	public override string PackedIconPath => ImageHelper.GetImagePath("atlases/relic_atlas.sprites/snecko_skull.tres");
	protected override string PackedIconOutlinePath => ImageHelper.GetImagePath("atlases/relic_outline_atlas.sprites/snecko_skull.tres");
	protected override string BigIconPath => ImageHelper.GetImagePath("relics/snecko_skull.png");
	public override RelicRarity Rarity => RelicRarity.Starter;
	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[] { new PowerVar<PoisonPower>(3m) };
	protected override IEnumerable<IHoverTip> ExtraHoverTips => new IHoverTip[] { HoverTipFactory.FromPower<PoisonPower>() };
	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card.Owner != Owner || !CombatManager.Instance.IsInProgress || cardPlay.Card.Type != CardType.Attack) return;
		IEnumerable<Creature> targets = cardPlay.Target != null ? new Creature[] { cardPlay.Target } : Owner.Creature.CombatState?.HittableEnemies ?? Enumerable.Empty<Creature>();
		List<Creature> validTargets = targets.Where(target => !target.IsPlayer && target.IsAlive && !_poisonedThisTurn.Contains(target)).ToList();
		if (validTargets.Count == 0) return;
		Flash();
		foreach (Creature target in validTargets) { _poisonedThisTurn.Add(target); await PowerCmd.Apply<PoisonPower>(target, DynamicVars.Poison.BaseValue, Owner.Creature, null); }
	}
	public override Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side) { if (side == Owner.Creature.Side) _poisonedThisTurn.Clear(); return Task.CompletedTask; }
	public override Task AfterCombatEnd(CombatRoom _) { _poisonedThisTurn.Clear(); return Task.CompletedTask; }
	public override List<(string, string)> Localization => new() { ("title", "Neurotoxin Coating"), ("description", "The first time each turn you play an Attack against an enemy, apply {PoisonPower:diff()} Poison to that enemy."), ("flavor", "A measured dose on the blade's edge.") };
}
