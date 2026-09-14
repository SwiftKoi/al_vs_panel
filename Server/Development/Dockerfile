FROM mcr.microsoft.com/dotnet/runtime:10.0

ARG SERVER_UID
ARG SERVER_GID

RUN apt-get update \
    && apt-get install --no-install-recommends --yes \
        ca-certificates \
        curl \
        unzip \
    && rm -rf /var/lib/apt/lists/*

RUN if ! getent group "${SERVER_GID}" >/dev/null; then \
        groupadd --gid "${SERVER_GID}" vintagestory; \
    fi \
    && if ! getent passwd "${SERVER_UID}" >/dev/null; then \
        useradd --uid "${SERVER_UID}" --gid "${SERVER_GID}" \
            --no-create-home --home-dir /nonexistent --shell /usr/sbin/nologin \
            vintagestory; \
    fi \
    && install -d -o "${SERVER_UID}" -g "${SERVER_GID}" -m 0750 \
        /run/vintagestory

COPY --chown=0:0 docker/vintagestory-entrypoint.sh /usr/local/bin/vintagestory-entrypoint
COPY --chown=0:0 docker/vintagestory-command.sh /usr/local/bin/vintagestory-command

RUN chmod 0755 \
    /usr/local/bin/vintagestory-entrypoint \
    /usr/local/bin/vintagestory-command

USER ${SERVER_UID}:${SERVER_GID}
WORKDIR /server

EXPOSE 9020/tcp
STOPSIGNAL SIGINT

ENTRYPOINT ["/usr/local/bin/vintagestory-entrypoint"]
