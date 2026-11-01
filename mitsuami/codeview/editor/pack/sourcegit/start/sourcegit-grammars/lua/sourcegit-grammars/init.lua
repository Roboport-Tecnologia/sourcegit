-- The Code View preference "Fetch and build syntax highlighting": a language with no
-- grammar gets one, by `:TSInstall`, as what's in it shows. Whoever shows it calls
-- `ensure`, and hears of the grammar through `on_ready`. Windows each have an editor, which
-- only hears of its own installs, so this tells the app (`grammar_ready`), and the app tells
-- the others (`ready`).

local M = {}

local enabled = false
-- The languages asked for already, here or by another editor, which the app keeps: a
-- failed install shouldn't run again for every file, and the editor says why it failed.
local tried = {}

-- The `TSInstalled` handler, so a second `setup` replaces it.
local listening
-- The language last asked about, to catch up on when this is turned on.
local last

-- Told when a grammar this editor installed is ready.
M.on_ready = function(_) end

-- Tells the app, or nobody in a plain terminal. A field, so specs can hear it with no app.
M.send = function(name, payload)
  if btv.host.name() then
    btv.host.send(name, payload)
  end
end

-- Runs the install. A field, so specs can see what it's asked without the network.
M.install = function(lang)
  btv.cmd("TSInstall " .. lang)
end

-- Installs `lang`'s grammar if it's on, missing and wasn't tried.
function M.ensure(lang)
  last = lang
  if not enabled or not lang or lang == "" or tried[lang] then
    return
  end
  tried[lang] = true
  btv.treesitter.installed():next(function(langs)
    if btv.tbl.contains(langs, lang) then
      return
    end
    M.install(lang)
    M.send("grammar_install", { lang = lang })
  end)
end

-- Takes `spec`: { enabled, tried = { lang, … } }. Turned on, what's showing gets its grammar.
function M.configure(spec)
  assert(type(spec) == "table", "sourcegit-grammars: the settings aren't a table")
  assert(
    type(spec.enabled) == "boolean",
    "sourcegit-grammars: bad enabled: " .. tostring(spec.enabled)
  )
  enabled = spec.enabled
  tried = {}
  for _, lang in ipairs(spec.tried or {}) do
    tried[lang] = true
  end
  if last then
    M.ensure(last)
  end
end

-- Another editor installed `spec.lang`: this one looks again, as it remembers there was none.
function M.ready(spec)
  assert(type(spec) == "table" and type(spec.lang) == "string", "sourcegit-grammars: no lang")
  btv.treesitter.reload(spec.lang)
  M.on_ready(spec.lang)
end

function M.setup()
  if listening then
    btv.off(listening)
  end
  listening = btv.on("TSInstalled", function(args)
    if args.data.ok then
      M.on_ready(args.data.lang)
      M.send("grammar_ready", { lang = args.data.lang })
    end
  end)
end

return M
