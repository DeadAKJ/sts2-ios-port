extends Control

@onready var status_label: Label = %StatusLabel
@onready var progress_bar: ProgressBar = %ProgressBar

func _ready() -> void:
	callable_init.call_deferred()

func callable_init() -> void:
	await get_tree().process_frame
	update_status("Checking game assets...", 0.2)
	await get_tree().process_frame
	
	# Pre-create standard save directories in user://
	DirAccess.make_dir_recursive_absolute("user://Mods")
	DirAccess.make_dir_recursive_absolute("user://Saves")
	DirAccess.make_dir_recursive_absolute("user://MegaCrit/SlayTheSpire2")
	DirAccess.make_dir_recursive_absolute("user://MegaCrit/SlayTheSpire2/saves")
	DirAccess.make_dir_recursive_absolute("user://MegaCrit/SlayTheSpire2/preferences")
	
	# 1. Check if assets are already mounted
	if ResourceLoader.exists("res://scenes/game.tscn"):
		printerr("[STS2 Bootstrap] Game assets already mounted.")
		launch_game()
		return
		
	# 2. Check user documents folder (Documents/SlayTheSpire2.pck)
	if FileAccess.file_exists("user://SlayTheSpire2.pck"):
		update_status("Loading SlayTheSpire2.pck from Documents...", 0.5)
		await get_tree().process_frame
		if ProjectSettings.load_resource_pack("user://SlayTheSpire2.pck"):
			printerr("[STS2 Bootstrap] Loaded user://SlayTheSpire2.pck successfully!")
			update_status("Mounting game resources...", 0.85)
			await get_tree().process_frame
			launch_game()
			return
		else:
			printerr("[STS2 Bootstrap] FAILED to load user://SlayTheSpire2.pck!")
			
	# 3. Check bundled pck
	if FileAccess.file_exists("res://SlayTheSpire2.pck"):
		update_status("Loading bundled SlayTheSpire2.pck...", 0.5)
		await get_tree().process_frame
		if ProjectSettings.load_resource_pack("res://SlayTheSpire2.pck"):
			printerr("[STS2 Bootstrap] Loaded res://SlayTheSpire2.pck successfully!")
			update_status("Mounting game resources...", 0.85)
			await get_tree().process_frame
			launch_game()
			return
		else:
			printerr("[STS2 Bootstrap] FAILED to load res://SlayTheSpire2.pck!")

	# Missing PCK: Show instructions
	show_missing_pck_instructions()

func update_status(message: String, progress: float) -> void:
	printerr("[STS2 Bootstrap] " + message + " (" + str(int(progress * 100)) + "%)")
	if status_label:
		status_label.text = message
	if progress_bar:
		progress_bar.value = progress * 100.0

func launch_game() -> void:
	printerr("[STS2 Bootstrap] Entering launch_game()...")
	update_status("Launching Slay the Spire 2...", 1.0)
	await get_tree().process_frame
	ProjectSettings.set_setting("input_devices/pointing/emulate_mouse_from_touch", true)
	ProjectSettings.set_setting("input_devices/pointing/emulate_touch_from_mouse", true)
	
	if has_node("/root/STS2Bootstrapper"):
		printerr("[STS2 Bootstrap] Calling EnsureRegistered on /root/STS2Bootstrapper")
		get_node("/root/STS2Bootstrapper").call("EnsureRegistered")
	
	var candidate_scenes = [
		"res://scenes/game.tscn",
		"res://scenes/screens/main_menu.tscn",
		"res://main.tscn",
		"res://src/main.tscn"
	]
	for sc in candidate_scenes:
		if ResourceLoader.exists(sc):
			printerr("[STS2 Bootstrap] Transitioning to scene: " + sc)
			get_tree().change_scene_to_file(sc)
			return
	printerr("[STS2 Bootstrap] ERROR: No candidate game scene found in mounted PCK!")
	show_missing_pck_instructions()

func show_missing_pck_instructions() -> void:
	if status_label:
		status_label.text = "Game Data Not Found!\n\nTo play Slay the Spire 2 on your iPhone:\n1. Open the 'Files' app on this iPhone (or connect to PC via iTunes/3uTools).\n2. Go to: 'On My iPhone' -> 'Slay the Spire 2'.\n3. Copy your 'SlayTheSpire2.pck' file into that folder.\n4. Close and re-open this app."
	if progress_bar:
		progress_bar.visible = false
