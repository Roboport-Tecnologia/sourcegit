-- SourceGit's text diff. It shows the diff git made: SourceGit's commands choose whitespace
-- options, so this never diffs anything itself. `fixtures/*.json` show what it's given, and
-- `model.lua` what it makes of it. Unified and side by side are two layouts of the same
-- lines, and fewer or more lines of context only rebuild them, with no call to git.
--
-- The hunks of a file aren't code a grammar can read, so the colours come from each version
-- whole, parsed off-buffer, and are copied onto the lines they belong to.

local model = require("sourcegit-diff.model")

local M = {}

-- How much of a version gets parsed for colours; above it a diff shows plain, right away.
-- The parse has its own deadline, 50 ms on the editor thread, which a version passes at
-- around 350 KB on a fast machine, and also shows plain, so past this it isn't tried.
M.max_colored_bytes = 1024 * 1024

-- What fills a row with no line opposite it: no code has it, and it's none of what bemtvi's
-- 'list' draws for whitespace.
M.filler = "╌"

-- Told the language each diff is in, as `sourcegit-grammars` wants to know.
M.on_language = function(_) end

local ns = btv.ns.create("sourcegit-diff")
M.namespace = ns
local ns_syntax = btv.ns.create("sourcegit-diff-syntax")

local KINDS = {
  normal = {},
  indicator = {},
  added = { line = "DiffAdd", sign = "+", sign_group = "Added" },
  deleted = { line = "DiffDelete", sign = "-", sign_group = "Removed" },
}

local state = {
  layout = {
    context = 4,
    all = false,
    side_by_side = false,
    wrap = false,
    syntax = true,
    hidden_symbols = false,
  },
  -- The diff showing.
  spec = nil,
  lines = nil,
  -- The colours of each version, by line number: { old = {…}, new = {…} }.
  syntax = nil,
  language = nil,
  -- What's on screen: a view per pane, and each pane's rows.
  views = {},
  panes = {},
  hunks = {},
  -- Which show this is, so what's late for an earlier one goes.
  generation = 0,
  moved = nil,
  -- The change last told to the app, so it hears only as it changes.
  position = nil,
  sync = {},
  syncing = false,
  echoes = {},
}

-- A sign with no colour draws in the client's default, red. Colorschemes usually have these
-- two; for those that don't, they follow the scheme's palette, and switching schemes.
local function sign_colours()
  local palette = btv.hl.palette()
  btv.hl.fallback("Added", { fg = palette.green })
  btv.hl.fallback("Removed", { fg = palette.red })
end

local sign_colours_listening = false

-- Tells the app, or nobody in a plain terminal.
local function send(name, payload)
  if btv.host.name() then
    btv.host.send(name, payload)
  end
end

-- Raises on what the app got wrong, so a change on its side shows up here.
local function check(spec)
  assert(type(spec) == "table", "sourcegit-diff: the diff isn't a table")
  assert(type(spec.path) == "string", "sourcegit-diff: the diff has no path")
  assert(type(spec.lines) == "table", "sourcegit-diff: the diff has no lines")
  for i, line in ipairs(spec.lines) do
    local where = "sourcegit-diff: line " .. i
    assert(KINDS[line.kind], where .. " has an unknown kind: " .. tostring(line.kind))
    assert(type(line.text) == "string", where .. " has no text")
    assert(math.type(line.old) == "integer", where .. " has no old number")
    assert(math.type(line.new) == "integer", where .. " has no new number")
  end
end

local function pad(n, width)
  return string.format("%" .. width .. "s", n > 0 and n or "")
end

