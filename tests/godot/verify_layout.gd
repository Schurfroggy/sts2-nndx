extends SceneTree

# The C# regression runner exports values produced by the actual mod. Verify
# those values with native Godot transforms, without loading a game or any mod.
func _initialize():
	call_deferred("verify")

func verify():
	var args = OS.get_cmdline_user_args()
	if args.size() != 1:
		push_error("Expected the production-layout JSON path after --")
		quit(1)
		return
	var cases = JSON.parse_string(FileAccess.get_file_as_string(args[0]))
	if not cases is Array or cases.is_empty():
		push_error("No production layout cases found")
		quit(1)
		return
	var failures = 0
	for data in cases:
		var creature = Node2D.new()
		root.add_child(creature)
		creature.position = Vector2(600, 500)
		creature.scale = vector(data.parent_scale)
		var visuals = Node2D.new()
		creature.add_child(visuals)
		visuals.scale = vector(data.body_scale)
		var sprite = AnimatedSprite2D.new()
		visuals.add_child(sprite)
		sprite.centered = false
		sprite.scale = vector(data.sprite_scale)
		sprite.position = vector(data.sprite_position)
		sprite.offset = vector(data.sprite_offset)
		var foot = sprite.to_global(sprite.offset + Vector2(135, 315))
		var canvas_bottom = sprite.to_global(sprite.offset + Vector2(135, 330))
		var top = sprite.to_global(sprite.offset + Vector2(135, 0))
		var left_edge = sprite.to_global(sprite.offset)
		var right_edge = sprite.to_global(sprite.offset + Vector2(270, 0))
		var width_failed = data.has("expected_width") and abs(right_edge.x - left_edge.x - data.expected_width) > 0.01
		if width_failed or foot.distance_to(creature.global_position) > 0.01 or abs(canvas_bottom.y - top.y - data.expected_height) > 0.01:
			failures += 1
			push_error("Layout invariant failed: " + str(data))
		creature.free()
	print("Native Godot: %d/%d production layouts passed" % [cases.size() - failures, cases.size()])
	quit(0 if failures == 0 else 1)

func vector(values):
	return Vector2(values[0], values[1])
