-- The float that says where staging is, in each mode, and what ends it.
local diff = require("sourcegit-diff")
local expect = btv.test.expect

local dir = debug.getinfo(1, "S").source:match("^@(.*)/test/[^/]+$")
local fixture = dir .. "/../../../../../fixtures/edit.json"

local sent

local function said(name)
  local count = 0
  for _, entry in ipairs(sent) do
    if entry == name then
      count = count + 1
    end
  end
  return count
end

local function show(t, actions)
  local spec = btv.json.decode(btv.await(btv.fs.read_text(fixture)))
  spec.actions = actions
  local before = said("shown")
  diff.show(spec)
  t:wait_for(function()
    return said("shown") > before
  end, { message = "the diff never showed" })
  t:sleep(30)
end

local STAGE = {
  { action = "stage", label = "Stage" },
  { action = "discard", label = "Discard" },
}

-- The float, by its config, if there's one.
local function hint_win()
  for _, win in ipairs(btv.win.list()) do
    local config = vim.api.nvim_win_get_config(win)
    if config.relative and config.relative ~= "" then
      return win, config
    end
  end
  return nil
end

local function gone(t)
  t:wait_for(function()
    return hint_win() == nil
  end, { message = "the hint stayed" })
end

btv.test.describe("sourcegit-diff's hint", function()
  btv.test.before_each(function(t)
    diff.close()
    sent = {}
    diff.send = function(name, _)
      sent[#sent + 1] = name
    end
    diff.on_language = function(_) end
    diff.hint_seen = false
    diff.layout({ side_by_side = false })
    btv.helix.disable()
    btv.o.modeless = false
    t:wait_for(function()
      return t:mode() == "n"
    end, { message = "vim's mode didn't come back" })
  end)

  btv.test.it("shows at the top right, naming what this diff can do", function(t)
    show(t, STAGE)
    local win, config = hint_win()
    expect(win ~= nil).to_be(true)
    expect(config.align).to_be("top-right")
    local text = table.concat(btv.buf.lines(btv.win.buf(win), 0, -1), "\n")
    expect(text:find("Right-click", 1, true) ~= nil).to_be(true)
    expect(text:find("Stage, Discard", 1, true) ~= nil).to_be(true)
    expect(text:find("gx Discard", 1, true) ~= nil).to_be(true)
  end)

  btv.test.it("doesn't show where the diff can't stage", function(t)
    show(t, nil)
    expect(hint_win()).to_be_nil()
  end)

  btv.test.it("leaves the focus and the keys on the diff", function(t)
    show(t, STAGE)
    expect(btv.bo[btv.buf.current()].filetype ~= nil).to_be(true)
    expect(vim.api.nvim_win_get_config(btv.win.current()).relative or "").to_equal("")
    local row = t:cursor()[1]
    t:feed("]c")
    t:wait_for(function()
      return t:cursor()[1] > row
    end, { message = "]c didn't reach the diff" })
    expect(hint_win() ~= nil).to_be(true)
  end)

  btv.test.it("a click on the float itself ends it, with the focus on the diff", function(t)
    show(t, STAGE)
    t:mouse("left", "press", 1, btv.o.columns - 4)
    t:mouse("left", "release", 1, btv.o.columns - 4)
    gone(t)
    expect(vim.api.nvim_win_get_config(btv.win.current()).relative or "").to_equal("")
    expect(t:lines()[1]).to_equal("// A small module, changed in three places.")
  end)

  btv.test.it("a drag after it still selects", function(t)
    btv.o.modeless = true
    t:sleep(20)
    show(t, STAGE)
    t:mouse("left", "press", 5, 20)
    t:mouse("left", "drag", 7, 20)
    t:mouse("left", "release", 7, 20)
    gone(t)
    t:wait_for(function()
      return btv.win.selection() ~= nil
    end, { message = "the drag selected nothing" })
  end)

  btv.test.it("stays gone for the next file, and stays through a redraw until then", function(t)
    show(t, STAGE)
    show(t, STAGE)
    expect(hint_win() ~= nil).to_be(true)
    t:mouse("left", "press", 3, 20)
    t:mouse("left", "release", 3, 20)
    gone(t)
    show(t, STAGE)
    expect(hint_win()).to_be_nil()
  end)

  btv.test.it("goes when the diff can't stage, or closes", function(t)
    show(t, STAGE)
    show(t, nil)
    gone(t)
    show(t, STAGE)
    expect(hint_win() ~= nil).to_be(true)
    diff.close()
    gone(t)
  end)

  btv.test.it("stays over both panes of side by side, with the keys on them", function(t)
    show(t, STAGE)
    diff.layout({ side_by_side = true })
    t:wait_for(function()
      return #btv.win.list() == 3
    end, { message = "no panes under the hint" })
    expect(vim.api.nvim_win_get_config(btv.win.current()).relative or "").to_equal("")
    t:mouse("left", "press", 8, 60)
    gone(t)
  end)

  -- Helix last: the runner's reset after Helix can leave modeless off in the next test. Each
  -- on a row of its own, as a second press on a cell counts as a double click.
  for i, mode in ipairs({
    { "modeless", function()
      btv.o.modeless = true
    end },
    { "vim", function() end },
    { "helix", function()
      btv.helix.enable()
    end },
  }) do
    local name, turn_on = mode[1], mode[2]
    btv.test.it("a left click in " .. name .. " mode ends it, and still moves there", function(t)
      show(t, STAGE)
      turn_on()
      t:sleep(20)
      local row = 6 + i
      t:mouse("left", "press", row, 20)
      t:mouse("left", "release", row, 20)
      gone(t)
      t:wait_for(function()
        return t:cursor()[1] == row + 1
      end, { message = "the click didn't place the cursor" })
    end)

    btv.test.it("a right click in " .. name .. " mode ends it, and opens the menu", function(t)
      show(t, STAGE)
      turn_on()
      t:sleep(20)
      t:mouse("right", "press", 4, 20)
      gone(t)
      local menu = t:wait_for(function()
        local m = t:menu()
        return m and #m.items > 0 and m or nil
      end, { message = "no menu opened" })
      expect(menu.items[1]).to_equal("Stage")
      t:feed("<Esc>")
    end)
  end
end)
