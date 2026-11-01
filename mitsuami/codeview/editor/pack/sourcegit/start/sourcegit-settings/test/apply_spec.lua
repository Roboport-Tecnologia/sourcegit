local settings = require("sourcegit-settings")
local expect = btv.test.expect

btv.test.describe("sourcegit-settings", function()
  btv.test.before_each(function()
    settings.apply({ mode = "vim", font = "", font_size = 14, tab_width = 4 })
  end)

  btv.test.it("switches between the modes, either way", function(t)
    settings.apply({ mode = "modeless" })
    t:wait_for(function()
      return t:mode() == "i"
    end)
    expect(btv.o.modeless).to_be(true)
    settings.apply({ mode = "helix" })
    t:wait_for(function()
      return t:mode() == "hn"
    end)
    expect(btv.o.modeless).to_be(false)
    settings.apply({ mode = "vim" })
    t:wait_for(function()
      return t:mode() == "n"
    end)
  end)

  btv.test.it("refuses a mode it doesn't know", function()
    expect(function()
      settings.apply({ mode = "emacs" })
    end).to_error("unknown mode: emacs")
  end)

  btv.test.it("sets the font and its size, together and apart", function()
    settings.apply({ font = "JetBrains Mono", font_size = 12.5 })
    expect(btv.o.guifont).to_equal("JetBrains Mono:h12.5")
    settings.apply({ font_size = 13 })
    expect(btv.o.guifont).to_equal("JetBrains Mono:h13")
    settings.apply({ font = "Iosevka" })
    expect(btv.o.guifont).to_equal("Iosevka:h13")
  end)

  btv.test.it("leaves the family to the platform with no font", function()
    settings.apply({ font = "", font_size = 14 })
    expect(btv.o.guifont).to_equal(":h14")
  end)

  btv.test.it("sets the tab width on buffers already open and on new ones", function(t)
    local before = t:buf()
    settings.apply({ tab_width = 2 })
    expect(btv.bo[before].tabstop).to_be(2)
    expect(btv.bo[before].shiftwidth).to_be(2)
    t:cmd("enew")
    expect(btv.bo[t:buf()].tabstop).to_be(2)
  end)

  btv.test.it("turns relative numbers on and off, in windows already open too", function(t)
    local win = btv.win.list()[1]
    settings.apply({ relative_numbers = true })
    expect(btv.wo[win].relativenumber).to_be(true)
    t:cmd("vsplit")
    settings.apply({ relative_numbers = false })
    for _, w in ipairs(btv.win.list()) do
      expect(btv.wo[w].relativenumber).to_be(false)
    end
    expect(btv.o.relativenumber).to_be(false)
    t:cmd("only")
  end)

  btv.test.it("refuses a bad tab width", function()
    expect(function()
      settings.apply({ tab_width = 0 })
    end).to_error("bad tab width: 0")
  end)

  btv.test.it("applies the colorscheme", function(t)
    settings.apply({ colorscheme = "habamax" })
    t:wait_for(function()
      return btv.g.colors_name == "habamax"
    end)
    settings.apply({ colorscheme = "bemtvi" })
    t:wait_for(function()
      return btv.g.colors_name == "bemtvi"
    end)
  end)
end)
