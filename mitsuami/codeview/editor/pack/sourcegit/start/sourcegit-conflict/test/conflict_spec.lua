local conflict = require("sourcegit-conflict")
local expect = btv.test.expect

local LABELS = {
  ours = "Use Mine",
  theirs = "Use Theirs",
  both_mine_first = "Mine then Theirs",
  both_theirs_first = "Theirs then Mine",
  undo = "Undo",
  result = "RESULT",
  mine_pane = "MINE",
  theirs_pane = "THEIRS",
}

local function row(kind, text, number)
  return { kind = kind, text = text or "", number = number }
end

-- Two conflicts around common lines, unresolved.
local function spec(result)
  return {
    path = "a.lua",
    labels = LABELS,
    ours = {
      row("common", "top", 1),
      row("none"),
      row("ours", "mine", 2),
      row("none"),
      row("none"),
      row("none"),
      row("common", "middle", 3),
      row("none"),
      row("ours", "mine two", 4),
      row("none"),
      row("none"),
      row("none"),
    },
    theirs = {
      row("common", "top", 1),
      row("none"),
      row("none"),
      row("none"),
      row("theirs", "theirs", 2),
      row("none"),
      row("common", "middle", 3),
      row("none"),
      row("none"),
      row("none"),
      row("theirs", "theirs two", 4),
      row("none"),
    },
    result = result or {
      row("common", "top", 1),
      row("marker", "<<<<<<< HEAD"),
      row("ours", "mine", 2),
      row("marker", "======="),
      row("theirs", "theirs", 3),
      row("marker", ">>>>>>> other"),
      row("common", "middle", 4),
      row("marker", "<<<<<<< HEAD"),
      row("ours", "mine two", 5),
      row("marker", "======="),
      row("theirs", "theirs two", 6),
      row("marker", ">>>>>>> other"),
    },
    regions = {
      { first = 2, last = 6, resolved = false },
      { first = 8, last = 12, resolved = false },
    },
  }
end

