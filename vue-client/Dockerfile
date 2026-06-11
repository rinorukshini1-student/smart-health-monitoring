FROM node:20-alpine AS build
WORKDIR /src
COPY vue-client/package*.json vue-client/
RUN npm ci --prefix vue-client
COPY vue-client/ vue-client/
WORKDIR /src/vue-client
ENV VITE_API_BASE_URL=
RUN npm run build:docker

FROM nginx:1.27-alpine
RUN apk add --no-cache tzdata
ENV TZ=Europe/Belgrade
COPY vue-client/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /src/vue-client/dist /usr/share/nginx/html
EXPOSE 80
