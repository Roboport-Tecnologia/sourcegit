-- The code view's editor: this directory is its whole config (`Config::Dir`), and
-- plugins under pack/*/start/ load with it. It reads nobody's own config.
--
-- Plain bemtvi runs it too, with no app around it, for trying a plugin in a terminal:
--   BEMTVI_CONFIG=mitsuami/codeview/editor bemtvi
--   :SourceGitDiff mitsuami/codeview/fixtures/edit.json
--   :lua require("sourcegit-settings").apply({ mode = "helix", tab_width = 2 })
-- The preferences' default, where the editor's own is on.
btv.o.relativenumber = false
local diff = require("sourcegit-diff")
local grammars = require("sourcegit-grammars")
diff.setup()
grammars.setup()
-- Plugins are tested alone, so they meet here.
diff.on_language = grammars.ensure
grammars.on_ready = diff.recolor
