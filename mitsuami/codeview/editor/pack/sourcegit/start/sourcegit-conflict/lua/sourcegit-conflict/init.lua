-- SourceGit's merge editor: a conflicted file's two sides over the result, in one editor's three
-- panes, which line up row for row. The app owns the file and what's been chosen: this shows
-- what it says, and tells it what the user chose. A choice comes back as a new `show`.

local M = {}

-- How much of a pane gets parsed for colours, as in `sourcegit-diff`.
M.max_colored_bytes = 1024 * 1024

M.filler = "╌"

-- Told the language each file is in, as `sourcegit-grammars` wants to know.
M.on_language = function(_) end

-- Tells the app, or nobody in a plain terminal. A field, so specs can hear it with no app.
M.send = function(name, payload)
  if btv.host.name() then
    btv.host.send(name, payload)
  end
end

local ns = btv.ns.create("sourcegit-conflict")
local ns_syntax = btv.ns.create("sourcegit-conflict-syntax")
local ns_active = btv.ns.create("sourcegit-conflict-active")

local KINDS = {
  common = {},
  ours = { line = "SourceGitConflictOurs", sign = "-", sign_group = "Removed" },
  theirs = { line = "SourceGitConflictTheirs", sign = "+", sign_group = "Added" },
  marker = {},
}

-- The conflict the cursor is in stands out across the panes, as the toolbar's buttons act on it
-- and a click there leaves the editor with no cursor to show it: its sides' colours stronger, and
-- a very subtle grey on the rest of its rows. Other conflicts have only their sides' faint colours.
local ACTIVE = {
  none = "SourceGitConflictActive",
  common = "SourceGitConflictActive",
  ours = "SourceGitConflictOursActive",
  theirs = "SourceGitConflictTheirsActive",
  marker = "SourceGitConflictActive",
}

-- From the theme's own colours, and again when it changes. A theme can set them itself.
local function colours()
  local p = btv.hl.palette()
  local function tint(name, colour, ratio)
    btv.hl.fallback("SourceGitConflict" .. name, { bg = btv.hl.blend(colour, p.bg, ratio) })
  end
  -- As a diff's, to go with the signs.
  tint("Ours", p.red, 0.08)
  tint("OursActive", p.red, 0.3)
  tint("Theirs", p.green, 0.08)
  tint("TheirsActive", p.green, 0.3)
  tint("Active", p.fg, 0.04)
  -- As `sourcegit-diff`'s signs: a sign with no colour draws in the client's default, red.
  btv.hl.fallback("Added", { fg = p.green })
  btv.hl.fallback("Removed", { fg = p.red })
end

local colours_listening = false

-- What can be chosen, as the app's names for them, with their keys.
local CHOICES = {
  { name = "ours", key = "gm" },
  { name = "theirs", key = "gt" },
  { name = "both_mine_first", key = "gb" },
  { name = "both_theirs_first", key = "gB" },
  { name = "undo", key = "gu" },
}

local state = {
  spec = nil,
  views = {},
  syntax = nil,
  language = nil,
  generation = 0,
  autocmds = {},
  syncing = false,
  echoes = {},
  at = false,
}

local function cursor_row(win)
  return btv.win.call(win, btv.win.saveview).lnum
end

local function ours(win)
  for _, view in ipairs(state.views) do
    if view:winid() == win then
      return true
    end
  end
  return false
end

-- The region the row is in, and its 1-based index.
local function region_at(row)
  for i, region in ipairs(state.spec and state.spec.regions or {}) do
    if row >= region.first and row <= region.last then
      return region, i
    end
  end
  return nil
end

-- Marks the region in every pane; nil for none.
local function paint_active(region)
  local panes = { state.spec.ours, state.spec.theirs, state.spec.result }
  for i, view in ipairs(state.views) do
    local marks = {}
    if region then
      for row = region.first, region.last do
        local line = row - 1
        marks[#marks + 1] = {
          line = line,
          col = 0,
          line_hl_group = ACTIVE[panes[i][row].kind],
          priority = 120,
        }
      end
    end
    if view:bufnr() then
      view:set_decor(ns_active, marks)
    end
  end
