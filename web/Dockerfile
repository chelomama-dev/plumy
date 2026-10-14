# Stage 1: build the Angular app with Node + pnpm
FROM node:24-alpine AS build
WORKDIR /app

# pnpm version comes from the packageManager field in package.json
RUN corepack enable

# Dependencies first, so this layer is reused while the lockfile does not change
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY . .
RUN pnpm build

# Stage 2: serve the static files with nginx (non-root, listens on 8080). Node is not in the final image.
FROM nginxinc/nginx-unprivileged:1.29-alpine

# Files in /etc/nginx/templates go through envsubst at startup (API_URL -> real value)
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/web/browser /usr/share/nginx/html

EXPOSE 8080
