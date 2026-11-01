-- The Code View preference "Fetch and build syntax highlighting": a language with no
-- grammar gets one, by `:TSInstall`, as what's in it shows. Whoever shows it calls
-- `ensure`, and hears of the grammar through `on_ready`.

local M = {}

local enabled = false
-- The languages asked for already, here or by another editor, which the app keeps: a
-- failed install shouldn't run again for every file, and the editor says why it failed.
local tried = {}

-- The language last asked about, to catch up on when this is turned on.
local last

-- Told when a grammar this editor installed is ready.
M.on_ready = function(_) end

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
    if btv.host.name() then
      btv.host.send("grammar_install", { lang = lang })
    end
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

function M.setup()
  btv.on("TSInstalled", function(args)
    if args.data.ok then
      M.on_ready(args.data.lang)
    end
  end)
end

return M
