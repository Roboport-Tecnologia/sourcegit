-- What a diff is, apart from how it shows. Git gives every line of the file as context
-- (the app asks it to), so its lines hold both versions whole: the old one is the normal
-- and deleted lines, the new one the normal and added ones. Which lines show, and how
-- they're laid out, then needs no more from git.

local M = {}

local CHANGED = { added = true, deleted = true }

-- Git's lines, without its hunk headers: with the whole file as context they only say
-- where the file starts.
function M.lines(spec)
  local out = {}
  for _, line in ipairs(spec.lines) do
    if line.kind ~= "indicator" then
      out[#out + 1] = line
    end
  end
  return out
end

-- Both versions as text, in the file's own lines. A line git didn't send stays empty, so
-- every line keeps its number.
function M.versions(lines)
  local old, new = {}, {}
  local old_count, new_count = 0, 0
  for _, line in ipairs(lines) do
    if line.old > 0 then
      old[line.old] = line.text
      old_count = math.max(old_count, line.old)
    end
    if line.new > 0 then
      new[line.new] = line.text
      new_count = math.max(new_count, line.new)
    end
  end
  for i = 1, old_count do
    old[i] = old[i] or ""
  end
  for i = 1, new_count do
    new[i] = new[i] or ""
  end
  return table.concat(old, "\n"), table.concat(new, "\n")
end

-- The lines that show, `context` lines around each change, with `{ gap = n }` for the
-- `n` lines between them that don't. No `context`: all of them.
function M.visible(lines, context)
  if not context then
    return lines
  end
  local keep = {}
  for i, line in ipairs(lines) do
    if CHANGED[line.kind] then
      for j = math.max(1, i - context), math.min(#lines, i + context) do
        keep[j] = true
      end
    end
  end
  local out = {}
  local gap = 0
  for i, line in ipairs(lines) do
    if keep[i] then
      if gap > 0 then
        out[#out + 1] = { gap = gap }
        gap = 0
      end
      out[#out + 1] = line
    else
      gap = gap + 1
    end
  end
  if gap > 0 then
    out[#out + 1] = { gap = gap }
  end
  return out
end

-- One pane's rows, one per line, as git ordered them.
function M.unified(visible)
  return { visible }
end

-- Two panes of the same length: unchanged lines side by side, and in a run of changes each
-- deleted line beside an added one, with `{ filler = true }` where one side has fewer.
function M.side_by_side(visible)
  local left, right = {}, {}
  local deleted, added = {}, {}
  local function flush()
    for i = 1, math.max(#deleted, #added) do
      left[#left + 1] = deleted[i] or { filler = true }
      right[#right + 1] = added[i] or { filler = true }
    end
    deleted, added = {}, {}
  end
  for _, row in ipairs(visible) do
    if row.kind == "deleted" then
      deleted[#deleted + 1] = row
    elseif row.kind == "added" then
      added[#added + 1] = row
    else
      flush()
      left[#left + 1] = row
      right[#right + 1] = row
    end
  end
  flush()
  return { left, right }
end

-- The rows where a run of changes starts. Rows line up across panes, so any pane's do,
-- given each pane's change at that row: a run starts where either pane has one.
function M.hunks(panes)
  local out = {}
  local inside = false
  for row = 1, #panes[1] do
    local changed = false
    for _, pane in ipairs(panes) do
      local cell = pane[row]
      changed = changed or CHANGED[cell.kind] or cell.filler or false
    end
    if changed and not inside then
      out[#out + 1] = row
    end
    inside = changed
  end
  return out
end

-- The row showing `line`'s place in the file (the same old or new number), or nil.
function M.row_of(pane, old, new)
  for row, cell in ipairs(pane) do
    if (old and old > 0 and cell.old == old) or (new and new > 0 and cell.new == new) then
      return row
    end
  end
  return nil
end

return M
