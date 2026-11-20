extends SceneTree
## Test-only SceneTree: loads the actual client scene. Input goes through Viewport.push_input,
## not through GameplayMain, the pointer buffer or JudgeEngine APIs. No visible viewport.

var clock: Node
var outlines: Array[Node2D] = []
var sent_down := false
var sent_up := false
var saw_start := false
var saw_end := false
var max_inner := 0
var report_written := false

func _initialize() -> void:
	if DisplayServer.get_name() != "headless":
		printerr("Custom probe requires --headless")
		quit(2)
		return
	node_added.connect(_track)
	process_frame.connect(_verify_frame)
	call_deferred("_load_game")

func _load_game() -> void:
	if change_scene_to_file("res://scenes/gameplay.tscn") != OK:
		printerr("Could not load the real gameplay scene")
		quit(2)

func _track(node: Node) -> void:
	if node.has_method("GetSongTime"):
		clock = node
	# The real renderer reparents missed Hold links into the fade layer; node_added
	# fires again for the very same object. Count each instance once.
	if node is Node2D and "CapStart" in node and "CapEnd" in node and not outlines.has(node):
		outlines.append(node)

func _inject(down: bool, now: float) -> void:
	var event := InputEventScreenTouch.new()
	event.index = 17
	event.position = Vector2(280.0 + 2.0 * 273.2, 901.0)
	event.pressed = down
	root.push_input(event, true)
	print("CUSTOM_TOUCH ", JSON.stringify({"pressed": down, "time": now, "x": event.position.x, "y": event.position.y}))

func _verify_frame() -> void:
	if not is_instance_valid(clock): return
	var now := float(clock.call("GetSongTime"))
	# 30/60 FPS can represent the intended frame at 0.49999999999999994.
	# This threshold only schedules test input; production contact math is unchanged.
	var release_at := 1.0 if OS.get_environment("DUV_VERIFY_HOLD_PROBE") == "1" else 0.75
	if not sent_down and now >= 0.5 - 1e-12:
		sent_down = true
		_inject(true, now)
	if not sent_up and now >= release_at - 1e-12:
		sent_up = true
		_inject(false, now)
	# The fixture has one Hold with exactly two authored endpoints. No intermediate
	# link is entitled to a transverse cap, even if its synthetic point is on screen.
	var inner := 0
	var starts := 0
	var ends := 0
	for node in outlines:
		if not is_instance_valid(node) or not node.is_visible_in_tree(): continue
		var start := bool(node.get("CapStart"))
		var end := bool(node.get("CapEnd"))
		if start: starts += 1
		if end: ends += 1
		if not start and not end: inner += 1
	if starts > 1 or ends > 1:
		printerr("Extra transverse Hold cap: time=", now, " starts=", starts, " ends=", ends)
		quit(3)
		return
	max_inner = maxi(max_inner, inner)
	saw_start = saw_start or starts != 0
	saw_end = saw_end or ends != 0
	if not report_written and now >= 4.25:
		report_written = true
		print("CUSTOM_OUTLINE ", JSON.stringify({"uncappedInterior": max_inner, "sawStart": saw_start, "sawEnd": saw_end, "outlineObjects": outlines.size()}))
