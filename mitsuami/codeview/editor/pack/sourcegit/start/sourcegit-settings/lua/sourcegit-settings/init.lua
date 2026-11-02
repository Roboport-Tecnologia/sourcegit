-- The port's Code View preferences, on the editor. They apply as they change, to the
-- diff showing too. Only what `spec` has changes.

local M = {}

local MODES = { vim = true, modeless = true, helix = true }

-- Modeless chords for what a code view doesn't have: its one view is what it shows, and it
-- saves and opens nothing. `<C-w>` would close the view and leave the pane empty.
local FREED = { "<C-w>", "<D-w>", "<C-s>", "<D-s>", "<C-p>", "<D-p>", "<C-Tab>", "<C-S-Tab>" }

function M.setup()
  for _, key in ipairs(FREED) do
    btv.keymap.set("modeless", key, function() end, { desc = "sourcegit-settings: nothing here" })
  end
end

local function set_mode(mode)
  assert(MODES[mode], "sourcegit-settings: unknown mode: " .. tostring(mode))
  btv.helix.disable()
  btv.o.modeless = mode == "modeless"
  if mode == "helix" then
    btv.helix.enable()
  end
end

-- `size` goes in points, in halves too. No font: the platform's monospace one.
local function set_font(font, size)
  local family, current = font, btv.o.guifont:match(":h([%d.]+)$")
  if family == nil then
    family = btv.o.guifont:gsub(":h[%d.]+$", "")
  end
  size = size or tonumber(current)
  btv.o.guifont = family .. (size and string.format(":h%g", size) or "")
end

-- 'tabstop' is each buffer's: the global only reaches buffers made later.
local function set_tab_width(width)
  assert(
    math.type(width) == "integer" and width > 0,
    "sourcegit-settings: bad tab width: " .. tostring(width)
  )
  btv.o.tabstop = width
  btv.o.shiftwidth = width
  for _, buf in ipairs(btv.buf.list()) do
    btv.bo[buf].tabstop = width
    btv.bo[buf].shiftwidth = width
  end
end

-- 'relativenumber' is each window's, as 'tabstop' is each buffer's.
local function set_relative_numbers(on)
  assert(type(on) == "boolean", "sourcegit-settings: bad relative_numbers: " .. tostring(on))
  btv.o.relativenumber = on
  for _, win in ipairs(btv.win.list()) do
    btv.wo[win].relativenumber = on
  end
end

-- Applies `spec`: { mode, font, font_size, colorscheme, tab_width, relative_numbers },
-- each optional.
function M.apply(spec)
  assert(type(spec) == "table", "sourcegit-settings: the settings aren't a table")
  if spec.mode ~= nil then
    set_mode(spec.mode)
  end
  if spec.font ~= nil or spec.font_size ~= nil then
    set_font(spec.font, spec.font_size)
  end
  if spec.tab_width ~= nil then
    set_tab_width(spec.tab_width)
  end
  if spec.relative_numbers ~= nil then
    set_relative_numbers(spec.relative_numbers)
  end
  if spec.colorscheme ~= nil then
    btv.cmd("colorscheme " .. spec.colorscheme)
  end
end

return M
