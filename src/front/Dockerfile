# Build context is the repository root.
FROM node:24-alpine AS build
RUN corepack enable && corepack prepare pnpm@11.5.1 --activate
WORKDIR /app

COPY src/front/package.json src/front/pnpm-lock.yaml src/front/pnpm-workspace.yaml ./
RUN pnpm install --frozen-lockfile

COPY src/front/ ./
RUN pnpm build

FROM nginx:alpine AS runtime
COPY src/front/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 80