end

-- Tells the app which region the cursor is in, for its buttons, as it changes. `row` is where
-- it's on its way to: a move is queued, and reading it now would say where it was.
local function report(row, force)
  local win = btv.win.current()
  if not state.spec or not ours(win) then
    return
  end
  local region, index = region_at(row or cursor_row(win))
  local at = region and (index .. (region.resolved and "r" or "u")) or "none"
  if at ~= state.at or force then
    state.at = at
    paint_active(region)
    M.send("conflict_at", {
      index = region and index - 1 or -1,
      resolved = region ~= nil and region.resolved,
    })
  end
end

-- Keeps the panes together by the row. This is `sourcegit-diff`'s scroll lock: plugins load
-- alone in their specs, so they don't share it.
local function stop_sync()
  for _, id in ipairs(state.autocmds) do
    btv.autocmd.del(id)
  end
  state.autocmds = {}
end

local function start_sync()
  stop_sync()
  state.echoes = {}
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
  local function guarded(body)
    state.syncing = true
    local ok, err = pcall(body)
    state.syncing = false
    if not ok then
      M.send("error", { message = tostring(err) })
    end
  end
  state.autocmds[1] = btv.autocmd.create("WinScrolled", {
    callback = function(args)
      local win = tonumber(args.match)
      if state.syncing or not win or not ours(win) then
        return
      end
      guarded(function()
        local from = btv.win.call(win, btv.win.saveview)
        -- A pane set here says so a tick later: passing that on would undo a later notch.
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
  -- A modeless session rests in Insert, which has an event of its own.
  state.autocmds[2] = btv.autocmd.create({ "CursorMoved", "CursorMovedI" }, {
    callback = function()
      local win = btv.win.current()
      if state.syncing or not ours(win) then
        return
      end
      guarded(function()
        local at = btv.win.call(win, btv.win.saveview)
        for _, other in ipairs(others(win)) do
          local there = btv.win.call(other, btv.win.saveview)
          if there.lnum ~= at.lnum or there.col ~= at.col then
            btv.win.set_cursor(other, at.lnum, at.col)
          end
        end
        report(at.lnum)
      end)
    end,
  })
end

local function pane_text(pane)
  local out = {}
  for i, row in ipairs(pane) do
    out[i] = row.text
  end
  return out
end

local function pane_marks(pane, width)
  local out = {}
  for i, row in ipairs(pane) do
    local line = i - 1
    if row.kind == "none" then
      out[#out + 1] = {
        line = line,
        col = 0,
        line_fill = { text = M.filler, hl_group = "NonText" },
        priority = 100,
      }
    else
      local number = string.format("%" .. width .. "s ", row.number or "")
      out[#out + 1] = { line = line, col = 0, gutter_text = { { number, "LineNr" } } }
      if KINDS[row.kind].line then
        out[#out + 1] =
          { line = line, col = 0, line_hl_group = KINDS[row.kind].line, priority = 100 }
      end
      if KINDS[row.kind].sign then
        out[#out + 1] = {
          line = line,
          col = 0,
          sign_text = KINDS[row.kind].sign,
          sign_hl_group = KINDS[row.kind].sign_group,
          priority = 100,
        }
      end
    end
  end
  return out
end

-- A pane's lines as a file to parse, and which row each is on. Markers and filler aren't code.
local function version_of(pane)
  local lines, rows = {}, {}
  for i, row in ipairs(pane) do
    if row.kind ~= "none" and row.kind ~= "marker" then
      lines[#lines + 1] = row.text
      rows[#lines] = i
    end
  end
  return table.concat(lines, "\n") .. "\n", rows
end

local function paint_syntax()
  if not state.syntax then
    return
  end
  for i, view in ipairs(state.views) do
    local marks = {}
    for _, span in ipairs(state.syntax[i] or {}) do
      marks[#marks + 1] = {
        line = span[1] - 1,
        col = span[2],
        end_row = span[1] - 1,
        end_col = span[3],
        hl_group = "@" .. span[4],
        priority = 150,
      }
    end
    if view:bufnr() then
      view:set_decor(ns_syntax, marks)
    end
  end
end

local function colour()
  local generation, language, spec = state.generation, state.language, state.spec
  if not language then
    return
  end
  btv
    .async(function()
      local out = {}
      for i, pane in ipairs({ spec.ours, spec.theirs, spec.result }) do
        local text, rows = version_of(pane)
        out[i] = {}
        if #text <= M.max_colored_bytes then
          for _, span in ipairs(btv.await(btv.treesitter.highlight(language, text))) do
            local row = rows[span.line + 1]
            if row and span.col_end > span.col_start then
              table.insert(out[i], { row, span.col_start, span.col_end, span.group })
            end
          end
        end
      end
      if generation ~= state.generation then
        return
      end
      state.syntax = out
      paint_syntax()
      M.send("colored", { path = spec.path, language = language })
    end)()
    :catch(function(e)
      M.send("error", { message = tostring(e) })
    end)
end

-- Tells the app what was chosen for the region under the cursor. A region that's resolved can
-- only be undone, and one that isn't only chosen, as upstream's.
function M.choose(name)
  local win = btv.win.current()
  local region, index = region_at(cursor_row(win))
  if not region then
    btv.notify("No conflict here")
    return
  end
  if (name == "undo") ~= region.resolved then
    btv.notify(region.resolved and "This conflict is resolved: undo it first" or "Nothing to undo here")
    return
  end
  M.send("conflict_choose", { index = index - 1, choice = name })
end

-- Moves to the next or previous conflict still open. Past either end it stays.
function M.go(where)
  assert(where == "next" or where == "prev", "sourcegit-conflict: can't go to: " .. tostring(where))
  local win = btv.win.current()
  local at = cursor_row(win)
  local target
  for _, region in ipairs(state.spec.regions) do
    if not region.resolved then
      if where == "next" and region.first > at then
        target = target or region.first
      elseif where == "prev" and region.last < at then
        target = region.first
      end
    end
  end
  if target then
    -- At the top of the pane, as `zt`, with as much of it as fits below.
    -- The cursor first, as a cursor out of its view scrolls the view back to it.
    btv.win.set_cursor(win, target, 0)
    btv.win.set_topline(win, target)
    report(target)
  end
end

-- The app's calls.
function M.go_to(spec)
  M.go(spec.where)
end

function M.choose_here(spec)
  M.choose(spec.choice)
end

-- This file's rows in the right-click menu, with the app's labels.
btv.contextmenu.groups["sourcegit-conflict"] = {
  when = function()
    return state.spec ~= nil and ours(btv.win.current())
  end,
  items = function()
    local out = {}
    for _, choice in ipairs(CHOICES) do
      out[#out + 1] = {
        label = state.spec.labels[choice.name],
        detail = choice.key,
        enabled = function()
          local region = region_at(btv.line("."))
          return region ~= nil and (choice.name == "undo") == region.resolved
        end,
        action = function()
          M.choose(choice.name)
        end,
      }
    end
    return out
  end,
}

local function closed()
  local had = #state.views
  stop_sync()
  state.views, state.spec = {}, nil
  if had > 0 then
    btv.cmd("qa!")
  end
end

local function make_view(name)
  local view = btv.view.create({ name = name })
  view:on_close(closed)
  return view
end

-- The two sides over the result. New windows open left of and above the current one, so the
-- result's takes the window that's here, and the rest split from it.
local function ensure_views(spec)
  if #state.views > 0 then
    return
  end
  local result = make_view(spec.labels.result .. ": " .. spec.path)
  result:mount({ here = true })
  local theirs = make_view(spec.labels.theirs_pane)
  theirs:mount({ split = "split" })
  local mine = make_view(spec.labels.mine_pane)
  mine:mount({ split = "vsplit" })
  state.views = { mine, theirs, result }
  start_sync()
end

local function check(spec)
  assert(type(spec) == "table", "sourcegit-conflict: the conflict isn't a table")
  assert(type(spec.path) == "string", "sourcegit-conflict: no path")
  for _, name in ipairs({ "ours", "theirs", "result" }) do
    assert(type(spec[name]) == "table", "sourcegit-conflict: no " .. name)
    assert(#spec[name] == #spec.ours, "sourcegit-conflict: the panes don't line up: " .. name)
    for i, row in ipairs(spec[name]) do
      assert(
        row.kind == "none" or KINDS[row.kind],
        "sourcegit-conflict: unknown kind on line " .. i .. ": " .. tostring(row.kind)
      )
    end
  end
  assert(type(spec.regions) == "table", "sourcegit-conflict: no regions")
  assert(type(spec.labels) == "table", "sourcegit-conflict: no labels")
end

-- Shows `spec`: { path, ours, theirs, result = { { kind, text, number? } }, regions = { { first,
-- last, resolved } }, labels }, and says `shown`. The same file again is a choice made: the
-- cursor stays. A new file starts at its first conflict.
function M.show(spec)
  check(spec)
  local same_file = state.spec ~= nil and state.spec.path == spec.path
  state.generation = state.generation + 1
  local generation = state.generation
  state.spec = spec
  state.syntax = nil
  state.language = btv.filetype.match(spec.path)
  if not colours_listening then
    colours_listening = true
    colours()
    btv.on("ColorScheme", colours)
  end
  ensure_views(spec)
  local views = { table.unpack(state.views) }
  local panes = { spec.ours, spec.theirs, spec.result }
  for i, view in ipairs(views) do
    view:set_lines(pane_text(panes[i]))
  end
  local width = #tostring(#spec.ours)
  btv
    .wait_for(function()
      for _, view in ipairs(views) do
        if not view:bufnr() or not view:winid() then
          return false
        end
      end
      return true
    end, { tries = 400, interval = 5, message = "sourcegit-conflict: the view never mounted" })
    :next(function()
      if generation ~= state.generation then
        return
      end
      for i, view in ipairs(views) do
        local win, buf = view:winid(), view:bufnr()
        view:set_decor(ns, pane_marks(panes[i], width))
        btv.wo[win].wrap = false
        btv.wo[win].number = false
        btv.wo[win].relativenumber = btv.o.relativenumber
        -- Always there, so the panes don't shift as a choice takes the last sign away.
        btv.wo[win].signcolumn = "yes"
        -- Set by hand in `start_sync`: smooth scrolling would have the panes apart.
        btv.wo[win].scrollanim = false
        for _, mode in ipairs({ "n", "helix", "modeless" }) do
          for _, choice in ipairs(CHOICES) do
            btv.keymap.set(mode, choice.key, function()
              M.choose(choice.name)
            end, { buffer = buf, desc = "sourcegit-conflict: " .. choice.name })
          end
          btv.keymap.set(mode, "]c", function()
            M.go("next")
          end, { buffer = buf, desc = "sourcegit-conflict: next conflict" })
          btv.keymap.set(mode, "[c", function()
            M.go("prev")
          end, { buffer = buf, desc = "sourcegit-conflict: previous conflict" })
        end
        btv.contextmenu.set(
          buf,
          { "sourcegit-conflict", table.unpack(btv.contextmenu.default_readonly) }
        )
      end
      paint_syntax()
      local row
      if not same_file then
        row = spec.regions[1] and spec.regions[1].first or 1
        for _, view in ipairs(views) do
          btv.win.set_cursor(view:winid(), row, 0)
        end
      end
      M.send("shown", { path = spec.path, lines = #spec.ours, regions = #spec.regions })
      report(row, true)
    end)
    :catch(function(e)
      M.send("error", { message = tostring(e) })
      error(e)
    end)
  if state.language then
    M.on_language(state.language)
  end
  colour()
end

-- A grammar for `language` arrived.
function M.recolor(language)
  if state.spec and state.language == language then
    colour()
  end
end

-- Takes the conflict off the screen, for specs.
function M.close()
  stop_sync()
  state.generation = state.generation + 1
  local views = state.views
  state.views, state.spec, state.syntax, state.language = {}, nil, nil, nil
  for _, view in ipairs(views) do
    view:on_close(function() end)
    view:close()
  end
end

return M
