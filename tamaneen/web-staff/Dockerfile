# Staff portal for a local trial. Build context: tamaneen/web-staff.
# Uses the development environment file: the browser calls the API on localhost:5080 and the crisis worker
# on localhost:5090 directly, exactly as in local development.
FROM node:20-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npx ng build --configuration production,local

FROM nginx:1.27-alpine
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /app/dist/ /tmp/dist/
RUN set -e; src=$(find /tmp/dist -name index.html -exec dirname {} \; | head -1); cp -r "$src"/. /usr/share/nginx/html/; rm -rf /tmp/dist
EXPOSE 80
