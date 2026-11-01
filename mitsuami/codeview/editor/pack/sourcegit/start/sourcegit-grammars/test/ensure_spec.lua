local grammars = require("sourcegit-grammars")
local expect = btv.test.expect

-- No grammar of this name exists anywhere.
local MISSING = "sourcegitnonesuch"

btv.test.describe("sourcegit-grammars", function()
  local asked

  btv.test.before_each(function()
    asked = {}
    grammars.install = function(lang)
      asked[#asked + 1] = lang
    end
    grammars.configure({ enabled = false })
  end)

  local function settled(t)
    t:sleep(30)
  end

  btv.test.it("installs a missing grammar when it's on", function(t)
    grammars.configure({ enabled = true })
    grammars.ensure(MISSING)
    t:wait_for(function()
      return #asked == 1
    end)
    expect(asked).to_equal({ MISSING })
  end)

  btv.test.it("does nothing when it's off", function(t)
    grammars.ensure(MISSING)
    settled(t)
    expect(asked).to_equal({})
  end)

  btv.test.it("asks once per language", function(t)
    grammars.configure({ enabled = true })
    grammars.ensure(MISSING)
    grammars.ensure(MISSING)
    settled(t)
    expect(asked).to_equal({ MISSING })
  end)

  btv.test.it("leaves what another editor tried", function(t)
    grammars.configure({ enabled = true, tried = { MISSING } })
    grammars.ensure(MISSING)
    settled(t)
    expect(asked).to_equal({})
  end)

  btv.test.it("doesn't install a grammar that's there", function(t)
    -- Reacts to what it's asked by listing it, so only this name counts as installed.
    local real = btv.treesitter.installed
    btv.treesitter.installed = function()
      return btv.promise.resolve({ "sourcegitfixture" })
    end
    grammars.configure({ enabled = true })
    grammars.ensure("sourcegitfixture")
    grammars.ensure(MISSING)
    t:wait_for(function()
      return #asked > 0
    end)
    btv.treesitter.installed = real
    expect(asked).to_equal({ MISSING })
  end)

  btv.test.it("turned on, installs for the language last asked about", function(t)
    grammars.ensure(MISSING)
    settled(t)
    expect(asked).to_equal({})
    grammars.configure({ enabled = true })
    t:wait_for(function()
      return #asked == 1
    end)
    expect(asked).to_equal({ MISSING })
  end)

  btv.test.it("says when a grammar is ready, and not when it failed", function(t)
    local ready = {}
    grammars.on_ready = function(lang)
      ready[#ready + 1] = lang
    end
    grammars.setup()
    btv.autocmd.exec(
      "TSInstalled",
      { pattern = "nope", data = { lang = "nope", ok = false, error = "x" } }
    )
    btv.autocmd.exec("TSInstalled", { pattern = "yep", data = { lang = "yep", ok = true } })
    t:wait_for(function()
      return #ready > 0
    end, { message = "on_ready wasn't told" })
    expect(ready[1]).to_equal("yep")
    expect(#ready).to_be(1)
  end)

  btv.test.it("tells the app of its own install, not of a failed one", function(t)
    local said = {}
    grammars.send = function(name, payload)
      said[#said + 1] = { name, payload }
    end
    grammars.on_ready = function(_) end
    grammars.setup()
    btv.autocmd.exec(
      "TSInstalled",
      { pattern = "nope", data = { lang = "nope", ok = false, error = "x" } }
    )
    btv.autocmd.exec("TSInstalled", { pattern = "yep", data = { lang = "yep", ok = true } })
    t:wait_for(function()
      return #said > 0
    end, { message = "the app wasn't told" })
    expect(said).to_equal({ { "grammar_ready", { lang = "yep" } } })
  end)

  btv.test.it("looks again when told of another editor's install", function()
    local reloaded, ready = {}, {}
    local real = btv.treesitter.reload
    btv.treesitter.reload = function(lang)
      reloaded[#reloaded + 1] = lang
    end
    grammars.on_ready = function(lang)
      ready[#ready + 1] = lang
    end
    grammars.ready({ lang = "rust" })
    btv.treesitter.reload = real
    expect(reloaded).to_equal({ "rust" })
    expect(ready).to_equal({ "rust" })
    expect(function()
      grammars.ready({})
    end).to_error("no lang")
  end)

  btv.test.it("refuses bad settings", function()
    expect(function()
      grammars.configure({})
    end).to_error("bad enabled")
  end)
end)
