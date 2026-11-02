-- A commit message, in the editor's one file. The app owns the text: what's typed goes to it
-- as it's typed, and the file is only what a single-file editor needs to edit. Nothing reads
-- it, so saving means nothing here, and leaving never asks to.

local M = {}

-- What `show` set or what was last typed, so what the editor does to the buffer isn't told
-- back to the app.
local last
-- The app's count of the texts it sent: it drops what was typed before its latest one.
local revision = 0
local placeholder = ""
local listening = false

local namespace = btv.ns.create("sourcegit-message")

-- The app's, on chords that type nothing. Control, or Command where the layout uses it.
local KEYS = {
  ["CR"] = "commit",
  ["S-CR"] = "commit_stage",
  ["A-CR"] = "commit_push",
  ["S-s"] = "stage_all",
  ["S-u"] = "unstage_all",
}

local function send(name, payload)
  if btv.host.name() then
    btv.host.send(name, payload)
  end
end

local function lines_of(text)
  local out = {}
  for line in (text:gsub("\r\n", "\n") .. "\n"):gmatch("(.-)\n") do
    out[#out + 1] = line
  end
  return out
end

-- The hint over an empty message, as the platform's text view had.
local function show_placeholder(buf, text)
  btv.buf.clear_namespace(buf, namespace, 0, -1)
  if text == "" and placeholder ~= "" then
    btv.buf.set_extmark(buf, namespace, 0, 0, {
      virt_text = { { placeholder, "Comment" } },
      virt_text_pos = "overlay",
    })
  end
end

local function changed(args)
  local text = table.concat(btv.buf.lines(args.buf, 0, -1, false), "\n")
  btv.bo[args.buf].modified = false
  if text == last then
    return
  end
  last = text
  show_placeholder(args.buf, text)
  send("message_changed", { text = text, revision = revision })
end

local function listen(buf)
  if listening then
    return
  end
  listening = true
  btv.on({ "TextChanged", "TextChangedI" }, { buffer = buf }, changed)
  for chord, action in pairs(KEYS) do
    local function act()
      send("message_action", { action = action })
    end
    for _, prefix in ipairs({ "C", "D" }) do
      btv.keymap.set(
        { "n", "i", "modeless" },
        "<" .. prefix .. "-" .. chord .. ">",
        act,
        { buffer = buf, desc = "sourcegit-message: " .. action }
      )
    end
  end
end

-- A text box's look: the box is a few lines high, and has no room for the editor's bars.
-- Again after each preference, which sets windows' numbers.
function M.dress()
  btv.o.laststatus = 0
  btv.o.showtabline = 0
  local win = btv.win.current()
  btv.wo[win].number = false
  btv.wo[win].relativenumber = false
  btv.wo[win].signcolumn = "no"
  btv.wo[win].wrap = true
  btv.wo[win].linebreak = true
end

-- Puts `spec` ({ text, revision, placeholder }) in the file: the app's text changed, or the
-- editor started.
function M.show(spec)
  assert(type(spec) == "table", "sourcegit-message: the message isn't a table")
  assert(type(spec.text) == "string", "sourcegit-message: the message has no text")
  local buf = btv.buf.current()
  revision = spec.revision or revision
  placeholder = spec.placeholder or placeholder
  listen(buf)
  M.dress()
  btv.bo[buf].filetype = "gitcommit"
  local text = spec.text:gsub("\r\n", "\n")
  if text ~= last then
    last = text
    btv.buf.set_lines(buf, 0, -1, false, lines_of(text))
    btv.bo[buf].modified = false
  end
  show_placeholder(buf, text)
  M.on_language("gitcommit")
  send("shown", { language = "gitcommit" })
end

-- Told the language, as `sourcegit-grammars` wants to know.
M.on_language = function(_) end

-- The listeners go with the buffer: for specs, which start again in one editor.
function M.reset()
  last = nil
  revision = 0
  placeholder = ""
  listening = false
end

return M
