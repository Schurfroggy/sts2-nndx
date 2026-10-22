extends SceneTree

func _initialize():
    var args = OS.get_cmdline_user_args()
    assert(args.size() == 1)
    assert(ProjectSettings.load_resource_pack(args[0]))
    var icon = TextureRect.new()
    icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
    icon.size = Vector2(48, 48)
    var original = ImageTexture.create_from_image(Image.create(88, 88, false, Image.FORMAT_RGBA8))
    icon.texture = original
    for i in range(1, 5):
        var path = "res://res/face/face%d.png" % i
        var texture = load(path) as Texture2D
        assert(texture != null, path)
        var expected_size = Vector2(84, 84) if i == 4 else Vector2(88, 88)
        assert(texture.get_size() == expected_size, path)
        assert(texture.get_image() != null, path)
        icon.texture = texture
        assert(icon.size == Vector2(48, 48))
        icon.texture = original
        assert(icon.texture == original)
        print("PASS packed portrait ", i, ": ", texture.get_size(), "; TextureRect size and original texture preserved")
    icon.free()
    quit(0)
