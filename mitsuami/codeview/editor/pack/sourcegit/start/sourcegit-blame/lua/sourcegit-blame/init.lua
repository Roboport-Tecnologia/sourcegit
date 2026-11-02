-- SourceGit's blame: the file at a revision, with each run of lines from one commit headed by
-- its hash, author and time in the gutter. It's a real file's text, so the editor highlights
-- it by itself. Moving onto a line lights up the rest of its commit and tells the app, and
-- <CR> and `gb` ask the app to go to that commit, or to blame the file there.

local M = {}

-- Told the language each file is in, as `sourcegit-grammars` wants to know.
M.on_language = function(_) end

-- How wide an author's name gets, so one long name doesn't take the width of the text.
M.max_author_cells = 20

local ns = btv.ns.create("sourcegit-blame")
local ns_active = btv.ns.create("sourcegit-blame-active")

local view
local spec_showing
-- Which show this is, so a view that mounts late doesn't dress up an earlier one's file.
local generation = 0
local active
local moved

-- Tells the app, or nobody in a plain terminal. A field, so specs can hear it with no app.
M.send = function(name, payload)
  if btv.host.name() then
    btv.host.send(name, payload)
  end
end

local function send(name, payload)
  M.send(name, payload)
end

-- Raises on what the app got wrong, so a change on its side shows up here.
local function check(spec)
  assert(type(spec) == "table", "sourcegit-blame: the blame isn't a table")
  assert(type(spec.path) == "string", "sourcegit-blame: the blame has no path")
  assert(type(spec.text) == "string", "sourcegit-blame: the blame has no text")
  assert(type(spec.lines) == "table", "sourcegit-blame: the blame has no lines")
  for i, line in ipairs(spec.lines) do
    local where = "sourcegit-blame: line " .. i
    assert(type(line.sha) == "string", where .. " has no sha")
    assert(type(line.author) == "string", where .. " has no author")
    assert(type(line.time) == "string", where .. " has no time")
    assert(type(line.file) == "string", where .. " has no file")
    assert(type(line.first) == "boolean", where .. " doesn't say if it's first")
  end
end

-- `name` in exactly `cells` screen cells: cut with an ellipsis, or padded. Measured in cells,
-- so a wide name still lines up the times after it.
local function fit(name, cells)
  local width = btv.str.displaywidth(name)
  if width > cells then
    local out = {}
    local used = 0
    for _, char in utf8.codes(name) do
      local text = utf8.char(char)
      local w = btv.str.displaywidth(text)
      if used + w > cells - 1 then
        break
      end
      out[#out + 1] = text
      used = used + w
    end
    return table.concat(out) .. "…" .. string.rep(" ", cells - 1 - used)
  end
  return name .. string.rep(" ", cells - width)
end

-- A control character in a name would be refused by the column, and has no width to measure.
local function clean(author)
  return (author:gsub("%c", " "))
end

local function author_cells(lines)
  local cells = 1
  for _, line in ipairs(lines) do
    if line.first then
      cells = math.max(cells, btv.str.displaywidth(clean(line.author)))
    end
  end
  return math.min(cells, M.max_author_cells)
end

-- The heading of each run of a commit's lines.
local function gutter_marks(lines)
  local cells = author_cells(lines)
  local out = {}
  for i, line in ipairs(lines) do
    if line.first then
      out[#out + 1] = {
        line = i - 1,
        col = 0,
        gutter_text = {
          { line.sha .. " ", "Identifier" },
          { fit(clean(line.author), cells) .. " ", "Normal" },
          { line.time .. " ", "Comment" },
        },
      }
    end
  end
  return out
end

local function line_at(row)
  return spec_showing and spec_showing.lines[row]
end

-- Lights up the commit under the cursor, and tells the app: only as it changes, as this runs
-- for every move of the cursor and the marks are for every line of the commit.
local function follow_cursor()
  if not view or not view:winid() or btv.win.current() ~= view:winid() then
    return
  end
  local line = line_at(btv.win.call(view:winid(), btv.win.saveview).lnum)
  if not line or line.sha == active then
    return
  end
  active = line.sha
  local marks = {}
  for i, other in ipairs(spec_showing.lines) do
    if other.sha == line.sha then
      marks[#marks + 1] = { line = i - 1, col = 0, line_hl_group = "CursorLine", priority = 50 }
    end
  end
  view:set_decor(ns_active, marks)
  send("blame_line", { sha = line.sha, file = line.file })
end

local function ask(name)
  local row = btv.win.call(btv.win.current(), btv.win.saveview).lnum
  local line = line_at(row)
  if line then
    send(name, { sha = line.sha, file = line.file })
  end
end

local function lines_of(text)
  local out = {}
  for line in (text:gsub("\r\n", "\n"):gsub("\n$", "") .. "\n"):gmatch("(.-)\n") do
    out[#out + 1] = line
  end
  return out
end

-- Shows `spec` ({ path, text, lines = { { sha, author, time, file, first } } }), one entry per
-- line of `text`, from its first line, in place of what's showing. Says `shown` once it's on
-- screen, `blame_line` as the cursor reaches another commit's line, and `blame_goto` and
-- `blame_here` for <CR> and `gb`.
function M.show(spec)
  check(spec)
  generation = generation + 1
  local showing = generation
  spec_showing = spec
  active = nil
  local language = btv.filetype.match(spec.path)
  if not view then
    view = btv.view.create({ name = spec.path })
    view:on_close(function()
      view = nil
    end)
    view:mount({ here = true })
  end
  if not moved then
    -- A modeless session rests in Insert, which has an event of its own.
    moved = btv.autocmd.create({ "CursorMoved", "CursorMovedI" }, { callback = follow_cursor })
  end
  local shown = view
  shown:set_lines(lines_of(spec.text))
  btv
    .wait_for(function()
      return shown:bufnr() and shown:winid()
    end, { tries = 400, interval = 5, message = "sourcegit-blame: the view never mounted" })
    :next(function()
      if showing ~= generation then
        return
      end
      local win, buf = shown:winid(), shown:bufnr()
      btv.bo[buf].filetype = language or ""
      btv.wo[win].wrap = false
      btv.wo[win].number = true
      btv.wo[win].relativenumber = btv.o.relativenumber
      shown:set_decor(ns, gutter_marks(spec.lines))
      for _, mode in ipairs({ "n", "helix", "modeless" }) do
        btv.keymap.set(mode, "<CR>", function()
          ask("blame_goto")
        end, { buffer = buf, desc = "sourcegit-blame: go to this line's commit" })
        btv.keymap.set(mode, "gb", function()
          ask("blame_here")
        end, { buffer = buf, desc = "sourcegit-blame: blame the file at this line's commit" })
      end
      btv.win.set_cursor(win, 1, 0)
      follow_cursor()
      send("shown", { path = spec.path, lines = #spec.lines, language = language })
    end)
    :catch(function(e)
      send("error", { message = tostring(e) })
      error(e)
    end)
  if language then
    M.on_language(language)
  end
end

-- Takes the blame off the screen.
function M.close()
  generation = generation + 1
  if moved then
    btv.autocmd.del(moved)
    moved = nil
  end
  if view then
    view:close()
    view = nil
  end
  spec_showing, active = nil, nil
end

return M
