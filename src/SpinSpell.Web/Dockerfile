FROM node:22-alpine AS build
WORKDIR /app

COPY package*.json ./
RUN npm ci

COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=build /app/dist /usr/share/nginx/html
# nginx proxies /api and /hub to the api service, so the browser is same-origin
# and there is no CORS in play at all.
COPY nginx.conf /etc/nginx/conf.d/default.conf

EXPOSE 5173