btv.test.describe("sourcegit-conflict", function()
  local heard

  local function said(name)
    local out = {}
    for _, e in ipairs(heard) do
      if e[1] == name then
        out[#out + 1] = e[2]
      end
    end
    return out
  end

  local function shown(t, s)
    local before = #said("shown")
    conflict.show(s or spec())
    t:wait_for(function()
      return #said("shown") > before
    end, { message = "it never showed" })
  end

  btv.test.before_each(function()
    heard = {}
    conflict.send = function(name, payload)
      heard[#heard + 1] = { name, payload }
    end
    conflict.on_language = function(_) end
  end)

  btv.test.after_each(function()
    conflict.close()
  end)

  btv.test.it("shows three panes, mine and theirs over the result", function(t)
    shown(t)
    expect(#btv.win.list()).to_equal(3)
    -- Positions aren't told, and only the focused pane is on screen: by each one's size.
    local sizes = {}
    for _, win in ipairs(btv.win.list()) do
      local lines = btv.buf.lines(btv.win.buf(win), 0, -1, false)
      local name = lines[2] ~= "" and "result" or lines[3] == "mine" and "mine" or "theirs"
      sizes[name] = { width = btv.win.width(win), height = btv.win.height(win) }
    end
    expect(sizes.mine.height).to_equal(sizes.theirs.height)
    -- Side by side, with a divider between them, and together as wide as the result.
    expect(sizes.mine.width + sizes.theirs.width + 1).to_equal(sizes.result.width)
    -- The cursor starts in mine, which `split` then `vsplit` leave current.
    expect(t:lines()[3]).to_equal("mine")
  end)

  btv.test.it("starts at the first conflict and says so", function(t)
    shown(t)
    t:wait_for(function()
      return #said("conflict_at") > 0
    end, { message = "no position" })
    local last = said("conflict_at")[#said("conflict_at")]
    expect(last.index).to_equal(0)
    expect(last.resolved).to_equal(false)
    expect(btv.line(".")).to_equal(2)
  end)

  btv.test.it("refuses panes that don't line up", function()
    local bad = spec()
    table.remove(bad.result)
    expect(pcall(conflict.show, bad)).to_equal(false)
  end)

  -- Each mode's bucket of keys, Helix last: its reset can leave the next test's modeless off.
  for _, mode in ipairs({ "vim", "modeless", "helix" }) do
    btv.test.it("chooses by key in " .. mode, function(t)
      btv.o.modeless = mode == "modeless"
      if mode == "helix" then
        btv.helix.enable()
      end
      shown(t)
      t:sleep(20)
      t:feed("gm")
      t:wait_for(function()
        return #said("conflict_choose") == 1
      end, { message = "the app never heard" })
      expect(said("conflict_choose")[1].index).to_equal(0)
      expect(said("conflict_choose")[1].choice).to_equal("ours")
      btv.o.modeless = false
      btv.helix.disable()
    end)
  end

  btv.test.it("only undoes a resolved region, and only chooses for an open one", function(t)
    shown(t)
    t:sleep(20)
    conflict.choose("undo")
    expect(#said("conflict_choose")).to_equal(0)
    local resolved = spec()
    resolved.regions[1].resolved = true
    shown(t, resolved)
    t:sleep(20)
    conflict.choose("theirs")
    expect(#said("conflict_choose")).to_equal(0)
    conflict.choose("undo")
    expect(said("conflict_choose")[1].choice).to_equal("undo")
  end)

  btv.test.it("says nothing is here outside a conflict", function(t)
    shown(t)
    t:sleep(20)
    btv.win.set_cursor(btv.win.current(), 7, 0)
    t:sleep(20)
    conflict.choose("ours")
    expect(#said("conflict_choose")).to_equal(0)
  end)

  btv.test.it("goes through open conflicts, skipping resolved ones", function(t)
    shown(t)
    t:sleep(20)
    conflict.go("next")
    t:wait_for(function()
      return btv.line(".") == 8
    end, { message = "never moved" })
    conflict.go("next")
    t:sleep(20)
    expect(btv.line(".")).to_equal(8)
    conflict.go("prev")
    t:wait_for(function()
      return btv.line(".") == 2
    end, { message = "never moved back" })
  end)

  btv.test.it("keeps the panes' cursors together, past a screen", function(t)
    local long = spec()
    for name, pane in pairs({ ours = long.ours, theirs = long.theirs, result = long.result }) do
      for i = 1, 200 do
        pane[#pane + 1] = row("common", name .. i, i + 10)
      end
    end
    shown(t, long)
    t:sleep(20)
    t:feed("150G")
    t:wait_for(function()
      local all = true
      for _, win in ipairs(btv.win.list()) do
        all = all and btv.win.call(win, btv.win.saveview).lnum == 150
      end
      return all
    end, { message = "the panes came apart" })
  end)

  btv.test.it("a choice shown again keeps the cursor", function(t)
    shown(t)
    t:sleep(20)
    conflict.go("next")
    t:wait_for(function()
      return btv.line(".") == 8
    end, { message = "never moved" })
    local resolved = spec()
    resolved.regions[2].resolved = true
    shown(t, resolved)
    t:sleep(20)
    expect(btv.line(".")).to_equal(8)
    local last = said("conflict_at")[#said("conflict_at")]
    expect(last.resolved).to_equal(true)
  end)

  -- One long file with its second conflict past a screen.
  local function long_spec()
    local s = spec()
    for _, pane in ipairs({ s.ours, s.theirs, s.result }) do
      local tail = { table.unpack(pane, 8, 12) }
      for i = 8, #pane do
        pane[i] = nil
      end
      for i = 8, 149 do
        pane[i] = row("common", "line " .. i, i)
      end
      for i, cell in ipairs(tail) do
        pane[149 + i] = cell
      end
      for i = 155, 300 do
        pane[i] = row("common", "line " .. i, i)
      end
    end
    s.regions[2] = { first = 150, last = 154, resolved = false }
    return s
  end

  -- A file's line on the screen, as going to a conflict scrolls.
  local function on_screen(line)
    return line - btv.win.call(btv.win.current(), btv.win.saveview).topline + 1
  end

  -- The style of a line's background as painted: `t:highlights` has spans, not these.
  local function background(line)
    for _, place in ipairs(btv._ui.line_bg or {}) do
      if place[1] + 1 == on_screen(line) then
        return place[2]
      end
    end
    return nil
  end

  -- Two "mine" lines painted apart: the current one's colour wins over the faint one under it. A
  -- frame numbers its own styles, so they only compare within one.
  local function apart()
    local first, second = background(3), background(9)
    return first ~= nil and second ~= nil and first ~= second
  end

  btv.test.it("colours both sides everywhere, stronger in the current conflict", function(t)
    shown(t)
    t:wait_for(apart, { message = "the first doesn't stand out" })
    -- By the cursor, not `go`, which would scroll the first off the screen.
    btv.win.set_cursor(btv.win.current(), 8, 0)
    t:wait_for(function()
      return btv.line(".") == 8 and apart()
    end, { message = "the second doesn't stand out" })
  end)

  btv.test.it("has grey only on the current conflict's other rows", function(t)
    shown(t)
    t:wait_for(function()
      return background(2) ~= nil
    end, { message = "the current conflict's filler has no background" })
    expect(background(1)).to_equal(nil)
    expect(background(7)).to_equal(nil)
    -- Filler and markers in the other conflict.
    expect(background(8)).to_equal(nil)
    expect(background(10)).to_equal(nil)
    btv.win.set_cursor(btv.win.current(), 8, 0)
    t:wait_for(function()
      return btv.line(".") == 8 and background(8) ~= nil
    end, { message = "it didn't move" })
    expect(background(2)).to_equal(nil)
  end)

  btv.test.it("no conflict stands out with the cursor outside them", function(t)
    shown(t)
    t:wait_for(apart, { message = "the first doesn't stand out" })
    btv.win.set_cursor(btv.win.current(), 7, 0)
    t:wait_for(function()
      return background(3) ~= nil and background(3) == background(9)
    end, { message = "it stayed marked" })
    expect(background(2)).to_equal(nil)
  end)

  btv.test.it("signs mine with - and theirs with +", function(t)
    shown(t)
    t:wait_for(function()
      return t:decor(3).sign == "-"
    end, { message = "mine has no -" })
    expect(t:decor(1).sign).to_equal(nil)
    t:feed("<C-w>l")
    t:wait_for(function()
      return t:lines()[5] == "theirs" and t:decor(5).sign == "+"
    end, { message = "theirs has no +" })
  end)

  btv.test.it("puts the conflict it goes to at the top, in every pane", function(t)
    shown(t, long_spec())
    t:sleep(20)
    conflict.go("next")
    t:wait_for(function()
      return btv.line(".") == 150
    end, { message = "never moved" })
    local want = 150
    t:wait_for(function()
      for _, win in ipairs(btv.win.list()) do
        if btv.win.call(win, btv.win.saveview).topline ~= want then
          return false
        end
      end
      return true
    end, { message = "not at the top" })
    -- It stays: the cursor is in view, and the panes' echoes don't undo it.
    t:sleep(100)
    expect(btv.win.call(btv.win.current(), btv.win.saveview).topline).to_equal(want)
  end)

  btv.test.it("has the choices in its menu, enabled as they apply", function(t)
    shown(t)
    t:sleep(20)
    local items = btv.contextmenu.groups["sourcegit-conflict"].items()
    expect(items[1].label).to_equal("Use Mine")
    expect(items[1].enabled()).to_equal(true)
    expect(items[5].label).to_equal("Undo")
    expect(items[5].enabled()).to_equal(false)
  end)

  btv.test.it("asks for its language's grammar", function(t)
    local asked
    conflict.on_language = function(lang)
      asked = lang
    end
    shown(t)
    expect(asked).to_equal("lua")
  end)
end)
