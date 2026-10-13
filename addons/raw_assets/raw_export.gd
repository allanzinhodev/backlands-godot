@tool
extends EditorExportPlugin

func _get_name() -> String:
    return "RawRuntimeArt"

func _export_begin(_features: PackedStringArray, _is_debug: bool, _path: String, _flags: int) -> void:
    for folder in ["res://spritesheets", "res://icons", "res://vfx"]:
        _include_folder(folder)

func _include_folder(folder: String) -> void:
    var directory := DirAccess.open(folder)
    if directory == null:
        return
    for child in directory.get_directories():
        _include_folder(folder.path_join(child))
    for filename in directory.get_files():
        if filename.get_extension().to_lower() in ["png", "json"]:
            var path := folder.path_join(filename)
            add_file(path, FileAccess.get_file_as_bytes(path), false)
