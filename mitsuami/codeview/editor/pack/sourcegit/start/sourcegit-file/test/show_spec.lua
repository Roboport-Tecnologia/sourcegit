local file = require("sourcegit-file")
local expect = btv.test.expect

local function shown(t, path, text)
  file.show({ path = path, text = text })
  t:wait_for(function()
    return btv.bo[0].buftype ~= "" and btv.wo[0].number
  end, { message = "the file never showed" })
end

btv.test.describe("sourcegit-file", function()
  btv.test.before_each(function()
    file.close()
    file.on_language = function(_) end
    btv.o.relativenumber = false
  end)

  btv.test.it("shows the text, line for line", function(t)
    shown(t, "a.txt", "one\n\nthree\n")
    expect(t:lines()).to_equal({ "one", "", "three" })
  end)

  btv.test.it("keeps a last line with no newline", function(t)
    shown(t, "a.txt", "one\ntwo")
    expect(t:lines()).to_equal({ "one", "two" })
  end)

  btv.test.it("shows a CRLF file without its carriage returns", function(t)
    shown(t, "a.txt", "one\r\ntwo\r\n")
    expect(t:lines()).to_equal({ "one", "two" })
  end)

  btv.test.it("shows an empty file", function(t)
    shown(t, "a.txt", "")
    expect(t:lines()).to_equal({ "" })
  end)

  btv.test.it("takes its language from its name", function(t)
    shown(t, "src/app.rs", "fn main() {}\n")
    t:wait_for(function()
      return btv.bo[0].filetype == "rust"
    end, { message = "no filetype" })
  end)

  btv.test.it("is read-only", function(t)
    shown(t, "a.txt", "one\n")
    t:feed("ix<Esc>")
    expect(t:lines()).to_equal({ "one" })
  end)

  btv.test.it("numbers its lines, relative when asked", function(t)
    shown(t, "a.txt", "one\n")
    expect(btv.wo[0].number).to_be(true)
    expect(btv.wo[0].relativenumber).to_be(false)
    file.close()
    btv.o.relativenumber = true
    shown(t, "a.txt", "one\n")
    expect(btv.wo[0].relativenumber).to_be(true)
  end)

  btv.test.it("replaces the file showing, its language too, from the top", function(t)
    shown(t, "src/app.rs", "a\nb\nc\nd\n")
    t:feed("G")
    file.show({ path = "notes.nonesuch", text = "only\n" })
    t:wait_for(function()
      return t:lines()[1] == "only" and btv.bo[0].filetype == ""
    end, { message = "the last file's language stayed" })
    expect(t:cursor()[1]).to_be(1)
  end)

  btv.test.it("leaves no empty tab beside it", function(t)
    shown(t, "a.txt", "one\n")
    expect(#btv.tabpage.list()).to_be(1)
  end)

  btv.test.it("shows again after the user closed it", function(t)
    shown(t, "a.txt", "one\n")
    t:cmd("close")
    file.show({ path = "b.txt", text = "two\n" })
    t:wait_for(function()
      return t:lines()[1] == "two"
    end, { message = "the file never showed" })
  end)

  btv.test.it("tells who asked about the language, if it has one", function(t)
    local told = {}
    file.on_language = function(lang)
      told[#told + 1] = lang
    end
    shown(t, "src/app.rs", "x\n")
    file.show({ path = "notes.nonesuch", text = "y\n" })
    expect(told).to_equal({ "rust" })
  end)

  btv.test.it("refuses what it doesn't understand, naming it", function()
    expect(function()
      file.show({ path = "a" })
    end).to_error("no text")
    expect(function()
      file.show({ text = "a" })
    end).to_error("no path")
  end)
end)
