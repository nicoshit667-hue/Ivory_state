--[[
  Module Nakama d'Ivory State : profils et portefeuille des joueurs.

  Toutes ces RPC sont réservées au SERVEUR DE JEU, appelées avec la clé secrète runtime.http_key.
  Un client authentifié (qui a un user_id dans son contexte) est refusé : un téléphone ne peut
  donc ni créditer son portefeuille ni modifier son profil directement. Il passe par le serveur
  de jeu, qui applique les règles du jeu.

  Portefeuille : clé "fcfa" du wallet Nakama, mise à jour atomique avec historique (ledger), donc
  chaque gain et dépense est tracé avec sa raison. Nakama refuse toute opération qui rendrait le
  solde négatif.
]]

local nk = require("nakama")

local COLLECTION = "profile"
local KEY = "main"

-- Refuse tout appel venant d'un client (seul le serveur de jeu, via http_key, est autorisé).
local function require_server(context)
  if context.user_id ~= nil and context.user_id ~= "" then
    error({ "réservé au serveur de jeu", 7 }) -- 7 = PERMISSION_DENIED
  end
end

local function decode(payload)
  local ok, request = pcall(nk.json_decode, payload)
  if not ok or type(request) ~= "table" or type(request.user_id) ~= "string" or request.user_id == "" then
    error({ "requête invalide", 3 }) -- 3 = INVALID_ARGUMENT
  end
  return request
end

local function wallet_balance(user_id)
  local account = nk.account_get_id(user_id)
  local wallet = account.wallet
  if type(wallet) == "string" then
    wallet = nk.json_decode(wallet)
  end
  return wallet and wallet.fcfa
end

--[[
  Studios en colocation : chaque joueur reçoit un studio de la Résidence de Treichville, partagé avec
  un autre vrai joueur (2 colocataires au plus).
  - Registre global (objets système) : collection "studios", clé "studio_<n>" → { members, names }.
  - Studio de chaque joueur : collection "home", clé "main" → { studio }. Séparé du profil, pour
    qu'une sauvegarde du profil ne puisse pas l'effacer.
  On complète d'abord un studio où vit déjà une personne seule, sinon on ouvre un studio vide.
  Écriture avec version (concurrence optimiste) : en cas de conflit, on recommence.
]]
local SYSTEM_USER = "00000000-0000-0000-0000-000000000000"
local STUDIO_CAPACITY = 2

local function studio_key(n)
  return "studio_" .. n
end

local function read_home(user_id)
  local objects = nk.storage_read({ { collection = "home", key = "main", user_id = user_id } })
  if objects[1] ~= nil and type(objects[1].value) == "table" then
    return tonumber(objects[1].value.studio) or 0
  end
  return 0
end

local function roommate_of(studio, user_id)
  if studio <= 0 then
    return ""
  end
  local objects = nk.storage_read({ { collection = "studios", key = studio_key(studio), user_id = SYSTEM_USER } })
  if objects[1] == nil then
    return ""
  end
  local value = objects[1].value
  for i, member in ipairs(value.members or {}) do
    if member ~= user_id then
      return (value.names or {})[i] or ""
    end
  end
  return ""
end

local function assign_studio(user_id, pseudo, count)
  for _ = 1, 3 do
    local reads = {}
    for n = 1, count do
      reads[n] = { collection = "studios", key = studio_key(n), user_id = SYSTEM_USER }
    end
    local existing = {}
    for _, object in ipairs(nk.storage_read(reads)) do
      existing[object.key] = object
    end

    -- 1) un studio avec une seule personne ; 2) sinon le premier studio vide.
    local chosen, chosen_object = nil, nil
    for n = 1, count do
      local object = existing[studio_key(n)]
      local members = object and object.value.members or {}
      if #members == 1 and members[1] ~= user_id then
        chosen, chosen_object = n, object
        break
      end
    end
    if chosen == nil then
      for n = 1, count do
        local object = existing[studio_key(n)]
        if object == nil or #(object.value.members or {}) == 0 then
          chosen, chosen_object = n, object
          break
        end
      end
    end
    if chosen == nil then
      nk.logger_warn("Résidence pleine : aucun studio libre pour " .. user_id)
      return 0
    end

    local value = chosen_object and chosen_object.value or { members = {}, names = {} }
    value.members = value.members or {}
    value.names = value.names or {}
    if #value.members < STUDIO_CAPACITY then
      table.insert(value.members, user_id)
      table.insert(value.names, pseudo or "")
      local write = {
        collection = "studios", key = studio_key(chosen), user_id = SYSTEM_USER, value = value,
        permission_read = 0, permission_write = 0,
        version = chosen_object and chosen_object.version or "*", -- "*" : n'existe pas encore
      }
      local ok = pcall(nk.storage_write, { write })
      if ok then
        nk.storage_write({ {
          collection = "home", key = "main", user_id = user_id, value = { studio = chosen },
          permission_read = 1, permission_write = 0,
        } })
        return chosen
      end
    end
  end
  return 0
