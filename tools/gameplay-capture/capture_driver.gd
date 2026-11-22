extends Node

var output := OS.get_environment("DU_CAPTURE_OUT")
var preview_frames := -1
var result_hold_frames := -1
var checks: Array[String] = []
var marks := {}
var failed := false

func mark(name: String) -> void:
    marks[name] = Engine.get_frames_drawn()
    print("CAPTURE FRAME ", name, " = ", marks[name])

func _ready() -> void:
    process_mode = Node.PROCESS_MODE_ALWAYS
    var expected := OS.get_environment("DU_CAPTURE_USER").simplify_path().replace("\\", "/").to_lower()
    var actual := OS.get_user_data_dir().simplify_path().replace("\\", "/").to_lower()
    if actual != expected or not actual.contains("/.tmp/gameplay-capture/") or DirAccess.dir_exists_absolute("res://testdata"):
        fail("unsafe recording project or user directory: " + actual + " expected " + expected)
        return
    if not OS.has_feature("movie"):
        fail("Godot Movie Maker is not active")
        return
    var preview := OS.get_environment("DU_CAPTURE_PREVIEW_FRAMES")
    var hold := OS.get_environment("DU_CAPTURE_RESULT_HOLD_FRAMES")
    if not preview.is_valid_int() or not hold.is_valid_int() or int(preview) < 0 or int(hold) < 0:
        fail("invalid segment frame counts")
        return
    preview_frames = int(preview)
    result_hold_frames = int(hold)
    call_deferred("run_capture")

func check(ok: bool, name: String) -> bool:
    checks.append(name + ": " + str(ok))
    print("CAPTURE CHECK ", name, " = ", ok)
    if not ok:
        fail("failed: " + name)
    return ok

func fail(reason: String) -> void:
    if failed:
        return
    failed = true
    write_report(false, reason)
    push_error("GAMEPLAY CAPTURE FAILED: " + reason)
    get_tree().quit(20)

func write_report(ok: bool, reason: String = "") -> void:
    var file := FileAccess.open(output + "/capture-result.json", FileAccess.WRITE)
    if file != null:
        file.store_string(JSON.stringify({"pass": ok, "reason": reason, "checks": checks, "frames": marks}, "  "))
        file.close()

func wait_frames(count: int) -> void:
    for _i in count:
        await get_tree().process_frame

func click(point: Vector2) -> void:
    var event := InputEventMouseButton.new()
    event.position = point
    event.global_position = point
    event.button_index = MOUSE_BUTTON_LEFT
    event.pressed = true
    get_viewport().push_input(event, true)
    await get_tree().process_frame
    event = InputEventMouseButton.new()
    event.position = point
    event.global_position = point
    event.button_index = MOUSE_BUTTON_LEFT
    event.pressed = false
    get_viewport().push_input(event, true)
    await wait_frames(4)

func wait_scene(path: String, limit: int) -> bool:
    for _i in limit:
        if get_tree().current_scene != null and get_tree().current_scene.scene_file_path.ends_with(path):
            return true
        await get_tree().process_frame
    return false

func save_frame(name: String) -> void:
    await RenderingServer.frame_post_draw
    var image := get_viewport().get_texture().get_image()
    if image != null and not image.is_empty():
        image.save_png(output + "/" + name + ".png")

func run_capture() -> void:
    if not await wait_scene("song_select.tscn", 300):
        fail("formal song-select scene did not become ready")
        return
    await wait_frames(90)
    var page := get_tree().current_scene
    if not check(page.get_child_count() > 0, "song-select UI assembled"):
        return

    var preview := page.find_child("PreviewPlayer", true, false) as AudioStreamPlayer
    if not check(preview != null, "formal preview player is present"):
        return
    # The formal page prepares START and its cover when the card is opened. For zero preview
    # this is only discarded preroll; the output starts at handoff, after START stops the preview.
    if preview_frames > 0:
        mark("expand")
    await click(Vector2(900, 225))
    for _i in 900:
        if preview.playing:
            break
        await get_tree().process_frame
    if not check(preview.playing, "expanded song card plays its decoded audio"):
        return
    if preview_frames > 0:
        mark("preview-playing")
    var card := page.find_child("ExpandedCard", true, false) as Control
    if not check(card != null and card.is_visible_in_tree(), "real expanded preview page is visible"):
        return
    if preview_frames > 0:
        await wait_frames(preview_frames)
        await save_frame("preview-page")
    mark("start")
    await click(Vector2(1690, 1010))
    for _i in 120:
        if get_tree().paused:
            break
        await get_tree().process_frame
    if not check(get_tree().paused, "formal Track Handoff began"):
        return
    mark("handoff")
    if not await wait_scene("gameplay.tscn", 600):
        fail("formal START did not navigate to gameplay")
        return
    var music: AudioStreamPlayer = null
    for player in get_tree().current_scene.find_children("*", "AudioStreamPlayer", true, false):
        if player.bus == &"Music":
            music = player as AudioStreamPlayer
            break
    if not check(music != null, "gameplay has a real music player"):
        return
    var counted := 0
    while not music.playing and counted < 300:
        counted += 1
        await get_tree().process_frame
    if not check(music.playing and not is_instance_valid(preview), "gameplay starts music after freeing preview"):
        return
    mark("gameplay-music")
    var result := get_tree().current_scene.find_child("ResultOutcomePrelude", true, false) as Control
    if not check(result != null, "formal result-outcome node is present"):
        return
    for i in 1500:
        if not music.playing and not marks.has("music-ended"):
            mark("music-ended")
        if result.visible:
            break
        if i == 310:
            await save_frame("gameplay")
        await get_tree().process_frame
    if not check(result.visible and marks.has("music-ended"), "formal result intro starts no earlier than natural music end"):
        return
    mark("result-intro")
    await wait_frames(26)
    await save_frame("result-intro")
    var result_page: Control = null
    for _i in 240:
        result_page = get_tree().current_scene.find_child("ResultPage", true, false) as Control
        if result_page != null and result_page.is_visible_in_tree():
            break
        await get_tree().process_frame
    if not check(result_page != null and result_page.is_visible_in_tree(), "formal result page uncovered"):
        return
    var grade := result_page.find_child("GradeV5Current", true, false) as TextureRect
    var actions := result_page.find_child("Actions", true, false) as Control
    if not check(grade != null and actions != null, "formal grade and action groups exist"):
        return
    for _i in 600:
        if grade.visible and grade.texture != null and actions.modulate.a >= .999:
            break
        await get_tree().process_frame
    if not check(grade.visible and grade.texture != null and actions.modulate.a >= .999,
                 "grade and score reveal reached its final state"):
        return
    mark("result-revealed")
    await save_frame("result-page")
    await wait_frames(result_hold_frames)
    mark("result-held")
    write_report(true)
    print("GAMEPLAY CAPTURE PASS")
    get_tree().quit()
