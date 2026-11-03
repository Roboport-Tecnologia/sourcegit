-- The change the cursor is in, going to one, and a history's revisions keeping their places, on
-- the fixture with changes at lines 5, 130 and 250.
local diff = require("sourcegit-diff")
local expect = btv.test.expect

local dir = debug.getinfo(1, "S").source:match("^@(.*)/test/[^/]+$")
local fixture = dir .. "/../../../../../fixtures/edit.json"

local function spec_of()
  return btv.json.decode(btv.await(btv.fs.read_text(fixture)))
end

local function view()
  return btv.win.call(btv.win.current(), btv.win.saveview)
end

-- A file's line on the screen, as views scroll.
local function on_screen(line)
  return line - view().topline + 1
end

-- The style of a line's background as painted: `t:highlights` has spans, not these. A frame
-- numbers its own styles, so they only compare within one.
local function background(line)
  for _, place in ipairs(btv._ui.line_bg or {}) do
    if place[1] + 1 == on_screen(line) then
      return place[2]
    end
  end
  return nil
end

btv.test.describe("sourcegit-diff's current change", function()
  local shown_count

  btv.test.before_each(function()
    diff.close()
    diff.forget_places()
    diff.layout({ context = 4, all = false, side_by_side = false, wrap = false, syntax = false })
    btv.helix.disable()
    btv.o.modeless = false
    diff.on_language = function(_) end
    diff.hint_seen = true
    shown_count = 0
    diff.send = function(name)
      if name == "shown" then
        shown_count = shown_count + 1
      end
    end
  end)

  local function show(t, spec)
    local before = shown_count
    diff.show(spec)
    t:wait_for(function()
      return shown_count > before
    end, { message = "the diff never showed" })
    t:sleep(20)
  end

  -- With 4 lines of context, the second change's one line is on row 17.
  local SECOND = 17

  btv.test.it("colours every change, stronger in the current one", function(t)
    show(t, spec_of())
    t:wait_for(function()
      return background(5) ~= nil and background(SECOND) ~= nil
    end, { message = "the changes have no colour" })
    t:wait_for(function()
      return background(5) ~= background(SECOND)
    end, { message = "the first doesn't stand out" })
    -- Lines that didn't change have none.
    expect(background(4)).to_equal(nil)
    btv.win.set_cursor(btv.win.current(), SECOND, 0)
    t:sleep(40)
    expect(background(5) ~= background(SECOND)).to_equal(true)
    btv.win.set_cursor(btv.win.current(), 2, 0)
    t:wait_for(function()
      return background(5) == background(SECOND)
    end, { message = "it stayed marked outside any change" })
  end)

  btv.test.it("has grey only on the current change's filler", function(t)
    diff.layout({ side_by_side = true })
    show(t, spec_of())
    -- The first change has one line old and two new: the old pane's row 6 is filler.
    t:wait_for(function()
      return background(6) ~= nil
    end, { message = "the current change's filler has no background" })
    btv.win.set_cursor(btv.win.current(), 2, 0)
    t:wait_for(function()
      return background(6) == nil
    end, { message = "filler outside the current change has a background" })
  end)

  btv.test.it("puts the change it goes to at the top, in every pane", function(t)
    diff.layout({ all = true, side_by_side = true })
    show(t, spec_of())
    diff.go("next")
    local row
    t:wait_for(function()
      row = btv.line(".")
      return row > 100
    end, { message = "never moved" })
    t:wait_for(function()
      for _, win in ipairs(btv.win.list()) do
        if btv.win.call(win, btv.win.saveview).topline ~= row then
          return false
        end
      end
      return true
    end, { message = "not at the top" })
    t:sleep(100)
    expect(view().topline).to_equal(row)
  end)

  btv.test.describe("with a place to remember", function()
    local function revision(key)
      local spec = spec_of()
      spec.place = key
      return spec
    end

    local function scrolled_to(t, row, top)
      btv.win.set_cursor(btv.win.current(), row, 0)
      btv.win.set_topline(btv.win.current(), top)
      t:wait_for(function()
        return view().topline == top and btv.line(".") == row
      end, { message = "never scrolled" })
    end

    btv.test.before_each(function()
      diff.layout({ all = true })
    end)

    btv.test.it("starts a new one at the top of the file", function(t)
      show(t, revision("a"))
      scrolled_to(t, 100, 90)
      show(t, revision("b"))
      t:wait_for(function()
        return btv.line(".") == 1 and view().topline == 1
      end, { message = "it kept the last one's place" })
    end)

    btv.test.it("comes back to each one where it was", function(t)
      show(t, revision("a"))
      scrolled_to(t, 100, 90)
      show(t, revision("b"))
      scrolled_to(t, 40, 35)
      show(t, revision("a"))
      t:wait_for(function()
        return btv.line(".") == 100 and view().topline == 90
      end, { message = "the first lost its place" })
      show(t, revision("b"))
      t:wait_for(function()
        return btv.line(".") == 40 and view().topline == 35
      end, { message = "the second lost its place" })
    end)

    btv.test.it("doesn't give a place to one that never showed", function(t)
      show(t, revision("a"))
      scrolled_to(t, 100, 90)
      -- Picked past before it showed: its place isn't where the first was.
      diff.show(revision("skipped"))
      show(t, revision("b"))
      show(t, revision("skipped"))
      t:wait_for(function()
        return btv.line(".") == 1 and view().topline == 1
      end, { message = "it took another's place" })
    end)
  end)

  btv.test.it("without a place, the same file keeps the cursor", function(t)
    diff.layout({ all = true })
    show(t, spec_of())
    btv.win.set_cursor(btv.win.current(), 100, 0)
    t:sleep(20)
    show(t, spec_of())
    expect(btv.line(".")).to_equal(100)
  end)
end)
