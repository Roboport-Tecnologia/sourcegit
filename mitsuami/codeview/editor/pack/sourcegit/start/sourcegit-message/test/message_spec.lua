local message = require("sourcegit-message")
local expect = btv.test.expect

btv.test.describe("sourcegit-message", function()
  local heard
  local real_send

  btv.test.before_each(function()
    heard = {}
    real_send = btv.host.send
    btv.host.name = function()
      return "test"
    end
    btv.host.send = function(name, payload)
      heard[#heard + 1] = { name = name, payload = payload }
    end
    message.reset()
  end)

  btv.test.after_each(function()
    btv.host.send = real_send
  end)

  local function said(name)
    local out = {}
    for _, e in ipairs(heard) do
      if e.name == name then
        out[#out + 1] = e.payload
      end
    end
    return out
  end

  btv.test.it("sets the filetype, so a gitcommit grammar colours it", function(t)
    local asked
    message.on_language = function(lang)
      asked = lang
    end
    message.show({ text = "subject", revision = 1 })
    t:wait_for(function()
      return btv.bo[0].filetype == "gitcommit"
    end, { message = "no filetype" })
    expect(asked).to_equal("gitcommit")
    message.on_language = function(_) end
  end)

  btv.test.it("shows the text, and doesn't tell the app what it told it", function(t)
    message.show({ text = "subject\n\nbody\n", revision = 1 })
    t:wait_for(function()
      return #t:lines() == 4
    end, { message = "the text never showed" })
    expect(t:lines()).to_equal({ "subject", "", "body", "" })
    t:sleep(20)
    t:sleep(20)
    expect(#said("message_changed")).to_equal(0)
  end)

  btv.test.it("tells the app what's typed, with the text it was typed on", function(t)
    message.show({ text = "", revision = 7 })
    t:sleep(20)
    t:feed("ifix<Esc>")
    t:wait_for(function()
      local all = said("message_changed")
      return #all > 0 and all[#all].text == "fix"
    end, { message = "the app never heard" })
    local all = said("message_changed")
    expect(all[#all].revision).to_equal(7)
    expect(btv.bo[0].modified).to_equal(false)
  end)

  btv.test.it("hears typing with no Normal mode", function(t)
    btv.o.modeless = true
    message.show({ text = "", revision = 1 })
    t:sleep(20)
    t:feed("abc")
    t:wait_for(function()
      local all = said("message_changed")
      return #all > 0 and all[#all].text == "abc"
    end, { message = "the app never heard" })
    btv.o.modeless = false
  end)

  btv.test.it("takes the app's new text over what's there", function(t)
    message.show({ text = "old", revision = 1 })
    t:sleep(20)
    message.show({ text = "", revision = 2 })
    t:wait_for(function()
      return t:lines()[1] == ""
    end, { message = "the text stayed" })
    expect(#said("message_changed")).to_equal(0)
  end)

  btv.test.it("shows the placeholder only while it's empty", function(t)
    message.show({ text = "", revision = 1, placeholder = "Enter a message" })
    t:wait_for(function()
      return (t:decor(1).virt_text or ""):find("Enter a message") ~= nil
    end, { message = "no placeholder" })
    message.show({ text = "x", revision = 2 })
    t:wait_for(function()
      return t:lines()[1] == "x" and t:decor(1).virt_text == nil
    end, { message = "the placeholder stayed" })
  end)

  btv.test.it("keeps a box's look when the preferences set numbers", function(t)
    message.show({ text = "x", revision = 1 })
    t:sleep(20)
    expect(btv.wo[0].number).to_equal(false)
    expect(btv.wo[0].wrap).to_equal(true)
    btv.wo[0].relativenumber = true
    message.dress()
    expect(btv.wo[0].relativenumber).to_equal(false)
  end)

  btv.test.it("sends the chords to the app", function(t)
    message.show({ text = "x", revision = 1 })
    t:sleep(20)
    t:feed("<C-CR>")
    t:feed("<C-S-CR>")
    t:wait_for(function()
      return #said("message_action") == 2
    end, { message = "the app never heard the chords" })
    expect(said("message_action")[1].action).to_equal("commit")
    expect(said("message_action")[2].action).to_equal("commit_stage")
  end)

  -- Last: the reset after Helix can leave the next test's modeless off.
  btv.test.it("hears typing in Helix", function(t)
    btv.helix.enable()
    message.show({ text = "", revision = 1 })
    t:sleep(20)
    t:feed("ifix<Esc>")
    t:wait_for(function()
      local all = said("message_changed")
      return #all > 0 and all[#all].text == "fix"
    end, { message = "the app never heard: " .. btv.inspect(t:lines()) })
    btv.helix.disable()
  end)
end)
