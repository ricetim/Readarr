# ── Stage 1: build frontend ───────────────────────────────────────────────────
FROM node:20-slim AS frontend-builder
WORKDIR /src

COPY package.json yarn.lock tsconfig.json ./
RUN yarn install --frozen-lockfile --network-timeout 120000

COPY frontend/ ./frontend/
RUN yarn run build --env production

# ── Stage 2: build backend ────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:6.0 AS backend-builder
WORKDIR /src

COPY src/ ./src/
COPY Logo/ ./Logo/
RUN dotnet msbuild src/Readarr.sln \
      -restore \
      -p:Configuration=Release \
      -p:Platform=Posix \
      -p:RuntimeIdentifiers=linux-musl-x64 \
      -p:EnableAnalyzers=false \
      -p:TreatWarningsAsErrors=false \
      -t:PublishAllRids

# ── Stage 3: runtime (Alpine, matching faustvii/readarr image style) ──────────
FROM alpine:3.22 AS runtime

RUN apk add --no-cache \
      bash \
      ca-certificates \
      catatonit \
      coreutils \
      icu-libs \
      libintl \
      nano \
      sqlite-libs \
      tzdata \
    && mkdir -p /app/bin \
    && chown -R root:root /app \
    && chmod -R 755 /app

WORKDIR /app

ARG VERSION=dev
ARG VENDOR=ricetim
ARG PackageOwner=ricetim
ARG PackageRepo=readarr
ARG BRANCH=feature/mam-indexer

ENV COMPlus_EnableDiagnostics=0
ENV READARR__UPDATE__BRANCH=${BRANCH}

# Copy published backend
COPY --from=backend-builder /src/_output/net6.0/linux-musl-x64/publish/ /app/bin/
# Copy built frontend
COPY --from=frontend-builder /src/_output/UI/ /app/bin/UI/

# Write package_info (matches faustvii convention)
RUN printf "UpdateMethod=docker\nBranch=%s\nPackageVersion=%s\nPackageAuthor=[%s](https://github.com/%s)\nPackageOwner=%s\nPackageRepo=%s\n" \
      "${BRANCH}" "${VERSION}" "${VENDOR}" "${VENDOR}" "${PackageOwner}" "${PackageRepo}" \
    > /app/bin/package_info \
    && rm -rf /app/bin/Readarr.Update \
    && rm -f /app/bin/Readarr.Windows.*

COPY docker/entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

WORKDIR /config
VOLUME ["/config"]

ENTRYPOINT ["/usr/bin/catatonit", "--", "/entrypoint.sh"]
