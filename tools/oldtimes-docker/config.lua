-- Configuração do emulador de referência — FONTE DE VERDADE ÚNICA.
--
-- O emulador (Cronus, em C) só sabe ler o formato legado `chave: valor`, então
-- render_conf.lua gera a partir daqui os arquivos que ele espera em
-- conf/import/ (inter_conf.txt, char_conf.txt, map_conf.txt, login_conf.txt).
-- Edite apenas este arquivo.

return {
  -- Banco de dados. O fork já usa ragnarok/ragnarok/ragnarok por padrão;
  -- no Docker só o host muda (nome do serviço no compose).
  db = {
    host = "db",
    port = 3306,
    user = "ragnarok",
    password = "ragnarok",
    database = "ragnarok",
  },

  -- Banco de logs (o emulador permite separar; aqui compartilha o mesmo).
  log_db = {
    host = "db",
    port = 3306,
    user = "ragnarok",
    password = "ragnarok",
    database = "ragnarok",
  },

  -- Conta inter-server usada por char/map para autenticar no login.
  -- O entrypoint garante a linha correspondente na tabela `login` (sex = 'S').
  inter_server_account = {
    userid = "s1",
    passwd = "p1",
  },

  -- Os três servidores rodam no MESMO container, por isso 127.0.0.1 para a
  -- comunicação interna. Os campos char_ip/map_ip também são o endereço
  -- ANUNCIADO ao client: com as portas publicadas pelo Docker, um client no
  -- host Windows chega em 127.0.0.1 corretamente.
  login = {
    ip = "127.0.0.1",
    port = 6900,
  },

  char_server = {
    ip = "127.0.0.1",
    port = 6121,
    server_name = "backlands-server",
  },

  map = {
    ip = "127.0.0.1",
    port = 5121,
  },
}
