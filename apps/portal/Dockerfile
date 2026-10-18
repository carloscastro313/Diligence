# Contexto de build: la raíz del repo (docker build -f apps/portal/Dockerfile .)
FROM node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json tsconfig.base.json ./
COPY apps/portal/package.json apps/portal/
COPY packages/ui/package.json packages/ui/
RUN npm ci --workspace @diligencia/portal --include-workspace-root
COPY packages/ui packages/ui
COPY apps/portal apps/portal
RUN npm run build --workspace @diligencia/portal

FROM nginxinc/nginx-unprivileged:alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/apps/portal/dist /usr/share/nginx/html
EXPOSE 8080
