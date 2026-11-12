extends SceneTree

# Run from an empty project so source-tree shaders cannot mask missing PCK resources.
func _initialize():
    var args = OS.get_cmdline_user_args()
    if args.size() != 1 or not ProjectSettings.load_resource_pack(args[0]):
        push_error("Cannot mount exported stage PCK")
        quit(1)
        return
    var required = {
        "res://shaders/note_material.gdshader": "Shader",
        "res://shaders/bleed_hud.gdshader": "Shader",
        "res://shaders/note_surface.gdshader": "Shader",
        "res://assets/fonts/SpaceGrotesk-Regular.woff2": "Font",
        "res://assets/fonts/SpaceGrotesk-Bold.woff2": "Font",
    }
    var failed = false
    for path in required:
        if not ResourceLoader.exists(path, required[path]):
            push_error("Missing exported resource: " + path)
            failed = true
            continue
        var resource = ResourceLoader.load(path, required[path])
        if resource == null:
            push_error("Cannot load exported resource: " + path)
            failed = true
        elif resource is Shader:
            var material = ShaderMaterial.new()
            material.shader = resource
            if resource.code.is_empty() or resource.get_shader_uniform_list().is_empty():
                push_error("Invalid exported shader: " + path)
                failed = true
            else:
                print("PACK_RESOURCE PASS ", path)
        else:
            print("PACK_RESOURCE PASS ", path)
    if not failed:
        print("EDITOR_PACK_RESOURCES PASS")
    quit(1 if failed else 0)
