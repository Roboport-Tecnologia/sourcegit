-- A file as a revision has it, read-only. It's a real file's text in one view, so the
-- editor highlights it by itself, and as a grammar arrives.

local M = {}

-- Told the language each file is in, as `sourcegit-grammars` wants to know.
M.on_language = function(_) end

local view
-- Which show this is, so a view that mounts late doesn't dress up an earlier one's file.
local generation = 0
-- Where each `place` was left, while this editor lives.
local places = {}
-- The `place` of what's on screen, once it is.
local on_screen

function M.forget_places()
  places = {}
  on_screen = nil
end

-- Git's text as lines: a CRLF file shows no `^M`, and an ending newline is no line of its own.
local function lines_of(text)
  local out = {}
  for line in (text:gsub("\r\n", "\n"):gsub("\n$", "") .. "\n"):gmatch("(.-)\n") do
    out[#out + 1] = line
  end
  return out
end

-- Shows `spec` ({ path, text, place? }) in place of the file showing, from its first line, or,
-- with a `place` it's seen before, as that was left.
function M.show(spec)
  assert(type(spec) == "table", "sourcegit-file: the file isn't a table")
  assert(type(spec.path) == "string", "sourcegit-file: the file has no path")
  assert(type(spec.text) == "string", "sourcegit-file: the file has no text")
  generation = generation + 1
  local showing = generation
  if view and on_screen and view:winid() then
    local at = btv.win.call(view:winid(), btv.win.saveview)
    places[on_screen] = { row = at.lnum, top = at.topline }
  end
  on_screen = nil
  local language = btv.filetype.match(spec.path)
  if not view then
    view = btv.view.create({ name = spec.path })
    view:on_close(function()
      view = nil
    end)
    view:mount({ here = true })
  end
  local shown = view
  shown:set_lines(lines_of(spec.text))
  btv
    .wait_for(function()
      return shown:bufnr() and shown:winid()
    end, { tries = 400, interval = 5, message = "sourcegit-file: the view never mounted" })
    :next(function()
      if showing ~= generation then
        return
      end
      local win = shown:winid()
      btv.bo[shown:bufnr()].filetype = language or ""
      btv.wo[win].wrap = false
      btv.wo[win].number = true
      btv.wo[win].relativenumber = btv.o.relativenumber
      -- The cursor first, as a cursor out of its view scrolls the view back to it.
      local place = places[spec.place]
      btv.win.set_cursor(win, place and place.row or 1, 0)
      btv.win.set_topline(win, place and place.top or 1)
      on_screen = spec.place
      if btv.host.name() then
        btv.host.send("shown", { path = spec.path, language = language })
      end
    end)
    :catch(function(e)
      if btv.host.name() then
        btv.host.send("error", { message = tostring(e) })
      end
      error(e)
    end)
  if language then
    M.on_language(language)
  end
end

-- Takes the file off the screen.
function M.close()
  generation = generation + 1
  if view then
    view:close()
    view = nil
    on_screen = nil
  end
end

return M