end

-- Libère le studio d'un joueur (déménagement, compte supprimé). Entrée : { user_id }  Sortie : { studio }
local function home_release(context, payload)
  require_server(context)
  local request = decode(payload)
  local studio = read_home(request.user_id)
  if studio > 0 then
    for _ = 1, 3 do
      local objects = nk.storage_read({ { collection = "studios", key = studio_key(studio), user_id = SYSTEM_USER } })
      if objects[1] == nil then
        break
      end
      local value = objects[1].value
      local members, names = {}, {}
      for i, member in ipairs(value.members or {}) do
        if member ~= request.user_id then
          table.insert(members, member)
          table.insert(names, (value.names or {})[i] or "")
        end
      end
      local ok = pcall(nk.storage_write, { {
        collection = "studios", key = studio_key(studio), user_id = SYSTEM_USER,
        value = { members = members, names = names },
        permission_read = 0, permission_write = 0, version = objects[1].version,
      } })
      if ok then
        break
      end
    end
    nk.storage_delete({ { collection = "home", key = "main", user_id = request.user_id } })
  end
  return nk.json_encode({ studio = studio })
end
nk.register_rpc(home_release, "ivory_home_release")

-- Attribue un studio (ancien SDF qui a payé sa dette). Entrée : { user_id, pseudo, studio_count }
-- Sortie : { studio, roommate }
local function home_assign(context, payload)
  require_server(context)
  local request = decode(payload)
  local studio = read_home(request.user_id)
  local count = math.floor(tonumber(request.studio_count) or 0)
  if studio == 0 and count > 0 then
    studio = assign_studio(request.user_id, tostring(request.pseudo or ""), count)
  end
  return nk.json_encode({ studio = studio, roommate = roommate_of(studio, request.user_id) })
end
nk.register_rpc(home_assign, "ivory_home_assign")

