#!/bin/sh
# Sobe o emulador de referência: gera as confs a partir de config.lua, espera o
# banco, importa o schema na primeira execução e inicia login + char em
# background com o map-server em foreground.
set -eu

CONFIG_LUA="${CONFIG_LUA:-/opt/cronus/config.lua}"
CONF_IMPORT_DIR="${CONF_IMPORT_DIR:-/opt/cronus/conf/import}"
DB_ENV_FILE=/tmp/db.env

echo "[entrypoint] gerando conf a partir de $CONFIG_LUA"
mkdir -p "$CONF_IMPORT_DIR"
lua5.4 /usr/local/bin/render_conf.lua "$CONFIG_LUA" "$CONF_IMPORT_DIR" "$DB_ENV_FILE"

# Credenciais vêm do mesmo config.lua, sem duplicação aqui.
# shellcheck disable=SC1090
. "$DB_ENV_FILE"

# MYSQL_PWD evita o aviso "Using a password on the command line" em cada chamada.
export MYSQL_PWD="$DB_PASS"

mysql_do() {
    mysql --host="$DB_HOST" --port="$DB_PORT" --user="$DB_USER" "$@"
}

echo "[entrypoint] aguardando ${DB_HOST}:${DB_PORT} ..."
i=0
until mysqladmin ping --host="$DB_HOST" --port="$DB_PORT" --user="$DB_USER" \
        --silent 2>/dev/null; do
    i=$((i + 1))
    if [ "$i" -ge 60 ]; then
        echo "[entrypoint] banco não respondeu em 120s, abortando" >&2
        exit 1
    fi
    sleep 2
done
echo "[entrypoint] banco pronto"

# Importa o schema apenas se a tabela `login` ainda não existir.
if ! mysql_do --batch --skip-column-names -e \
        "SELECT 1 FROM information_schema.tables \
         WHERE table_schema='${DB_NAME}' AND table_name='login' LIMIT 1" \
        | grep -q 1; then
    echo "[entrypoint] importando sql-files/ ..."

    # `TYPE=MyISAM` foi removido do MySQL/MariaDB; dumps antigos ainda usam.
    normalize() { sed 's/TYPE=MyISAM/ENGINE=MyISAM/g' "$1"; }

    # main.sql é o schema essencial: falha aqui é fatal.
    echo "[entrypoint]   + main.sql"
    normalize sql-files/main.sql | mysql_do "$DB_NAME"

    # Os demais (logs.sql e os extras do fork: vip, cash, pvp, ...) se sobrepõem
    # parcialmente ao main.sql e não usam IF NOT EXISTS, então "table already
    # exists" é esperado. --force segue adiante e a falha não é fatal.
    for f in sql-files/*.sql; do
        [ "$(basename "$f")" = "main.sql" ] && continue
        echo "[entrypoint]   + $(basename "$f") (complementar)"
        normalize "$f" | mysql_do --force "$DB_NAME" 2>&1 \
            | grep -v "already exists" \
            | sed 's/^/[entrypoint]     /' || true
    done
else
    echo "[entrypoint] schema já presente, pulando import"
fi

# Conta inter-server usada por char/map para falar com o login (sex='S').
mysql_do "$DB_NAME" -e \
    "INSERT INTO login (account_id, userid, user_pass, sex, email) \
     VALUES (1, '${INTER_USERID}', '${INTER_PASSWD}', 'S', 'athena@localhost') \
     ON DUPLICATE KEY UPDATE user_pass='${INTER_PASSWD}', sex='S';" \
    || echo "[entrypoint] ! não foi possível garantir a conta inter-server"

# `make sql` gera binários com sufixo (login-server_sql); `make` sem SQL geraria
# login-server. Resolve o nome em vez de assumir um deles.
resolve_binary() {
    for candidate in "./$1_sql" "./$1"; do
        if [ -x "$candidate" ]; then
            echo "$candidate"
            return 0
        fi
    done
    echo "[entrypoint] binário de $1 não encontrado em $(pwd)" >&2
    exit 1
}

LOGIN_BIN=$(resolve_binary login-server)
CHAR_BIN=$(resolve_binary char-server)
MAP_BIN=$(resolve_binary map-server)

echo "[entrypoint] iniciando $LOGIN_BIN e $CHAR_BIN"
"$LOGIN_BIN" &
sleep 2
"$CHAR_BIN" &
sleep 2
echo "[entrypoint] iniciando $MAP_BIN (foreground)"
exec "$MAP_BIN"
