# syntax=docker/dockerfile:1.7
# Contexto esperado: ./frontend/
# Uso: docker build -t jade-frontend -f frontend/Dockerfile frontend/

# ---------- build ----------
FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm install --no-audit --no-fund
COPY . .
RUN npm run build -- --configuration production

# ---------- runtime ----------
FROM nginx:1.27-alpine
# Configuracion custom: reverse proxy hacia la API en :8080
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/jade-capital/browser /usr/share/nginx/html
EXPOSE 80
HEALTHCHECK --interval=15s --timeout=3s --retries=5 CMD wget -q --spider http://localhost/ || exit 1
CMD ["nginx", "-g", "daemon off;"]