--[[
  Biens immobiliers (appartements loués, maisons achetées) : registre global, objets système
  "properties" / "prop_<id>" → { owner, owner_name, members, names } (habitants : textes « a,b »,
  car un tableau Lua vide s'encoderait en objet JSON). Le serveur de jeu est
  l'autorité : il charge tout au démarrage et réécrit un bien à chaque changement.
]]
local function decode_any(payload)
  local ok, request = pcall(nk.json_decode, payload)
  if not ok or type(request) ~= "table" then
    error({ "requête invalide", 3 })
  end
  return request
end

-- Entrée : { count }   Sortie : { properties = [ { id, owner, owner_name, members, names } ] }
local function properties_load(context, payload)
  require_server(context)
  local request = decode_any(payload)
  local count = math.min(500, math.floor(tonumber(request.count) or 0))
  local reads = {}
  for n = 1, count do
    reads[n] = { collection = "properties", key = "prop_" .. n, user_id = SYSTEM_USER }
  end
  local result = {}
  if count > 0 then
    for _, object in ipairs(nk.storage_read(reads)) do
      local id = tonumber(string.sub(object.key, 6))
      local value = object.value
      table.insert(result, {
        id = id, owner = value.owner or "", owner_name = value.owner_name or "",
        members = value.members or "", names = value.names or "",   -- textes « a,b »
      })
    end
  end
  return nk.json_encode({ properties = result })
end
nk.register_rpc(properties_load, "ivory_properties_load")

-- Entrée : { id, owner, owner_name, members, names }   (owner vide : bien libre)
local function property_save(context, payload)
  require_server(context)
  local request = decode_any(payload)
  local id = math.floor(tonumber(request.id) or 0)
  if id <= 0 then
    error({ "bien invalide", 3 })
  end
  nk.storage_write({ {
    collection = "properties", key = "prop_" .. id, user_id = SYSTEM_USER,
    value = {
      owner = tostring(request.owner or ""), owner_name = tostring(request.owner_name or ""),
      members = string.sub(tostring(request.members or ""), 1, 1024),
      names = string.sub(tostring(request.names or ""), 1, 1024),
    },
    permission_read = 0, permission_write = 0,
  } })
  return nk.json_encode({ ok = true })
end
nk.register_rpc(property_save, "ivory_property_save")

--[[
  Djassa (vente entre joueurs) : annonces persistantes, objets système "djassa" / "ad_<id>" →
  { seller, seller_name, kind, item, price, created }. Le bien vendu est mis de côté par le serveur
  de jeu tant que l'annonce existe : elle doit survivre à un redémarrage.
]]
-- Entrée : {}   Sortie : { ads = [ { id, seller, seller_name, kind, item, price, created } ] }
local function djassa_load(context, payload)
  require_server(context)
  local ads = {}
  local cursor = nil
  repeat
    local objects, next_cursor = nk.storage_list(SYSTEM_USER, "djassa", 100, cursor)
    for _, object in ipairs(objects or {}) do
      local value = object.value
      table.insert(ads, {
        id = tonumber(string.sub(object.key, 4)) or 0,
        seller = value.seller or "", seller_name = value.seller_name or "",
        kind = value.kind or "", item = tonumber(value.item) or 0,
        extra = value.extra or "",
        price = tonumber(value.price) or 0, created = tonumber(value.created) or 0,
      })
    end
    cursor = next_cursor
  until cursor == nil or cursor == ""
  return nk.json_encode({ ads = ads })
end
nk.register_rpc(djassa_load, "ivory_djassa_load")

-- Entrée : { id, delete, seller, seller_name, kind, item, price, created }
local function djassa_save(context, payload)
  require_server(context)
  local request = decode_any(payload)
  local id = math.floor(tonumber(request.id) or 0)
  if id <= 0 then
    error({ "annonce invalide", 3 })
  end
  local key = "ad_" .. id
  if request.delete == true then
    nk.storage_delete({ { collection = "djassa", key = key, user_id = SYSTEM_USER } })
  else
    nk.storage_write({ {
      collection = "djassa", key = key, user_id = SYSTEM_USER,
      value = {
        seller = tostring(request.seller or ""), seller_name = tostring(request.seller_name or ""),
        kind = tostring(request.kind or ""), item = math.floor(tonumber(request.item) or 0),
        extra = string.sub(tostring(request.extra or ""), 1, 128),
        price = math.floor(tonumber(request.price) or 0), created = math.floor(tonumber(request.created) or 0),
      },
      permission_read = 0, permission_write = 0,
    } })
  end
  return nk.json_encode({ ok = true })
end
nk.register_rpc(djassa_save, "ivory_djassa_save")

--[[
  Commerces d'Abidjan (maquis, restaurants, fast-foods, boutiques, pharmacies) : registre global
  "commerces" / "comm_<id>" → { owner, owner_name, is_open, vault }.
]]
local function commerces_load(context, payload)
  require_server(context)
  local request = decode_any(payload)
  local count = math.min(200, math.floor(tonumber(request.count) or 0))
  local reads = {}
  for n = 1, count do
    reads[n] = { collection = "commerces", key = "comm_" .. n, user_id = SYSTEM_USER }
  end
  local result = {}
  if count > 0 then
    for _, object in ipairs(nk.storage_read(reads)) do
      local id = tonumber(string.sub(object.key, 6))
      local value = object.value
      table.insert(result, {
        id = id,
        owner = value.owner or "",
        owner_name = value.owner_name or "",
        is_open = value.is_open ~= false,
        vault = math.max(0, tonumber(value.vault) or 0),
      })
    end
  end
  return nk.json_encode({ commerces = result })
end
nk.register_rpc(commerces_load, "ivory_commerces_load")

local function commerce_save(context, payload)
  require_server(context)
  local request = decode_any(payload)
  local id = math.floor(tonumber(request.id) or 0)
  if id <= 0 then
    error({ "commerce invalide", 3 })
  end
  nk.storage_write({ {
    collection = "commerces", key = "comm_" .. id, user_id = SYSTEM_USER,
    value = {
      owner = tostring(request.owner or ""),
      owner_name = tostring(request.owner_name or ""),
      is_open = request.is_open ~= false,
      vault = math.max(0, math.floor(tonumber(request.vault) or 0)),
    },
    permission_read = 0, permission_write = 0,
  } })
  return nk.json_encode({ ok = true })
end
nk.register_rpc(commerce_save, "ivory_commerce_save")

-- Charge (ou crée) le profil d'un joueur.
-- Entrée : { user_id, starting_balance, pseudo, studio_count }
-- Sortie : { balance, satiety, hydration, health, pseudo, …, studio, roommate, created }
local function load_profile(context, payload)
  require_server(context)
  local request = decode(payload)

  local balance = wallet_balance(request.user_id)
  local created = false
  if balance == nil then
    local starting = tonumber(request.starting_balance) or 0
    nk.wallet_update(request.user_id, { fcfa = starting }, { reason = "bienvenue" }, true)
    balance = starting
    created = true
  end

  local profile = {}
  local objects = nk.storage_read({ { collection = COLLECTION, key = KEY, user_id = request.user_id } })
  if objects[1] ~= nil then
    profile = objects[1].value
  end

  -- Studio : attribué au premier chargement d'un compte qui a un pseudo (studio_count > 0).
  local studio = read_home(request.user_id)
  local count = math.floor(tonumber(request.studio_count) or 0)
  local pseudo = profile.pseudo
  if pseudo == nil or pseudo == "" then
    pseudo = tostring(request.pseudo or "")
  end
  if studio == 0 and count > 0 and pseudo ~= "" then
    studio = assign_studio(request.user_id, pseudo, count)
  end

  return nk.json_encode({
    studio = studio,
    roommate = roommate_of(studio, request.user_id),
    balance = balance,
    satiety = profile.satiety or 100,
    hydration = profile.hydration or 100,
    health = profile.health or 100,
    pseudo = profile.pseudo or "",
    inventory = profile.inventory or "",
    model = profile.model or -1,   -- apparence (-1 : personnage pas encore créé)
    outfit = profile.outfit or 0,
    rent_paid_until = profile.rent_paid_until or 0, -- loyer payé jusqu'à (secondes Unix)
    rent_debt = profile.rent_debt or 0,
    homeless = profile.homeless or false,
    aid_at = profile.aid_at or 0,                   -- dernière aide publique
    outfits = profile.outfits or 1,                 -- tenues achetées (bit i = tenue i)
    license = profile.license or false,
    cars = profile.cars or "",
    rental_until = profile.rental_until or 0,
    rental_paint = profile.rental_paint or 0,
    last_x = profile.last_x or 0,
    last_y = profile.last_y or 0,
    last_z = profile.last_z or 0,
    last_seen = profile.last_seen or 0,
    primary_home = profile.primary_home or 0,
    cash = profile.cash,   -- nil (jamais enregistré) : le serveur de jeu donne les espèces de départ
    friends = profile.friends or "",   -- « clé|pseudo;… »
    history = profile.history or "",   -- historique Pingoin « date|montant|raison;… »
    xp = profile.xp or 0,
    job_status = profile.job_status or 0,
    profile_views = profile.profile_views or 0,
    profile_privacy = profile.profile_privacy or 0,
    commerces = profile.commerces or "",
    commerce_vault = profile.commerce_vault or 0,
    created = created,
  })
end
nk.register_rpc(load_profile, "ivory_load_profile")

-- Applique un gain ou une dépense. Entrée : { user_id, delta, reason }    Sortie : { balance }
local function wallet_apply(context, payload)
  require_server(context)
  local request = decode(payload)
  local delta = math.floor(tonumber(request.delta) or 0)
  if delta == 0 then
    return nk.json_encode({ balance = wallet_balance(request.user_id) })
  end

  local reason = tostring(request.reason or "")
  local ok, err = pcall(nk.wallet_update, request.user_id, { fcfa = delta }, { reason = reason }, true)
  if not ok then
    nk.logger_warn(("Portefeuille refusé pour %s (%d, %s) : %s"):format(request.user_id, delta, reason, tostring(err)))
    error({ "solde insuffisant", 9 }) -- 9 = FAILED_PRECONDITION
  end

  return nk.json_encode({ balance = wallet_balance(request.user_id) })
end
nk.register_rpc(wallet_apply, "ivory_wallet_apply")

local function opt_num(value, previous, default)
  if value ~= nil then
    return tonumber(value) or default
  end
  if previous ~= nil then
    return tonumber(previous) or default
  end
  return default
end

local function opt_int(value, previous, default)
  return math.floor(opt_num(value, previous, default))
end

local function opt_str(value, previous, max_len)
  local text
  if value ~= nil then
    text = tostring(value)
  elseif previous ~= nil then
    text = tostring(previous)
  else
    text = ""
  end
  return max_len and string.sub(text, 1, max_len) or text
end

local function opt_bool(value, previous)
  if value ~= nil then
    return value == true
  end
  return previous == true
end

-- Sauvegarde le profil (pseudo, jauges, permis, garage, espèces…). Lisible par le joueur, modifiable par
-- le serveur seulement. Les champs absents de la requête conservent leur valeur existante en base afin
-- qu'un build plus ancien ne puisse jamais effacer le permis, les voitures ou les espèces.
local function save_profile(context, payload)
  require_server(context)
  local request = decode(payload)

  local existing = nk.storage_read({ { collection = COLLECTION, key = KEY, user_id = request.user_id } })
  local prev = (existing[1] ~= nil and type(existing[1].value) == "table") and existing[1].value or {}

  local cash = nil
  if request.cash ~= nil or prev.cash ~= nil then
    cash = math.max(0, opt_int(request.cash, prev.cash, 0))
  end

  nk.storage_write({ {
    collection = COLLECTION,
    key = KEY,
    user_id = request.user_id,
    value = {
      satiety = opt_num(request.satiety, prev.satiety, 100),
      hydration = opt_num(request.hydration, prev.hydration, 100),
      health = opt_num(request.health, prev.health, 100),
      pseudo = opt_str(request.pseudo, prev.pseudo),
      -- Sac du joueur, texte compact « nom|faim|soif|nombre;… » (borné : 2 Ko au plus).
      inventory = opt_str(request.inventory, prev.inventory, 2048),
      model = opt_int(request.model, prev.model, -1),
      outfit = opt_int(request.outfit, prev.outfit, 0),
      friends = opt_str(request.friends, prev.friends, 4096),
      history = opt_str(request.history, prev.history, 4096),
      rent_paid_until = opt_int(request.rent_paid_until, prev.rent_paid_until, 0),
      rent_debt = math.max(0, opt_int(request.rent_debt, prev.rent_debt, 0)),
      homeless = opt_bool(request.homeless, prev.homeless),
      aid_at = opt_int(request.aid_at, prev.aid_at, 0),
      outfits = opt_int(request.outfits, prev.outfits, 1),   -- garde-robe (bit i = tenue i)
      license = opt_bool(request.license, prev.license),     -- permis de conduire
      cars = opt_str(request.cars, prev.cars, 64),           -- voitures achetées « 1,3 »
      rental_until = opt_int(request.rental_until, prev.rental_until, 0),
      rental_paint = opt_int(request.rental_paint, prev.rental_paint, 0),
      -- Reconnexion : dernière position et heure ; domicile principal choisi (clé de logement).
      last_x = opt_num(request.last_x, prev.last_x, 0),
      last_y = opt_num(request.last_y, prev.last_y, 0),
      last_z = opt_num(request.last_z, prev.last_z, 0),
      last_seen = opt_int(request.last_seen, prev.last_seen, 0),
      primary_home = opt_int(request.primary_home, prev.primary_home, 0),
      cash = cash,   -- espèces (le solde Pingoin est le wallet)
      xp = math.max(0, opt_int(request.xp, prev.xp, 0)),
      job_status = math.max(0, opt_int(request.job_status, prev.job_status, 0)),
      profile_views = math.max(0, opt_int(request.profile_views, prev.profile_views, 0)),
      profile_privacy = math.max(0, opt_int(request.profile_privacy, prev.profile_privacy, 0)),
      commerces = opt_str(request.commerces, prev.commerces, 256),
      commerce_vault = math.max(0, opt_int(request.commerce_vault, prev.commerce_vault, 0)),
    },
    permission_read = 1,  -- le propriétaire peut lire
    permission_write = 0, -- seul le serveur écrit
  } })

  return nk.json_encode({ ok = true })
end
nk.register_rpc(save_profile, "ivory_save_profile")

nk.logger_info("Module Ivory State chargé : ivory_load_profile, ivory_wallet_apply, ivory_save_profile")
