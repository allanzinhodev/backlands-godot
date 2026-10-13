# Emulador de referência (Cronus / Ragnarok-OldTimes) — SOMENTE para estudo de
# protocolo e de validação por células. Nenhum código daqui vai para o nosso server.
#
# Base: ubuntu:22.04 (gcc 11). Motivos:
#   - repositórios apt ainda ativos (20.04 foi para old-releases.ubuntu.com);
#   - `default-libmysqlclient-dev` = cliente MariaDB, que mantém `my_bool`
#     (removido no cliente MySQL 8 e usado por código da era eAthena);
#   - gcc 11 ainda trata declaração implícita de função como warning (erro só no gcc 14).
FROM ubuntu:22.04

ARG REPO_URL=https://github.com/JulioCF/Ragnarok-OldTimes.git
ARG REPO_REF=master
# -fcommon: gcc >= 10 usa -fno-common por padrão e o código antigo depende de
#   definições tentativas (senão: "multiple definition of ...").
# -Dmy_bool=bool: o MariaDB Connector/C 3.x removeu o typedef `my_bool` do
#   MySQL 5.x, que src/common/sql.c ainda usa. O header atual declara
#   MYSQL_BIND::is_null/error como `bool*` e inclui <stdbool.h>, então mapear
#   para `bool` casa exatamente (mapear para `char` compila, mas gera avisos de
#   ponteiro incompatível).
ARG EXTRA_CFLAGS="-fcommon -Dmy_bool=bool"

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        build-essential \
        default-libmysqlclient-dev \
        default-mysql-client \
        zlib1g-dev \
        libpcre3-dev \
        dos2unix \
        git \
        ca-certificates \
        lua5.4 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /opt/cronus

# O repositório não é vendorizado: clonamos no build.
RUN git clone --depth 1 --branch "${REPO_REF}" "${REPO_URL}" /tmp/oldtimes \
    && cp -a /tmp/oldtimes/emulador_oldtimes/Trunk/. /opt/cronus/ \
    && rm -rf /tmp/oldtimes

# Scripts do repo vêm com CRLF; sem isso o shell reclama de "\r: command not found".
RUN find . -maxdepth 1 -type f \( -name 'configure' -o -name '*.sh' -o -name 'athena-start' \) \
        -exec dos2unix -q {} + \
    && chmod +x configure athena-start *.sh 2>/dev/null || true

# --disable-lto: LTO é default neste configure e quebra de forma estranha em código de 2013.
# `make sql` é o alvo usado pelo CI do próprio repositório (.travis.yml).
RUN CFLAGS="${EXTRA_CFLAGS}" ./configure --disable-lto \
    && make clean \
    && make sql

COPY entrypoint.sh /usr/local/bin/entrypoint.sh
COPY render_conf.lua /usr/local/bin/render_conf.lua
# Fallback: o compose monta config.lua por cima, mas a imagem carrega um padrão
# para que `docker run` sozinho também funcione.
COPY config.lua /opt/cronus/config.lua
RUN dos2unix -q /usr/local/bin/entrypoint.sh /usr/local/bin/render_conf.lua \
    && chmod +x /usr/local/bin/entrypoint.sh

# login / char / map
EXPOSE 6900 6121 5121

ENTRYPOINT ["/usr/local/bin/entrypoint.sh"]
