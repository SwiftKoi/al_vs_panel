# Minimal read-only socket inspector used by server-connections.sh.
# It joins the game server's network namespace only; it never runs inside
# the game server container and cannot affect its processes.
FROM alpine:3.22
RUN apk add --no-cache iproute2-ss
USER 65534:65534
