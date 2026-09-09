extends Control

@onready var status_label: Label = %StatusLabel
@onready var progress_bar: ProgressBar = %ProgressBar

func _ready() -> void:
	callable_init.call_deferred()

func callable_init() -> void:
	update_status("Checking game assets...", 0.3)
	
	# 1. Check if assets are already mounted
	if ResourceLoader.exists("res://scenes/game.tscn"):
		launch_game()
		return
		
	# 2. Check user documents folder (Documents/SlayTheSpire2.pck)
	if FileAccess.file_exists("user://SlayTheSpire2.pck"):
		update_status("Loading SlayTheSpire2.pck from Documents...", 0.6)
		if ProjectSettings.load_resource_pack("user://SlayTheSpire2.pck"):
			launch_game()
			return
			
	# 3. Check bundled pck
	if FileAccess.file_exists("res://SlayTheSpire2.pck"):
		update_status("Loading bundled SlayTheSpire2.pck...", 0.6)
		if ProjectSettings.load_resource_pack("res://SlayTheSpire2.pck"):
			launch_game()
			return

	# Missing PCK: Show instructions
	show_missing_pck_instructions()

func update_status(message: String, progress: float) -> void:
	if status_label:
		status_label.text = message
	if progress_bar:
		progress_bar.value = progress * 100.0

func launch_game() -> void:
	update_status("Launching Slay the Spire 2...", 1.0)
	ProjectSettings.set_setting("input_devices/pointing/emulate_mouse_from_touch", true)
	ProjectSettings.set_setting("input_devices/pointing/emulate_touch_from_mouse", true)
	
	var candidate_scenes = [
		"res://scenes/screens/main_menu.tscn",
		"res://scenes/game.tscn",
		"res://main.tscn",
		"res://src/main.tscn"
	]
	for sc in candidate_scenes:
		if ResourceLoader.exists(sc):
			get_tree().change_scene_to_file(sc)
			return
	show_missing_pck_instructions()

func show_missing_pck_instructions() -> void:
	if status_label:
		status_label.text = "Game Data Not Found!\n\nTo play Slay the Spire 2 on your iPhone:\n1. Open the 'Files' app on this iPhone (or connect to PC via iTunes/3uTools).\n2. Go to: 'On My iPhone' -> 'Slay the Spire 2'.\n3. Copy your 'SlayTheSpire2.pck' file into that folder.\n4. Close and re-open this app."
	if progress_bar:
		progress_bar.visible = false
