-- Modeless is the port's default, and a read-only buffer has no Normal mode there: what the
-- diff answers to has to be bound for it, and what the layout's chords do must not hurt it.
local diff = require("sourcegit-diff")
local expect = btv.test.expect

local dir = debug.getinfo(1, "S").source:match("^@(.*)/test/[^/]+$")
local fixture = dir .. "/../../../../../fixtures/edit.json"

local eprintln = '    eprintln!("{}", compute());'

local sent

local function said(name)
  local out = {}
  for _, entry in ipairs(sent) do
    if entry[1] == name then
      out[#out + 1] = entry[2]
    end
  end
  return out
end

local function shown(t, side_by_side)
  local spec = btv.json.decode(btv.await(btv.fs.read_text(fixture)))
  spec.actions = {
    { action = "stage", label = "Stage" },
    { action = "unstage", label = "Unstage" },
    { action = "discard", label = "Discard" },
  }
  diff.layout({ side_by_side = side_by_side or false })
  diff.show(spec)
  t:wait_for(function()
    return #said("shown") > 0
  end, { message = "the diff never showed" })
  t:sleep(20)
  return spec
end

local function row_of(t, text)
  for row, line in ipairs(t:lines()) do
    if line == text then
      return row
    end
  end
  error("no line: " .. text)
end

local function top(win)
  return btv.win.call(win, btv.win.saveview).topline
end

local function chunk(t, n)
  return t:wait_for(function()
    return said("chunk")[n or 1]
  end, { message = "no chunk was sent" })
end

btv.test.describe("sourcegit-diff, modeless", function()
  btv.test.before_each(function(t)
    diff.close()
    sent = {}
    diff.send = function(name, payload)
      sent[#sent + 1] = { name, payload }
    end
    diff.on_language = function(_) end
    diff.hint_seen = true
    btv.helix.disable()
    btv.o.modeless = true
    t:sleep(20)
  end)

  btv.test.it("rests in Insert on the diff", function(t)
    shown(t)
    expect(btv.mode().mode).to_equal("i")
  end)

  btv.test.it("]c and [c go from change to change", function(t)
    shown(t)
    expect(t:cursor()[1]).to_be(5)
    t:feed("]c")
    t:wait_for(function()
      return t:cursor()[1] > 5
    end, { message = "]c didn't move" })
    t:feed("[c")
    t:wait_for(function()
      return t:cursor()[1] == 5
    end, { message = "[c didn't come back" })
  end)

  btv.test.it("tells the toolbar where the caret went", function(t)
    local spec = shown(t)
    local at = function()
      local last
      for _, entry in ipairs(sent) do
        if entry[1] == "change_position" then
          last = entry[2].at
        end
      end
      return last
    end
    t:wait_for(function()
      return at() == 1
    end, { message = "no first position" })
    diff.go_to({ where = "next" })
    t:wait_for(function()
      return at() == 2
    end, { message = "the toolbar's button didn't tell" })
    t:feed("<Up>")
    t:wait_for(function()
      return at() == 1
    end, { message = "the arrow didn't tell" })
    expect(spec ~= nil).to_be(true)
  end)

  btv.test.it("gs takes the run under the caret, with nothing selected", function(t)
    shown(t)
    btv.win.set_cursor(0, row_of(t, eprintln), 0)
    t:feed("gs")
    local c = chunk(t)
    expect(c.action).to_equal("stage")
    expect(c.first).to_be(5)
    expect(c.last).to_be(7)
  end)

  btv.test.it("gu and gx take what Shift and the arrows selected, then drop it", function(t)
    shown(t)
    btv.win.set_cursor(0, row_of(t, eprintln), 0)
    t:feed("<S-Down><S-Down>")
    t:wait_for(function()
      return btv.mode().mode == "s"
    end, { message = "Shift-Down didn't select" })
    t:feed("gu")
    local c = chunk(t)
    expect(c.action).to_equal("unstage")
    expect(c.first).to_be(6)
    expect(c.last).to_be(7)
    t:wait_for(function()
      return btv.mode().mode == "i"
    end, { message = "the selection stayed" })
    -- Upward from the start of a line, which leaves that line out.
    btv.win.set_cursor(0, row_of(t, eprintln) + 1, 0)
    t:feed("<S-Up>")
    t:wait_for(function()
      return btv.mode().mode == "s"
    end, { message = "Shift-Up didn't select" })
    t:feed("gx")
    c = chunk(t, 2)
    expect(c.action).to_equal("discard")
    expect(c.first).to_be(6)
    expect(c.last).to_be(6)
  end)

  btv.test.it("the right-click menu acts on a selection", function(t)
    shown(t)
    local row = row_of(t, eprintln)
    btv.win.set_cursor(0, row, 0)
    t:feed("<S-Down><S-Down>")
    t:sleep(20)
    t:mouse("right", "press", row - 1, btv.win.textoff(btv.win.current()) + 6)
    local menu = t:wait_for(function()
      local m = t:menu()
      return m and #m.items > 0 and m or nil
    end, { message = "no menu opened" })
    expect(menu.items[1]).to_equal("Stage")
    t:feed("<Down><CR>")
    local c = chunk(t)
    expect(c.first).to_be(6)
    expect(c.last).to_be(7)
  end)

  btv.test.it("typing changes nothing", function(t)
    shown(t)
    local before = t:lines()
    t:feed("x<CR><BS>")
    t:sleep(20)
    expect(t:lines()).to_equal(before)
  end)

  -- `sourcegit-settings` frees the chords that would close it.
  for _, key in ipairs({ "<C-x>", "<C-v>", "<C-z>" }) do
    btv.test.it(key .. " leaves the diff showing", function(t)
      shown(t)
      local before, buf = t:lines(), btv.buf.current()
      t:feed(key)
      t:sleep(50)
      expect(btv.buf.current()).to_be(buf)
      expect(t:lines()).to_equal(before)
      expect(#said("error")).to_be(0)
    end)
  end

  btv.test.describe("side by side", function()
    local function together(t, wins)
      t:wait_for(function()
        return top(wins[1]) > 1 and top(wins[1]) == top(wins[2])
      end, { message = "the panes came apart" })
    end

    local function panes(t)
      shown(t, true)
      diff.layout({ side_by_side = true, all = true })
      t:wait_for(function()
        return #btv.win.list() == 2 and #t:lines() > 100
      end, { message = "no panes" })
      t:sleep(20)
      return btv.win.list()
    end

    btv.test.it("the arrows past a screen keep the panes together", function(t)
      local wins = panes(t)
      t:feed(string.rep("<Down>", 80))
      together(t, wins)
    end)

    btv.test.it("the wheel moves both panes and leaves the caret", function(t)
      local wins = panes(t)
      local row = t:cursor()[1]
      for _ = 1, 40 do
        t:mouse("wheel", "down", 2, 60)
      end
      together(t, wins)
      t:sleep(60)
      expect(top(wins[1])).to_be(top(wins[2]))
      expect(t:cursor()[1]).to_be(row)
    end)
  end)
end)
