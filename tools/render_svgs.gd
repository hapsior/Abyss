extends SceneTree
## Renderuje pliki SVG do arkusza PNG (podgląd grafiki tak, jak wyrenderuje ją Godot/ThorVG).
## Użycie:
##   godot --headless --path . --script res://tools/render_svgs.gd -- <wyjście.png> <rozmiar_komórki> <plik_lub_katalog>...
## Każdy SVG jest rysowany na tle podłogi i w skali 1:1 oraz w skali docelowej w grze (mniejsza miniatura w rogu).

const BG := Color(0.17, 0.15, 0.22)
const CELL_BG := Color(0.27, 0.24, 0.33)

func _init() -> void:
	var args := OS.get_cmdline_user_args()
	if args.size() < 3:
		push_error("użycie: <wyjście.png> <rozmiar_komórki> <pliki/katalogi...>")
		quit(2)
		return
	var out_path: String = args[0]
	var cell := int(args[1])
	var files: Array[String] = []
	for a in args.slice(2):
		if DirAccess.dir_exists_absolute(a):
			for f in DirAccess.get_files_at(a):
				if f.ends_with(".svg"):
					files.append(a.path_join(f))
		else:
			files.append(a)
	files.sort()
	var cols := maxi(1, mini(6, files.size()))
	var rows := int(ceil(files.size() / float(cols)))
	var pad := 12
	var label_h := 0
	var sheet := Image.create(cols * (cell + pad) + pad, rows * (cell + pad) + pad + label_h, false, Image.FORMAT_RGBA8)
	sheet.fill(BG)
	var i := 0
	for f in files:
		var svg := FileAccess.get_file_as_string(f)
		var img := Image.new()
		var err := img.load_svg_from_string(svg, 1.0)
		if err != OK:
			printerr("BŁĄD SVG: ", f, " kod ", err)
			i += 1
			continue
		var x := pad + (i % cols) * (cell + pad)
		var y := pad + (i / cols) * (cell + pad)
		var bg := Image.create(cell, cell, false, Image.FORMAT_RGBA8)
		bg.fill(CELL_BG)
		sheet.blit_rect(bg, Rect2i(0, 0, cell, cell), Vector2i(x, y))
		var s := minf(1.0, float(cell) / maxf(img.get_width(), img.get_height()))
		if s < 1.0:
			img.resize(int(img.get_width() * s), int(img.get_height() * s), Image.INTERPOLATE_LANCZOS)
		img.convert(Image.FORMAT_RGBA8)
		sheet.blend_rect(img, Rect2i(0, 0, img.get_width(), img.get_height()),
			Vector2i(x + (cell - img.get_width()) / 2, y + (cell - img.get_height()) / 2))
		print("ok ", f, " ", img.get_size())
		i += 1
	sheet.save_png(out_path)
	print("zapisano ", out_path)
	quit(0)
