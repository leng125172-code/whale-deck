ARG NODE_IMAGE=node:24.15.0-alpine
ARG NGINX_IMAGE=nginx:1.29-alpine

FROM ${NODE_IMAGE} AS web-build
WORKDIR /src
RUN corepack enable
COPY web/package.json web/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY web/ .
RUN pnpm build

FROM ${NGINX_IMAGE} AS final
ARG WHALEDECK_BUILD_REVISION
RUN test "${#WHALEDECK_BUILD_REVISION}" -eq 40 && ! printf '%s' "$WHALEDECK_BUILD_REVISION" | grep -q '[^a-f0-9]' \
    && printf '{"component":"WhaleDeck.Gateway","version":"0.1.0","sourceRevision":"%s"}\n' "$WHALEDECK_BUILD_REVISION" > /usr/share/nginx/build-info.json
LABEL org.opencontainers.image.revision="$WHALEDECK_BUILD_REVISION" \
      org.opencontainers.image.source="https://github.com/leng125172-code/whale-deck" \
      org.opencontainers.image.version="0.1.0"
COPY deploy/gateway/nginx.conf /etc/nginx/nginx.conf
COPY deploy/gateway/runtime-config.template.js /usr/share/nginx/runtime-config.template.js
COPY --chmod=755 deploy/gateway/start.sh /usr/local/bin/whaledeck-gateway-start
COPY --from=web-build /src/dist /usr/share/nginx/html
USER 101:101
EXPOSE 8080 8081
ENTRYPOINT ["/usr/local/bin/whaledeck-gateway-start"]
