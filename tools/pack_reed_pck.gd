extends SceneTree

const OUTPUT_PATH := "E:/Godot/sts2mod/weed/reed.pck"

const PACK_FILES := {
	"res://animations/characters/herbalist/char_1020_reed2.atlas": "E:/Godot/sts2mod/weed/animations/characters/herbalist/char_1020_reed2.atlas",
	"res://animations/characters/herbalist/char_1020_reed2.png": "E:/Godot/sts2mod/weed/animations/characters/herbalist/char_1020_reed2.png",
	"res://animations/characters/herbalist/char_1020_reed2.png.import": "E:/Godot/sts2mod/weed/animations/characters/herbalist/char_1020_reed2.png.import",
	"res://.godot/imported/char_1020_reed2.png-78bd075b54d42cac2899ecce08d59e0b.ctex": "E:/Godot/sts2mod/weed/.godot/imported/char_1020_reed2.png-78bd075b54d42cac2899ecce08d59e0b.ctex",
	"res://animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2.skel": "E:/Godot/sts2mod/weed/animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2.skel",
	"res://images/packed/card_portraits/weed/strike.png": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/strike.png",
	"res://images/packed/card_portraits/weed/strike.png.import": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/strike.png.import",
	"res://.godot/imported/strike.png-fc1a12c392462ca1be3c0d01d44a625f.ctex": "E:/Godot/sts2mod/weed/.godot/imported/strike.png-fc1a12c392462ca1be3c0d01d44a625f.ctex",
	"res://images/packed/card_portraits/weed/defend.png": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/defend.png",
	"res://images/packed/card_portraits/weed/defend.png.import": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/defend.png.import",
	"res://.godot/imported/defend.png-2e80085ef10aad7b6ead2e3f6764203d.ctex": "E:/Godot/sts2mod/weed/.godot/imported/defend.png-2e80085ef10aad7b6ead2e3f6764203d.ctex",
	"res://images/packed/card_portraits/weed/herbal_guard.png": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/herbal_guard.png",
	"res://images/packed/card_portraits/weed/herbal_guard.png.import": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/herbal_guard.png.import",
	"res://.godot/imported/herbal_guard.png-69e52b881b052c1dd5f439e9143b7fc6.ctex": "E:/Godot/sts2mod/weed/.godot/imported/herbal_guard.png-69e52b881b052c1dd5f439e9143b7fc6.ctex",
	"res://images/packed/card_portraits/weed/scorch.png": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/scorch.png",
	"res://images/packed/card_portraits/weed/scorch.png.import": "E:/Godot/sts2mod/weed/images/packed/card_portraits/weed/scorch.png.import",
	"res://.godot/imported/scorch.png-dbd40fcafcfac8097468f7c03f1dd91d.ctex": "E:/Godot/sts2mod/weed/.godot/imported/scorch.png-dbd40fcafcfac8097468f7c03f1dd91d.ctex",
	"res://images/packed/character_select/reed_character_select_bg.png": "E:/Godot/sts2mod/weed/images/packed/character_select/reed_character_select_bg.png",
	"res://images/packed/character_select/reed_character_select_bg.png.import": "E:/Godot/sts2mod/weed/images/packed/character_select/reed_character_select_bg.png.import",
	"res://.godot/imported/reed_character_select_bg.png-42af0d7a1a633167c2e62a075057d0f5.ctex": "E:/Godot/sts2mod/weed/.godot/imported/reed_character_select_bg.png-42af0d7a1a633167c2e62a075057d0f5.ctex",
	"res://scenes/creature_visuals/herbalist.tscn": "E:/Godot/sts2mod/weed/scenes/creature_visuals/herbalist.tscn",
	"res://scenes/creature_visuals/spine_runtime_loader.gd": "E:/Godot/sts2mod/weed/scenes/creature_visuals/spine_runtime_loader.gd",
	"res://scenes/screens/char_select/char_select_bg_herbalist.tscn": "E:/Godot/sts2mod/weed/scenes/screens/char_select/char_select_bg_herbalist.tscn",
	"res://scenes/merchant/characters/herbalist_merchant.tscn": "E:/Godot/sts2mod/weed/scenes/merchant/characters/herbalist_merchant.tscn",
	"res://scenes/rest_site/characters/herbalist_rest_site.tscn": "E:/Godot/sts2mod/weed/scenes/rest_site/characters/herbalist_rest_site.tscn",
}

func _init() -> void:
	var packer := PCKPacker.new()
	var start_err := packer.pck_start(OUTPUT_PATH)
	print("PCK_START ", start_err)
	if start_err != OK:
		quit(start_err)
		return

	for target_path in PACK_FILES.keys():
		var source_path: String = PACK_FILES[target_path]
		var add_err := packer.add_file(target_path, source_path)
		print("ADD ", target_path, " <- ", source_path, " err=", add_err)
		if add_err != OK:
			quit(add_err)
			return

	var flush_err := packer.flush(true)
	print("PCK_FLUSH ", flush_err)
	quit(flush_err)
