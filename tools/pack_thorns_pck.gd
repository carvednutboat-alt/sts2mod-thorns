extends SceneTree

const OUTPUT_PATH := "res://thorns.pck"

const DIRECT_FILES := [
	"res://animations/characters/thorns/char_293_thorns.atlas",
	"res://animations/characters/thorns/spine_converted_front_default/char_293_thorns.skel",
	"res://scenes/creature_visuals/thorns.tscn",
	"res://scenes/creature_visuals/spine_runtime_loader.gd",
	"res://scenes/screens/char_select/char_select_bg_thorns.tscn",
	"res://scenes/ui/character_icons/thorns_icon.tscn",
]

const TEXTURE_FILES := [
	"res://animations/characters/thorns/char_293_thorns.png",
	"res://images/packed/character_select/char_select_thorns.png",
	"res://images/packed/character_select/thorns_character_select_bg.png",
	"res://images/packed/thorns_icons/alchemy_release_icon.png",
	"res://images/packed/thorns_icons/alchemy_unit_icon.png",
	"res://images/packed/thorns_icons/catalyst_icon.png",
	"res://images/packed/thorns_icons/my_sea_icon.png",
	"res://images/packed/thorns_icons/neural_damage_icon.png",
	"res://images/packed/thorns_icons/paralysis_icon.png",
]

func _init() -> void:
	var packer := PCKPacker.new()
	var start_err := packer.pck_start(ProjectSettings.globalize_path(OUTPUT_PATH))
	print("PCK_START ", start_err)
	if start_err != OK:
		quit(start_err)
		return

	for target_path in DIRECT_FILES:
		if not _add_file(packer, target_path):
			return

	for texture_path in TEXTURE_FILES:
		if not _add_texture_with_imports(packer, texture_path):
			return

	var flush_err := packer.flush(true)
	print("PCK_FLUSH ", flush_err)
	quit(flush_err)

func _add_texture_with_imports(packer: PCKPacker, texture_path: String) -> bool:
	if not _add_file(packer, texture_path):
		return false

	var import_path := texture_path + ".import"
	if not _add_file(packer, import_path):
		return false

	var config := ConfigFile.new()
	var load_err := config.load(ProjectSettings.globalize_path(import_path))
	if load_err != OK:
		print("IMPORT_LOAD_FAIL ", import_path, " err=", load_err)
		quit(load_err)
		return false

	var dest_files: Array = config.get_value("deps", "dest_files", [])
	for dest_file in dest_files:
		if not _add_file(packer, String(dest_file)):
			return false

	return true

func _add_file(packer: PCKPacker, target_path: String) -> bool:
	var source_path := ProjectSettings.globalize_path(target_path)
	if not FileAccess.file_exists(source_path):
		print("MISSING ", target_path, " -> ", source_path)
		quit(ERR_FILE_NOT_FOUND)
		return false

	var add_err := packer.add_file(target_path, source_path)
	print("ADD ", target_path, " <- ", source_path, " err=", add_err)
	if add_err != OK:
		quit(add_err)
		return false

	return true
