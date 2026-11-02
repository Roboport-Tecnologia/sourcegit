-- Drives the real editor with the fixture the Rust ABI tests and the manual window use.
local blame = require("sourcegit-blame")
local expect = btv.test.expect

local dir = debug.getinfo(1, "S").source:match("^@(.*)/test/[^/]+$")
local fixture = dir .. "/../../../../../fixtures/blame-sample.json"

local function spec_of()
  return btv.json.decode(btv.await(btv.fs.read_text(fixture)))
end

local function shown(t)
  local spec = spec_of()
  blame.show(spec)
  t:wait_for(function()
    return t:decor(1).gutter
  end, { message = "the blame never showed" })
  return spec
end

btv.test.describe("sourcegit-blame", function()
  local sent

  local function said(name)
    local out = {}
    for _, message in ipairs(sent) do
      if message[1] == name then
        out[#out + 1] = message[2]
      end
    end
    return out
  end

  btv.test.before_each(function()
    sent = {}
    blame.send = function(name, payload)
      sent[#sent + 1] = { name, payload }
    end
    blame.close()
    blame.on_language = function(_) end
    btv.o.relativenumber = false
    btv.helix.disable()
    btv.o.modeless = false
  end)

  btv.test.it("shows the file's text, line for line", function(t)
    shown(t)
    expect(t:lines()[1]).to_equal("use std::fmt;")
    expect(#t:lines()).to_be(6)
  end)

  btv.test.it("heads each run of a commit's lines, and only those", function(t)
    shown(t)
    expect(t:decor(1).gutter).to_equal("a1b2c3d4 Ana Souza 2026/01/02 ")
    expect(t:decor(2).gutter).to_be_nil()
    expect(t:decor(3).gutter).to_equal("e5f6a7b8 日本語    2026/03/04 ")
    expect(t:decor(4).gutter).to_be_nil()
    expect(t:decor(5).gutter).to_equal("a1b2c3d4 Ana Souza 2026/01/02 ")
  end)

  btv.test.it("lines up a wide name, and cuts a long one", function(t)
    blame.show({
      path = "a.txt",
      text = "x\ny\n",
      lines = {
        { sha = "1", author = string.rep("n", 40), time = "t", file = "a.txt", first = true },
        { sha = "2", author = "短", time = "t", file = "a.txt", first = true },
      },
    })
    t:wait_for(function()
      return t:decor(1).gutter
    end)
    local cut = "1 " .. string.rep("n", 19) .. "… t "
    expect(t:decor(1).gutter).to_equal(cut)
    expect(btv.str.displaywidth(t:decor(2).gutter)).to_be(btv.str.displaywidth(cut))
  end)

  btv.test.it("takes its language from the name, and is read-only", function(t)
    local told = {}
    blame.on_language = function(lang)
      told[#told + 1] = lang
    end
    shown(t)
    t:wait_for(function()
      return btv.bo[0].filetype == "rust"
    end, { message = "no filetype" })
    expect(told).to_equal({ "rust" })
    t:feed("ix<Esc>")
    expect(t:lines()[1]).to_equal("use std::fmt;")
  end)

  btv.test.it("says which commit the cursor reached, once per commit", function(t)
    shown(t)
    t:wait_for(function()
      return #said("blame_line") == 1
    end, { message = "no first commit" })
    expect(said("blame_line")[1]).to_equal({ sha = "a1b2c3d4", file = "src/app.rs" })
    t:feed("j")
    t:sleep(20)
    expect(#said("blame_line")).to_be(1)
    t:feed("j")
    t:wait_for(function()
      return #said("blame_line") == 2
    end, { message = "the next commit wasn't told" })
    expect(said("blame_line")[2].sha).to_equal("e5f6a7b8")
    t:feed("2j")
    t:wait_for(function()
      return #said("blame_line") == 3
    end)
    expect(said("blame_line")[3].sha).to_equal("a1b2c3d4")
  end)

  -- Modeless first: the runner's reset after Helix can leave it off in the next test.
  for _, mode in ipairs({
    { "modeless", function()
      btv.o.modeless = true
    end },
    { "vim", function() end },
    { "helix", function()
      btv.helix.enable()
    end },
  }) do
    local name, turn_on = mode[1], mode[2]
    btv.test.it("asks to go to a commit, or blame there, in " .. name .. " mode", function(t)
      shown(t)
      turn_on()
      t:sleep(20)
      t:feed("<CR>")
      t:wait_for(function()
        return #said("blame_goto") == 1
      end, { message = "<CR> wasn't heard" })
      expect(said("blame_goto")[1]).to_equal({ sha = "a1b2c3d4", file = "src/app.rs" })
      t:feed("gb")
      t:wait_for(function()
        return #said("blame_here") == 1
      end, { message = "gb wasn't heard" })
      expect(said("blame_here")[1].sha).to_equal("a1b2c3d4")
    end)
  end

  btv.test.it("numbers its lines, relative when asked", function(t)
    shown(t)
    expect(btv.wo[0].number).to_be(true)
    expect(btv.wo[0].relativenumber).to_be(false)
    blame.close()
    btv.o.relativenumber = true
    shown(t)
    expect(btv.wo[0].relativenumber).to_be(true)
  end)

  btv.test.it("a click in the gutter puts the cursor on that line", function(t)
    shown(t)
    t:mouse("left", "press", 3, 2)
    t:mouse("left", "release", 3, 2)
    t:wait_for(function()
      return t:cursor()[1] == 4
    end, { message = "the click didn't reach the line" })
  end)

  btv.test.it("replaces what's showing, in the same view, from the top", function(t)
    shown(t)
    t:feed("G")
    blame.show({
      path = "b.txt",
      text = "only\n",
      lines = { { sha = "zz", author = "Z", time = "t", file = "b.txt", first = true } },
    })
    t:wait_for(function()
      return t:lines()[1] == "only" and t:decor(1).gutter == "zz Z t "
    end, { message = "the last blame stayed" })
    expect(t:cursor()[1]).to_be(1)
    expect(#btv.win.list()).to_be(1)
  end)

  btv.test.it("takes the control characters out of a name", function(t)
    blame.show({
      path = "a.txt",
      text = "x\n",
      lines = { { sha = "1", author = "a\tb", time = "t", file = "a.txt", first = true } },
    })
    t:wait_for(function()
      return t:decor(1).gutter == "1 a b t "
    end, { message = "the name was refused or kept its tab" })
  end)

  btv.test.it("refuses what it doesn't understand, naming it", function()
    expect(function()
      blame.show({ path = "a", text = "" })
    end).to_error("no lines")
    expect(function()
      blame.show({ path = "a", text = "x", lines = { { sha = "1" } } })
    end).to_error("line 1 has no author")
  end)
end)
