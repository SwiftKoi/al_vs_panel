FROM node:22-alpine AS frontend-build

WORKDIR /src/frontend
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

FROM caddy:2-alpine

COPY --from=frontend-build /src/frontend/dist /srv
COPY gateway/Caddyfile /etc/caddy/Caddyfile

EXPOSE 80 443