local function digits(lines)
  local width = 1
  for _, line in ipairs(lines) do
    width = math.max(width, #tostring(line.old), #tostring(line.new))
  end
  return width
end

-- The gutter's numbers: both in a pane that has both versions, else its own side's.
local function numbers(row, pane_index, side_by_side, width)
  if not side_by_side then
    return pad(row.old, width) .. " " .. pad(row.new, width) .. " "
  end
  return pad(pane_index == 1 and row.old or row.new, width) .. " "
end

local function pane_text(pane)
  local out = {}
  for i, row in ipairs(pane) do
    if row.gap then
      out[i] = string.format("⋯ %d unchanged lines", row.gap)
    else
      out[i] = row.text or ""
    end
  end
  return out
end

-- The numbers beside each line, its tint, and the changed characters over that. `spans` are
-- 0-based, end-exclusive bytes.
local function pane_marks(pane, pane_index, side_by_side, width)
  local out = {}
  for i, row in ipairs(pane) do
    local line = i - 1
    if row.gap then
      out[#out + 1] = { line = line, col = 0, line_hl_group = "Folded", priority = 100 }
    elseif row.filler then
      out[#out + 1] = {
        line = line,
        col = 0,
        line_fill = { text = M.filler, hl_group = "NonText" },
        priority = 100,
      }
    else
      local kind = KINDS[row.kind]
      out[#out + 1] = {
        line = line,
        col = 0,
        gutter_text = { { numbers(row, pane_index, side_by_side, width), "LineNr" } },
      }
      if kind.line then
        out[#out + 1] = { line = line, col = 0, line_hl_group = kind.line, priority = 100 }
        out[#out + 1] = {
          line = line,
          col = 0,
          sign_text = kind.sign,
          sign_hl_group = kind.sign_group,
          priority = 100,
        }
      end
      for _, span in ipairs(row.spans or {}) do
        out[#out + 1] = {
          line = line,
          col = span[1],
          end_row = line,
          end_col = span[2],
          hl_group = "DiffText",
          priority = 200,
        }
      end
    end
  end
  return out
end

-- The version's colours on the rows showing it. A deleted line takes the old version's, and
-- the rest the new one's; an unchanged line reads the same in both.
local function syntax_marks(pane)
  if not state.syntax or not state.layout.syntax then
    return {}
  end
  local out = {}
  for i, row in ipairs(pane) do
    local spans
    if row.kind == "deleted" then
      spans = state.syntax.old[row.old]
    elseif row.kind then
      spans = state.syntax.new[row.new]
    end
    for _, span in ipairs(spans or {}) do
      out[#out + 1] = {
        line = i - 1,
        col = span[1],
        end_row = i - 1,
        end_col = span[2],
        hl_group = "@" .. span[3],
        priority = 150,
      }
    end
  end
  return out
end

local function paint_syntax()
  for i, view in ipairs(state.views) do
    if view:bufnr() and state.panes[i] then
      view:set_decor(ns_syntax, syntax_marks(state.panes[i]))
    end
  end
end

-- A window's cursor line, 1-based.
local function cursor_row(win)
  return btv.win.call(win, btv.win.saveview).lnum
end

-- Keeps the panes' views and cursors together, by the row: panes line up row for row.
local function stop_sync()
  for _, id in ipairs(state.sync) do
    btv.autocmd.del(id)
  end
  state.sync = {}
end

local function start_sync()
  stop_sync()
  state.echoes = {}
  local function ours(win)
    for _, view in ipairs(state.views) do
      if view:winid() == win then
        return true
      end
    end
    return false
  end
  local function others(win)
    local out = {}
    for _, view in ipairs(state.views) do
      local other = view:winid()
      if other and other ~= win then
        out[#out + 1] = other
      end
    end
    return out
  end
  -- A move set here fires these again: comparing first ends it there.
  local function guarded(body)
    state.syncing = true
    local ok, err = pcall(body)
    state.syncing = false
    if not ok then
      send("error", { message = tostring(err) })
    end
  end
  state.sync[1] = btv.autocmd.create("WinScrolled", {
    callback = function(args)
      local win = tonumber(args.match)
      if state.syncing or not win or not ours(win) then
        return
      end
      guarded(function()
        local from = btv.win.call(win, btv.win.saveview)
        -- A pane set here says so a tick later, with what it was set to. Another notch may
        -- have come since, and passing that on would undo it.
        local echo = state.echoes[win]
        if echo then
          state.echoes[win] = nil
          if echo.topline == from.topline and echo.leftcol == from.leftcol then
            return
          end
        end
        for _, other in ipairs(others(win)) do
          local at = btv.win.call(other, btv.win.saveview)
          if at.topline ~= from.topline or at.leftcol ~= from.leftcol then
            state.echoes[other] = { topline = from.topline, leftcol = from.leftcol }
            btv.win.set_topline(other, from.topline)
            btv.win.set_leftcol(other, from.leftcol)
          end
        end
      end)
    end,
  })
  state.sync[2] = btv.autocmd.create("CursorMoved", {
    callback = function()
      local win = btv.win.current()
      if state.syncing or not ours(win) then
        return
      end
      guarded(function()
        -- With its column too: a pane's cursor out of its view scrolls the view back to it,
        -- and a sideways scroll moves the cursor.
        local at = btv.win.call(win, btv.win.saveview)
        for _, other in ipairs(others(win)) do
          local there = btv.win.call(other, btv.win.saveview)
          if there.lnum ~= at.lnum or there.col ~= at.col then
            btv.win.set_cursor(other, at.lnum, at.col)
          end
        end
      end)
    end,
  })
end

-- Which change the cursor is at or past, 0 before the first.
local function change_at(row)
  local at = 0
  for i, start in ipairs(state.hunks) do
    if start > row then
      break
    end
    at = i
  end
  return at
end

-- Tells the app where the cursor is among the changes, for the toolbar's `2/5`. `row` is
-- where it's on its way to: a move is queued, and reading it now would say where it was.
local function report_position(row)
  local win = btv.win.current()
  local ours = false
  for _, view in ipairs(state.views) do
    ours = ours or view:winid() == win
  end
  if not ours then
    return
  end
  local at = change_at(row or cursor_row(win))
  if at ~= state.position then
    state.position = at
    send("change_position", { at = at, total = #state.hunks })
  end
end

-- Moves to a change: "first", "prev", "next" or "last". Past either end it stays.
function M.go(where)
  local win = btv.win.current()
  local at = cursor_row(win)
  local target
  if where == "first" then
    target = state.hunks[1]
  elseif where == "last" then
    target = state.hunks[#state.hunks]
  elseif where == "next" then
    for _, start in ipairs(state.hunks) do
      if start > at then
        target = start
        break
      end
    end
  elseif where == "prev" then
    for i = #state.hunks, 1, -1 do
      if state.hunks[i] < at then
        target = state.hunks[i]
        break
      end
    end
  else
    error("sourcegit-diff: can't go to: " .. tostring(where))
  end
  if target then
    btv.win.set_cursor(win, target, 0)
  end
end

-- The app's call for the toolbar: { where }.
function M.go_to(spec)
  assert(type(spec) == "table", "sourcegit-diff: go_to takes a table")
  M.go(spec.where)
end

-- A diff in panes is one thing to close: `:q` in one is leaving, as in the one-pane view.
local function closed()
  local had = #state.views
  stop_sync()
  state.views, state.panes, state.hunks = {}, {}, {}
  if had > 1 then
    btv.cmd("qa!")
  end
end

local function make_view(name)
  local view = btv.view.create({ name = name })
  view:on_close(closed)
  return view
end

-- The panes the layout wants, keeping what's there. The first takes the current window, so
-- a diff is all this editor shows and `:q` quits it.
local function ensure_views(count, path)
  if #state.views == 0 then
    state.views[1] = make_view(path)
    state.views[1]:mount({ here = true })
  end
  if count == 2 and not state.views[2] then
    -- A vertical split opens to the left, so the view that was here shows the new version,
    -- and the new window the old one.
    local old = make_view(path .. " (old)")
    old:mount({ split = "vsplit" })
    state.views = { old, state.views[1] }
    old:focus()
    start_sync()
  elseif count == 1 and state.views[2] then
    stop_sync()
    state.views[2]:close()
    state.views[2] = nil
  end
end

-- The file's place the cursor is at, to find again in the next layout.
local function cursor_place()
  local win = state.views[1] and state.views[1]:winid()
  if not win then
    return nil
  end
  local row = cursor_row(win)
  for _, pane in ipairs(state.panes) do
    local cell = pane[row]
    if cell and (cell.old or cell.new) then
      return cell
    end
  end
  return nil
end

local function render(keep_cursor)
  local place = keep_cursor and cursor_place() or nil
  local generation = state.generation
  local context = not state.layout.all and state.layout.context or nil
  local visible = model.visible(state.lines, context)
  local side_by_side = state.layout.side_by_side
  local panes = side_by_side and model.side_by_side(visible) or model.unified(visible)
  state.panes = panes
  state.hunks = model.hunks(panes)
  ensure_views(#panes, state.spec.path)
  local width = digits(state.lines)
  local views = { table.unpack(state.views) }
  for i, view in ipairs(views) do
    view:set_lines(pane_text(panes[i]))
  end
  btv
    .wait_for(function()
      for _, view in ipairs(views) do
        if not view:bufnr() or not view:winid() then
          return false
        end
      end
      return true
    end, { tries = 400, interval = 5, message = "sourcegit-diff: the view never mounted" })
    :next(function()
      if generation ~= state.generation or state.panes ~= panes then
        return
      end
      for i, view in ipairs(views) do
        local win, buf = view:winid(), view:bufnr()
        view:set_decor(ns, pane_marks(panes[i], i, side_by_side, width))
        -- Panes line up row for row, which a wrapped line in one would undo.
        btv.wo[win].wrap = state.layout.wrap and not side_by_side
        btv.wo[win].list = state.layout.hidden_symbols
        btv.wo[win].number = false
        -- The gutter's numbers are the file's. Relative ones are for counts (`5j`), so
        -- they go beside them, as the preference says.
        btv.wo[win].relativenumber = btv.o.relativenumber
        btv.wo[win].signcolumn = "yes"
        -- Set by hand in `start_sync`: smooth scrolling would have the panes apart.
        btv.wo[win].scrollanim = not side_by_side
        -- Helix's modes have a keymap bucket of their own.
        for _, mode in ipairs({ "n", "helix" }) do
          btv.keymap.set(mode, "]c", function()
            M.go("next")
          end, { buffer = buf, desc = "sourcegit-diff: next hunk" })
          btv.keymap.set(mode, "[c", function()
            M.go("prev")
          end, { buffer = buf, desc = "sourcegit-diff: previous hunk" })
        end
      end
      paint_syntax()
      local row = place and model.row_of(panes[1], place.old, place.new) or nil
      if not row and side_by_side and place then
        row = model.row_of(panes[2], place.old, place.new)
      end
      row = row or state.hunks[1] or 1
      for _, view in ipairs(views) do
        btv.win.set_cursor(view:winid(), row, 0)
      end
      send("shown", { path = state.spec.path, lines = #panes[1], hunks = #state.hunks })
      state.position = nil
      report_position(row)
    end)
    :catch(function(e)
      send("error", { message = tostring(e) })
      error(e)
    end)
end

-- Colours for the diff showing, once both versions are parsed.
local function colour()
  local generation = state.generation
  local language = state.language
  if not language or not state.layout.syntax then
    return
  end
  local old, new = model.versions(state.lines)
  if #old > M.max_colored_bytes or #new > M.max_colored_bytes then
    return
  end
  local function by_line(spans)
    local out = {}
    for _, span in ipairs(spans) do
      if span.col_end > span.col_start then
        local line = span.line + 1
        out[line] = out[line] or {}
        table.insert(out[line], { span.col_start, span.col_end, span.group })
      end
    end
    return out
  end
  btv
    .async(function()
      local old_spans = btv.await(btv.treesitter.highlight(language, old))
      local new_spans = btv.await(btv.treesitter.highlight(language, new))
      if generation ~= state.generation then
        return
      end
      state.syntax = { old = by_line(old_spans), new = by_line(new_spans) }
      paint_syntax()
      send("colored", { path = state.spec.path, language = language })
    end)()
    :catch(function(e)
      send("error", { message = tostring(e) })
    end)
end

-- Shows `spec` ({ path, lines = { { kind, text, old, new, spans? } } }), in place of the diff
-- showing, at its first change, and says `shown` once it's on screen and `colored` once its
-- colours are.
function M.show(spec)
  check(spec)
  state.generation = state.generation + 1
  state.spec = spec
  state.lines = model.lines(spec)
  state.syntax = nil
  state.language = btv.filetype.match(spec.path)
  if not sign_colours_listening then
    sign_colours_listening = true
    sign_colours()
    btv.on("ColorScheme", sign_colours)
  end
  if not state.moved then
    state.moved = btv.autocmd.create("CursorMoved", {
      callback = function()
        report_position()
      end,
    })
  end
  render(false)
  if state.language and state.layout.syntax then
    M.on_language(state.language)
  end
  colour()
end

-- How the diff shows: { context = lines around each change, all = every line, side_by_side,
-- wrap, syntax, hidden_symbols }. Only what `spec` has changes. It takes effect at once, staying where the
-- cursor is, with what's already here: no call to git.
function M.layout(spec)
  assert(type(spec) == "table", "sourcegit-diff: the layout isn't a table")
  if spec.context ~= nil then
    assert(
      math.type(spec.context) == "integer" and spec.context >= 0,
      "sourcegit-diff: bad context: " .. tostring(spec.context)
    )
    state.layout.context = spec.context
  end
  if spec.all ~= nil then
    assert(type(spec.all) == "boolean", "sourcegit-diff: bad all: " .. tostring(spec.all))
    state.layout.all = spec.all
  end
  local was_syntax = state.layout.syntax
  for _, name in ipairs({ "side_by_side", "wrap", "syntax", "hidden_symbols" }) do
    if spec[name] ~= nil then
      assert(
        type(spec[name]) == "boolean",
        "sourcegit-diff: bad " .. name .. ": " .. tostring(spec[name])
      )
      state.layout[name] = spec[name]
    end
  end
  if state.lines then
    render(true)
    -- Turned on, what's showing may never have been parsed.
    if state.layout.syntax and not was_syntax then
      if state.language then
        M.on_language(state.language)
      end
      if not state.syntax then
        colour()
      end
    end
  end
end

-- A grammar for `language` arrived: the diff in it gets its colours now.
function M.recolor(language)
  if state.lines and state.language == language then
    colour()
  end
end

-- Takes the diff off the screen.
function M.close()
  stop_sync()
  if state.moved then
    btv.autocmd.del(state.moved)
    state.moved = nil
  end
  state.generation = state.generation + 1
  for _, view in ipairs(state.views) do
    view:close()
  end
  state.views, state.panes, state.hunks = {}, {}, {}
  state.spec, state.lines, state.syntax, state.language = nil, nil, nil, nil
end

-- `:SourceGitDiff {file}` in a plain terminal: there's no app to send it a diff.
function M.setup()
  if btv.host.name() then
    return
  end
  btv.command("SourceGitDiff", function(args)
    btv.async(function()
      M.show(btv.json.decode(btv.await(btv.fs.read_text(args.args))))
    end)()
  end, { nargs = 1, desc = "Show a diff from a JSON file, as the app sends it" })
end

return M
