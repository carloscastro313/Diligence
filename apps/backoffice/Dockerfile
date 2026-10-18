# Contexto de build: la raíz del repo (docker build -f apps/backoffice/Dockerfile .)
FROM node:24-alpine AS build
WORKDIR /src
COPY package.json package-lock.json tsconfig.base.json ./
COPY apps/backoffice/package.json apps/backoffice/
COPY packages/ui/package.json packages/ui/
RUN npm ci --workspace @diligencia/backoffice --include-workspace-root
COPY packages/ui packages/ui
COPY apps/backoffice apps/backoffice
RUN npm run build --workspace @diligencia/backoffice

FROM nginxinc/nginx-unprivileged:alpine
COPY docker/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/apps/backoffice/dist /usr/share/nginx/html
EXPOSE 8080
