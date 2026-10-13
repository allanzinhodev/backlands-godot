#!/usr/bin/env lua5.4
-- Gera os arquivos de conf legados do emulador a partir de config.lua.
--
-- Uso: lua5.4 render_conf.lua <config.lua> <dir-de-saida> [<db.env>]
--
-- O emulador em C lê apenas `chave: valor`; manter o Lua como fonte única
-- evita três arquivos .txt divergindo entre si.

local config_path = arg[1] or "/opt/cronus/config.lua"
local out_dir = arg[2] or "/opt/cronus/conf/import"
local env_path = arg[3] -- opcional: variáveis de shell para o entrypoint

local chunk, err = loadfile(config_path)
if not chunk then
  io.stderr:write("[render_conf] não foi possível carregar " .. config_path .. ": " .. tostring(err) .. "\n")
  os.exit(1)
end

local cfg = chunk()
if type(cfg) ~= "table" then
  io.stderr:write("[render_conf] " .. config_path .. " precisa retornar uma tabela\n")
  os.exit(1)
end

-- Falha cedo e com mensagem clara em vez de gerar conf silenciosamente vazia.
local function need(value, path)
  if value == nil or value == "" then
    io.stderr:write("[render_conf] campo obrigatório ausente: " .. path .. "\n")
    os.exit(1)
  end
  return value
end

local function section(tbl, name)
  need(tbl[name], name)
  return tbl[name]
end

local db = section(cfg, "db")
local log_db = cfg.log_db or db
local account = section(cfg, "inter_server_account")
local login = section(cfg, "login")
local char_server = section(cfg, "char_server")
local map = section(cfg, "map")

local function write_file(name, header, lines)
  local path = out_dir .. "/" .. name
  local file, open_err = io.open(path, "w")
  if not file then
    io.stderr:write("[render_conf] não foi possível escrever " .. path .. ": " .. tostring(open_err) .. "\n")
    os.exit(1)
  end
  file:write("// GERADO por render_conf.lua a partir de config.lua — NÃO EDITE.\n")
  file:write("// " .. header .. "\n\n")
  for _, line in ipairs(lines) do
    file:write(line .. "\n")
  end
  file:close()
  print("[render_conf] " .. path)
end

write_file("inter_conf.txt", "Conexões com o banco de dados.", {
  -- As chaves sql.* são lidas apenas pelo login server.
  "sql.db_hostname: " .. need(db.host, "db.host"),
  "sql.db_port: " .. need(db.port, "db.port"),
  "sql.db_username: " .. need(db.user, "db.user"),
  "sql.db_password: " .. need(db.password, "db.password"),
  "sql.db_database: " .. need(db.database, "db.database"),
  "",
  "char_server_ip: " .. db.host,
  "char_server_port: " .. db.port,
  "char_server_id: " .. db.user,
  "char_server_pw: " .. db.password,
  "char_server_db: " .. db.database,
  "",
  "map_server_ip: " .. db.host,
  "map_server_port: " .. db.port,
  "map_server_id: " .. db.user,
  "map_server_pw: " .. db.password,
  "map_server_db: " .. db.database,
  "",
  "log_db_ip: " .. need(log_db.host, "log_db.host"),
  "log_db_port: " .. need(log_db.port, "log_db.port"),
  "log_db_id: " .. need(log_db.user, "log_db.user"),
  "log_db_pw: " .. need(log_db.password, "log_db.password"),
  "log_db_db: " .. need(log_db.database, "log_db.database"),
})

write_file("char_conf.txt", "Char server: liga no login e anuncia-se ao client.", {
  "login_ip: " .. need(login.ip, "login.ip"),
  "login_port: " .. need(login.port, "login.port"),
  "",
  "char_ip: " .. need(char_server.ip, "char_server.ip"),
  "char_port: " .. need(char_server.port, "char_server.port"),
  "server_name: " .. need(char_server.server_name, "char_server.server_name"),
  "",
  "userid: " .. need(account.userid, "inter_server_account.userid"),
  "passwd: " .. need(account.passwd, "inter_server_account.passwd"),
})

write_file("map_conf.txt", "Map server: liga no char e anuncia-se ao client.", {
  "char_ip: " .. char_server.ip,
  "char_port: " .. char_server.port,
  "",
  "map_ip: " .. need(map.ip, "map.ip"),
  "map_port: " .. need(map.port, "map.port"),
  "",
  "userid: " .. account.userid,
  "passwd: " .. account.passwd,
})

write_file("login_conf.txt", "Login server.", {
  "login_port: " .. login.port,
})

-- O entrypoint precisa das mesmas credenciais para esperar o banco e importar
-- o schema; exportá-las daqui evita duplicar valores no shell script.
if env_path then
  local file, open_err = io.open(env_path, "w")
  if not file then
    io.stderr:write("[render_conf] não foi possível escrever " .. env_path .. ": " .. tostring(open_err) .. "\n")
    os.exit(1)
  end
  file:write("# GERADO por render_conf.lua — NÃO EDITE.\n")
  file:write(string.format("DB_HOST=%s\n", db.host))
  file:write(string.format("DB_PORT=%s\n", db.port))
  file:write(string.format("DB_NAME=%s\n", db.database))
  file:write(string.format("DB_USER=%s\n", db.user))
  file:write(string.format("DB_PASS=%s\n", db.password))
  file:write(string.format("INTER_USERID=%s\n", account.userid))
  file:write(string.format("INTER_PASSWD=%s\n", account.passwd))
  file:close()
  print("[render_conf] " .. env_path)
end
