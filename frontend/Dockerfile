FROM node:24-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
ARG VITE_API_URL=http://localhost:5000
ENV VITE_API_URL=$VITE_API_URL
RUN npm run build

# Unprivileged nginx: runs as a non-root user and listens on 8080.
# API_ORIGIN is substituted into the Content-Security-Policy at container start.
FROM nginxinc/nginx-unprivileged:alpine
ENV API_ORIGIN=http://localhost:5000
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist /usr/share/nginx/html
EXPOSE 8080
