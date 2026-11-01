-- Drives the real editor with the fixture the Rust ABI tests and the manual window use: a
-- whole file, as git sends it, with changes at lines 5, 130 and 250.
local diff = require("sourcegit-diff")
local expect = btv.test.expect

local dir = debug.getinfo(1, "S").source:match("^@(.*)/test/[^/]+$")
local fixture = dir .. "/../../../../../fixtures/edit.json"

local function spec_of()
  return btv.json.decode(btv.await(btv.fs.read_text(fixture)))
end

-- Waits until the pane's `row` has its gutter, which arrives last.
local function shown(t, row)
  local spec = spec_of()
  diff.show(spec)
  t:wait_for(function()
    return t:decor(row or 1).gutter or t:decor(row or 1).sign
  end, { message = "the diff never showed" })
  return spec
end

local function has(spans, group, first, last)
  for _, span in ipairs(spans) do
    if span[3] == group and (not first or (span[1] == first and span[2] == last)) then
      return true
    end
  end
  return false
end

local function layout_settles(t, row_text)
  t:wait_for(function()
    for _, line in ipairs(t:lines()) do
      if line == row_text then
        return true
      end
    end
    return false
  end, { message = "the layout never showed: " .. row_text })
  t:sleep(20)
end

btv.test.describe("sourcegit-diff", function()
  -- Not an after_each: a test that fails part-way never reaches it.
  btv.test.before_each(function(t)
    diff.close()
    diff.layout({
      context = 4,
      all = false,
      side_by_side = false,
      wrap = false,
      syntax = true,
      hidden_symbols = false,
    })
    btv.helix.disable()
    btv.o.modeless = false
    btv.o.relativenumber = false
    diff.on_language = function(_) end
  end)

  btv.test.describe("unified", function()
    btv.test.it("shows context around changes and how much it hid", function(t)
      shown(t)
      local lines = t:lines()
      expect(lines[1]).to_equal("// A small module, changed in three places.")
      expect(lines).to_contain('    eprintln!("{}", compute());')
      expect(lines).to_contain("⋯ 112 unchanged lines")
      expect(#lines < 40).to_be(true)
    end)

    btv.test.it("has no hunk header from git", function(t)
      shown(t)
      for _, line in ipairs(t:lines()) do
        expect(line:match("^@@")).to_be_nil()
      end
    end)

    btv.test.it("starts at the first change", function(t)
      shown(t)
      t:wait_for(function()
        return t:cursor()[1] == 5
      end, { message = "not at the first change" })
    end)

    btv.test.it("numbers both versions in the gutter", function(t)
      shown(t)
      expect(t:decor(4).gutter).to_equal("  4   4 ")
      expect(t:decor(5).gutter).to_equal("  5     ")
      expect(t:decor(6).gutter).to_equal("      5 ")
    end)

    btv.test.it("tints by kind and marks the changed characters", function(t)
      shown(t)
      expect(t:decor(5).sign).to_equal("-")
      expect(t:decor(6).sign).to_equal("+")
      expect(t:decor(4).sign).to_be_nil()
      expect(has(t:highlights(5), "DiffText", 4, 11)).to_be(true)
      expect(has(t:highlights(6), "DiffText", 4, 12)).to_be(true)
      expect(has(t:highlights(4), "DiffText")).to_be(false)
    end)

    btv.test.it("colours each sign for what it means", function(t)
      shown(t)
      local marks = vim.api.nvim_buf_get_extmarks(0, diff.namespace, 0, -1, { details = true })
      local groups = {}
      for _, mark in ipairs(marks) do
        local details = mark[4]
        if details.sign_text then
          groups[vim.trim(details.sign_text)] = details.sign_hl_group or "none"
        end
      end
      expect(groups["+"]).to_equal("Added")
      expect(groups["-"]).to_equal("Removed")
      -- The colorscheme's, or ours where it has none: a sign with no colour draws red.
      expect(btv.hl.exists("Added")).to_be(true)
      expect(btv.hl.exists("Removed")).to_be(true)
    end)

    btv.test.it("is read-only", function(t)
      local before = shown(t) and t:lines()[1]
      t:feed("ggix<Esc>")
      expect(t:lines()[1]).to_equal(before)
    end)

    btv.test.it("moves between changes, and stays at the ends", function(t)
      shown(t)
      t:feed("gg")
      t:feed("]c")
      expect(t:cursor()[1]).to_be(5)
      t:feed("]c")
      expect(t:lines()[t:cursor()[1]]).to_equal("// olá, removed with no line to replace it")
      t:feed("]c")
      local last = t:cursor()[1]
      expect(t:lines()[last]).to_equal("// 日本語, added with no line it replaces")
      t:feed("]c")
      expect(t:cursor()[1]).to_be(last)
      t:feed("[c")
      t:feed("[c")
      expect(t:cursor()[1]).to_be(5)
    end)

    for name, turn_on in pairs({
      helix = function()
        btv.helix.enable()
      end,
      modeless = function()
        btv.o.modeless = true
      end,
    }) do
      btv.test.it("moves between changes in " .. name .. " mode, read-only still", function(t)
        shown(t)
        turn_on()
        t:sleep(20)
        local before = t:lines()[5]
        t:feed("[c")
        t:feed("]c")
        t:wait_for(function()
          return t:cursor()[1] > 5
        end, { message = "]c didn't move" })
        t:feed("x")
        expect(t:lines()[5]).to_equal(before)
      end)
    end

    btv.test.it("leaves no empty tab beside the diff", function(t)
      shown(t)
      t:wait_for(function()
        return #btv.tabpage.list() == 1
      end, { message = "the editor's first tab stayed" })
    end)

    btv.test.it("shows relative numbers only when asked, beside the file's", function(t)
      -- `t:screen()` has no gutter to read: the window's option is what draws it.
      shown(t)
      expect(btv.wo[btv.win.list()[1]].relativenumber).to_be(false)
      diff.close()
      btv.o.relativenumber = true
      shown(t)
      expect(btv.wo[btv.win.list()[1]].relativenumber).to_be(true)
      expect(t:decor(4).gutter).to_equal("  4   4 ")
    end)

    btv.test.it("replaces the diff showing", function(t)
      shown(t)
      diff.show({ path = "b", lines = { { kind = "added", text = "only", old = 0, new = 1 } } })
      layout_settles(t, "only")
      expect(#t:lines()).to_be(1)
    end)

    btv.test.it("shows again after the user closed it", function(t)
      shown(t)
      t:cmd("close")
      shown(t)
      expect(t:lines()[1]).to_equal("// A small module, changed in three places.")
    end)
  end)

  btv.test.describe("the toolbar", function()
    btv.test.it("goes to the first, previous, next and last change", function(t)
      shown(t)
      local old = "// olá, removed with no line to replace it"
      local new = "// 日本語, added with no line it replaces"
      local text = function()
        return t:lines()[t:cursor()[1]]
      end
      local go = function(where, want)
        diff.go_to({ where = where })
        t:wait_for(function()
          return text() == want
        end, { message = where .. " went elsewhere: " .. tostring(text()) })
      end
      local first = '    println!("{}", compute());'
      go("last", new)
      go("prev", old)
      go("first", first)
      go("next", old)
      go("next", new)
      -- Stays at the ends.
      go("next", new)
      go("first", first)
      go("prev", first)
    end)

    btv.test.it("refuses a place it doesn't know", function()
      expect(function()
        diff.go_to({ where = "middle" })
      end).to_error("can't go to: middle")
    end)

    btv.test.it("wraps only with one pane", function(t)
      shown(t)
      diff.layout({ wrap = true })
      t:wait_for(function()
        return btv.wo[0].wrap
      end, { message = "no wrap" })
      diff.layout({ side_by_side = true })
      t:wait_for(function()
        return #btv.win.list() == 2
          and not btv.wo[btv.win.list()[1]].wrap
          and not btv.wo[btv.win.list()[2]].wrap
      end, { message = "panes wrap" })
    end)

    btv.test.it("shows hidden symbols when asked", function(t)
      shown(t)
      expect(btv.wo[0].list).to_be(false)
      diff.layout({ hidden_symbols = true })
      t:wait_for(function()
        return btv.wo[0].list
      end, { message = "no list" })
      diff.layout({ hidden_symbols = false })
      t:wait_for(function()
        return not btv.wo[0].list
      end)
    end)

    btv.test.it("refuses a bad toggle, naming it", function()
      expect(function()
        diff.layout({ wrap = "yes" })
      end).to_error("bad wrap")
    end)
  end)

  btv.test.describe("context", function()
    btv.test.it("grows and shrinks without the diff being sent again", function(t)
      shown(t)
      local four = #t:lines()
      diff.layout({ context = 8 })
      layout_settles(t, "⋯ 104 unchanged lines")
      expect(#t:lines() > four).to_be(true)
      diff.layout({ context = 4 })
      layout_settles(t, "⋯ 112 unchanged lines")
      expect(#t:lines()).to_be(four)
    end)

    btv.test.it("shows every line, then back", function(t)
      local spec = shown(t)
      local gaps = #t:lines()
      diff.layout({ all = true })
      -- Git's lines, less its one header.
      t:wait_for(function()
        return #t:lines() == #spec.lines - 1
      end, { message = "not every line showed" })
      for _, line in ipairs(t:lines()) do
        expect(line:match("^⋯")).to_be_nil()
      end
      diff.layout({ all = false })
      t:wait_for(function()
        return #t:lines() == gaps
      end)
    end)

    btv.test.it("stays on the same line of the file", function(t)
      shown(t)
      t:feed("]c")
      local text = t:lines()[t:cursor()[1]]
      diff.layout({ all = true })
      t:wait_for(function()
        return #t:lines() > 100 and t:lines()[t:cursor()[1]] == text
      end, { message = "the cursor didn't follow its line" })
    end)

    btv.test.it("refuses what it doesn't understand", function()
      expect(function()
        diff.layout({ context = -1 })
      end).to_error("bad context")
      expect(function()
        diff.layout({ side_by_side = "yes" })
      end).to_error("bad side_by_side")
    end)
  end)

  btv.test.describe("side by side", function()
    local function panes(t)
      diff.layout({ side_by_side = true })
      t:wait_for(function()
        return #btv.win.list() == 2 and t:decor(1).gutter
      end, { message = "no second pane" })
      t:sleep(20)
      local wins = btv.win.list()
      local current = btv.win.current()
      local other = wins[1] == current and wins[2] or wins[1]
      return current, other
    end

    local function lines_of(win)
      return btv.buf.lines(btv.win.buf(win), 0, -1)
    end

    btv.test.it("puts old on the left, new on the right, the same length", function(t)
      shown(t)
      local left, right = panes(t)
      local old, new = lines_of(left), lines_of(right)
      expect(#old).to_be(#new)
      expect(old[5]).to_equal('    println!("{}", compute());')
      expect(new[5]).to_equal('    eprintln!("{}", compute());')
    end)

    btv.test.it("has old on the left and new on the right on screen", function(t)
      shown(t)
      panes(t)
      -- A click focuses what's really at that column, whichever pane was made first.
      t:mouse("left", "press", 4, 20)
      t:mouse("left", "release", 4, 20)
      t:wait_for(function()
        return t:lines()[5] == '    println!("{}", compute());'
      end, { message = "the old version isn't on the left" })
      t:mouse("left", "press", 4, 60)
      t:mouse("left", "release", 4, 60)
      t:wait_for(function()
        return t:lines()[5] == '    eprintln!("{}", compute());'
      end, { message = "the new version isn't on the right" })
    end)

    btv.test.it("fills where one side has fewer lines", function(t)
      shown(t)
      local left, right = panes(t)
      local old, new = lines_of(left), lines_of(right)
      expect(old[6]).to_equal("")
      expect(new[6]).to_equal('    log::info!("done");')
    end)

    btv.test.it("fills with what can't be taken for code or whitespace", function()
      expect(btv.o.listchars:find(diff.filler, 1, true)).to_be_nil()
      -- Not ASCII, so no language's syntax.
      expect(diff.filler:byte(1) >= 0x80).to_be(true)
    end)

    btv.test.it("numbers each pane with its own version", function(t)
      shown(t)
      panes(t)
      expect(t:decor(5).gutter).to_equal("  5 ")
    end)

    btv.test.it("moves the other pane's cursor with this one's", function(t)
      shown(t)
      local _, right = panes(t)
      t:feed("gg")
      t:feed("]c")
      t:feed("]c")
      local row = t:cursor()[1]
      t:wait_for(function()
        return btv.win.call(right, btv.win.saveview).lnum == row
      end, { message = "the other pane's cursor stayed" })
    end)

    btv.test.it("scrolls the other pane with this one", function(t)
      shown(t)
      local left, right = panes(t)
      diff.layout({ all = true })
      t:wait_for(function()
        return #t:lines() > 100
      end)
      t:feed("gg")
      t:feed("20<C-e>")
      t:wait_for(function()
        local a = btv.win.call(left, btv.win.saveview).topline
        local b = btv.win.call(right, btv.win.saveview).topline
        return a > 1 and a == b
      end, { message = "the panes scrolled apart" })
    end)

    local function wide(t)
      local long = string.rep("abcdefghij", 40)
      diff.show({
        path = "wide.txt",
        lines = {
          { kind = "deleted", text = "old " .. long, old = 1, new = 0 },
          { kind = "added", text = "new " .. long, old = 0, new = 1 },
          { kind = "normal", text = long, old = 2, new = 2 },
        },
      })
      diff.layout({ side_by_side = true })
      t:wait_for(function()
        return #btv.win.list() == 2 and t:lines()[2] == long and t:decor(1).gutter
      end, { message = "no panes" })
      t:sleep(20)
      local wins = btv.win.list()
      local current = btv.win.current()
      return current, wins[1] == current and wins[2] or wins[1]
    end

    local function leftcol(win)
      return btv.win.call(win, btv.win.saveview).leftcol
    end

    local function together(t, left, right, at_least)
      t:wait_for(function()
        return leftcol(left) >= at_least and leftcol(left) == leftcol(right)
      end, { message = "the panes scrolled apart sideways" })
      -- And stay so, as the moves it set off settle.
      t:sleep(60)
      expect(leftcol(right)).to_be(leftcol(left))
      expect(leftcol(left) >= at_least).to_be(true)
    end

    btv.test.it("scrolls the other pane sideways with this one", function(t)
      local left, right = wide(t)
      t:feed("120|")
      together(t, left, right, 30)
    end)

    btv.test.it("scrolls together when the other pane is told to", function(t)
      local left, right = wide(t)
      btv.win.set_leftcol(right, 40)
      together(t, left, right, 40)
    end)

    -- Six notches, of six columns: every one has to count, as a pane set by the other
    -- used to answer with its old place and undo the notch after.
    local function wheel(t, col)
      for _ = 1, 6 do
        t:mouse("wheel", "right", 2, col)
      end
    end

    btv.test.it("scrolls together on the wheel over the focused pane", function(t)
      local left, right = wide(t)
      wheel(t, 20)
      together(t, left, right, 36)
    end)

    btv.test.it("scrolls together on the wheel over the other pane", function(t)
      local left, right = wide(t)
      wheel(t, 60)
      together(t, left, right, 36)
    end)

    btv.test.it("loses no notch of the vertical wheel either", function(t)
      shown(t)
      local left, right = panes(t)
      diff.layout({ all = true })
      t:wait_for(function()
        return #t:lines() > 100
      end)
      t:feed("gg")
      t:sleep(20)
      local top = function(win)
        return btv.win.call(win, btv.win.saveview).topline
      end
      local before = top(left)
      for _ = 1, 6 do
        t:mouse("wheel", "down", 2, 60)
      end
      t:wait_for(function()
        return top(left) == top(right) and top(right) > before + 6
      end, { message = "a notch was lost: " .. top(left) .. " and " .. top(right) })
    end)

    -- Further than a screen, so the focused pane's cursor is left out of its view.
    for name, col in pairs({ left = 20, right = 60 }) do
      btv.test.it("scrolls far together on the wheel over the " .. name .. " pane", function(t)
        shown(t)
        local left, right = panes(t)
        diff.layout({ all = true })
        t:wait_for(function()
          return #t:lines() > 100
        end)
        t:feed("gg")
        t:sleep(20)
        local top = function(win)
          return btv.win.call(win, btv.win.saveview).topline
        end
        for _ = 1, 40 do
          t:mouse("wheel", "down", 2, col)
        end
        t:sleep(60)
        expect(top(left) > 60).to_be(true)
        expect(top(right)).to_be(top(left))
      end)
    end

    btv.test.it("goes back to one pane", function(t)
      shown(t)
      panes(t)
      diff.layout({ side_by_side = false })
      t:wait_for(function()
        return #btv.win.list() == 1
      end, { message = "the second pane stayed" })
      expect(t:lines()).to_contain('    println!("{}", compute());')
      expect(t:lines()).to_contain('    eprintln!("{}", compute());')
    end)

    btv.test.it("still works after going back and forth", function(t)
      shown(t)
      panes(t)
      diff.layout({ side_by_side = false })
      t:wait_for(function()
        return #btv.win.list() == 1
      end)
      local _, right = panes(t)
      expect(#lines_of(right) > 0).to_be(true)
    end)
  end)

  btv.test.describe("colours", function()
    -- A grammar can't be assumed here, so `highlight` stands in for one, answering from what
    -- it's given: each line's first word, so a wrong version or line shows as a wrong span.
    local real
    local asked

    btv.test.before_each(function()
      real = btv.treesitter.highlight
      asked = {}
      btv.treesitter.highlight = function(lang, text)
        asked[#asked + 1] = { lang = lang, text = text }
        local spans = {}
        local line = 0
        for row in (text .. "\n"):gmatch("(.-)\n") do
          local s, e = row:find("%S+")
          if s then
            spans[#spans + 1] = { line = line, col_start = s - 1, col_end = e, group = "fixture" }
          end
          line = line + 1
        end
        return btv.promise.resolve(spans)
      end
      btv.hl.define(0, "@fixture", { link = "Keyword" })
    end)

    btv.test.after_each(function()
      btv.treesitter.highlight = real
    end)

    local function colored(t)
      t:wait_for(function()
        return has(t:highlights(4), "@fixture") or has(t:highlights(4), "Keyword")
      end, { message = "no colours" })
    end

    local function fixture_at(spans, first, last)
      return has(spans, "@fixture", first, last) or has(spans, "Keyword", first, last)
    end

    btv.test.it("parses each version whole, in the file's language", function(t)
      local spec = shown(t)
      colored(t)
      expect(#asked).to_be(2)
      expect(asked[1].lang).to_equal("rust")
      expect(asked[1].text).to_contain("println!")
      expect(asked[1].text:find("eprintln!", 1, true)).to_be_nil()
      expect(asked[1].text).to_contain("// olá")
      expect(asked[2].text).to_contain("eprintln!")
      expect(asked[2].text:find("// olá", 1, true)).to_be_nil()
      expect(asked[2].text).to_contain("// 日本語")
      -- Whole: more than shows.
      expect(asked[2].text).to_contain("helper_30")
      expect(spec.path).to_equal("src/app.rs")
    end)

    btv.test.it("copies them onto each version's lines", function(t)
      shown(t)
      colored(t)
      -- `fn` in the unchanged line, and each version's own first word in the changed ones,
      -- which reads from where the changed characters' tint ends.
      expect(fixture_at(t:highlights(4), 0, 2)).to_be(true)
      expect(fixture_at(t:highlights(5), 11, 18)).to_be(true)
      expect(fixture_at(t:highlights(6), 12, 19)).to_be(true)
    end)

    btv.test.it("keeps the changed characters over them", function(t)
      shown(t)
      colored(t)
      expect(has(t:highlights(5), "DiffText", 4, 11)).to_be(true)
    end)

    btv.test.it("keeps them through a layout change, with no new parse", function(t)
      shown(t)
      colored(t)
      diff.layout({ context = 8 })
      layout_settles(t, "⋯ 104 unchanged lines")
      t:wait_for(function()
        return fixture_at(t:highlights(4), 0, 2)
      end, { message = "the colours went" })
      expect(#asked).to_be(2)
    end)

    btv.test.it("colours each pane from its own version", function(t)
      shown(t)
      colored(t)
      diff.layout({ side_by_side = true })
      t:wait_for(function()
        return #btv.win.list() == 2 and fixture_at(t:highlights(5), 11, 18)
      end, { message = "the old pane has no old colours" })
    end)

    btv.test.it("colours a line past a line with none, in its own place", function(t)
      shown(t)
      colored(t)
      diff.layout({ all = true })
      t:wait_for(function()
        return #t:lines() > 100
      end)
      -- `t:highlights` counts the rows painted, so find it among those.
      t:feed("G")
      t:wait_for(function()
        for row, text in ipairs(t:screen()) do
          if text == "fn last() {}" then
            return fixture_at(t:highlights(row), 0, 2)
          end
        end
        return false
      end, { message = "the last line has no colours" })
    end)

    btv.test.it("drops colours that arrive for an earlier diff", function(t)
      local answer
      btv.treesitter.highlight = function()
        return btv.promise.new(function(resolve)
          answer = answer or resolve
        end)
      end
      diff.show(spec_of())
      t:wait_for(function()
        return answer
      end)
      btv.treesitter.highlight = real
      diff.show({ path = "b.txt", lines = { { kind = "added", text = "only", old = 0, new = 1 } } })
      layout_settles(t, "only")
      answer({ { line = 0, col_start = 0, col_end = 4, group = "fixture" } })
      t:sleep(30)
      expect(fixture_at(t:highlights(1), 0, 4)).to_be(false)
    end)

    btv.test.it("turned off, takes them away and doesn't parse", function(t)
      shown(t)
      colored(t)
      diff.layout({ syntax = false })
      t:wait_for(function()
        return not fixture_at(t:highlights(4), 0, 2)
      end, { message = "the colours stayed" })
      diff.close()
      asked = {}
      shown(t)
      t:sleep(30)
      expect(#asked).to_be(0)
    end)

    btv.test.it("turned on, parses what never was, and asks for its grammar", function(t)
      local told = {}
      diff.on_language = function(lang)
        told[#told + 1] = lang
      end
      diff.layout({ syntax = false })
      shown(t)
      t:sleep(20)
      expect(#asked).to_be(0)
      expect(told).to_equal({})
      diff.layout({ syntax = true })
      colored(t)
      expect(#asked).to_be(2)
      expect(told).to_equal({ "rust" })
      -- Off and on again has them already.
      diff.layout({ syntax = false })
      diff.layout({ syntax = true })
      t:sleep(30)
      expect(#asked).to_be(2)
    end)

    btv.test.it("doesn't parse past the size limit, and still shows", function(t)
      local limit = diff.max_colored_bytes
      diff.max_colored_bytes = 10
      shown(t)
      t:sleep(30)
      diff.max_colored_bytes = limit
      expect(#asked).to_be(0)
      expect(t:lines()[1]).to_equal("// A small module, changed in three places.")
    end)

    btv.test.it("doesn't parse a language it can't name", function(t)
      diff.show({
        path = "notes.nonesuch",
        lines = { { kind = "added", text = "x", old = 0, new = 1 } },
      })
      layout_settles(t, "x")
      expect(#asked).to_be(0)
    end)

    btv.test.it(
      "tells who asked about the language, and colours again on a new grammar",
      function(t)
        local told = {}
        diff.on_language = function(lang)
          told[#told + 1] = lang
        end
        shown(t)
        colored(t)
        expect(told).to_equal({ "rust" })
        diff.recolor("python")
        t:sleep(20)
        expect(#asked).to_be(2)
        diff.recolor("rust")
        t:wait_for(function()
          return #asked == 4
        end, { message = "no new parse" })
      end
    )
  end)

  btv.test.it("refuses what it doesn't understand, naming it", function()
    expect(function()
      diff.show({ path = "a", lines = { { kind = "moved", text = "", old = 0, new = 0 } } })
    end).to_error("line 1 has an unknown kind: moved")
    expect(function()
      diff.show({ lines = {} })
    end).to_error("no path")
  end)
end)
