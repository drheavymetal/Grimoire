# Build context is the repository root.
FROM node:24-alpine AS build
RUN corepack enable && corepack prepare pnpm@11.5.1 --activate
WORKDIR /app

COPY src/front/package.json src/front/pnpm-lock.yaml src/front/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY src/front/ ./
# The API base URL is baked at build time (Vite reads VITE_* from the environment). It is an
# ORIGIN, never a path: the client prefixes /api on every request itself. Production passes
# --build-arg VITE_API_URL=https://<host> — appending /api here yields /api/api/... 404s.
ARG VITE_API_URL=http://localhost:5080
ENV VITE_API_URL=$VITE_API_URL
RUN pnpm build

FROM nginx:alpine AS runtime
COPY src/front/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
