@tool
extends Node

@export var visuals_path: NodePath = NodePath("../Visuals")
@export var atlas_path := "res://animations/characters/herbalist/char_1020_reed2.atlas"
@export var skeleton_path := "res://animations/characters/herbalist/spine_converted_b76f642b77aa4e448ee7a97a54031e3c/char_1020_reed2.skel"

var _atlas_res
var _skeleton_file_res
var _skeleton_data_res

func _ready() -> void:
	_load_spine()

func _load_spine() -> void:
	var visuals := get_node_or_null(visuals_path)
	if visuals == null:
		push_error("Spine runtime loader could not find Visuals node: %s" % visuals_path)
		return

	_atlas_res = ClassDB.instantiate("SpineAtlasResource")
	_skeleton_file_res = ClassDB.instantiate("SpineSkeletonFileResource")
	_skeleton_data_res = ClassDB.instantiate("SpineSkeletonDataResource")
	if _atlas_res == null or _skeleton_file_res == null or _skeleton_data_res == null:
		push_error("Spine extension classes are unavailable.")
		return

	var atlas_err = _atlas_res.call("load_from_atlas_file", atlas_path)
	var skel_err = _skeleton_file_res.call("load_from_file", skeleton_path)
	if atlas_err != OK:
		push_error("Failed to load Spine atlas: %s" % atlas_path)
		return
	if skel_err != OK:
		push_error("Failed to load Spine skeleton: %s" % skeleton_path)
		return

	_skeleton_data_res.call("set_atlas_res", _atlas_res)
	_skeleton_data_res.call("set_skeleton_file_res", _skeleton_file_res)
	_skeleton_data_res.call("update_skeleton_data")
	if not _skeleton_data_res.call("is_skeleton_data_loaded"):
		push_error("Spine skeleton data did not load.")
		return

	visuals.set("skeleton_data_res", _skeleton_data_res)
