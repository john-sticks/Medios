#!/usr/bin/env bash
set -Eeuo pipefail

readonly APP_DIR=/opt/medios
readonly SOURCE_DIR="$APP_DIR/source"
readonly ENV_FILE="$APP_DIR/.env"
readonly IMAGE_REPOSITORY=johnsticks/medios

cd "$SOURCE_DIR"

if [[ -n "$(git status --porcelain)" ]]; then
    echo "ERROR: el repositorio productivo contiene cambios locales." >&2
    exit 1
fi

git fetch origin main
git merge --ff-only origin/main

revision="$(git rev-parse --short=12 HEAD)"
new_image="$IMAGE_REPOSITORY:$revision"
previous_image="$(sed -n 's/^MEDIOS_IMAGE=//p' "$ENV_FILE" | tail -n 1)"

echo "Construyendo $new_image"
docker build --tag "$new_image" --tag "$IMAGE_REPOSITORY:latest" .

replace_image() {
    local image="$1"
    local temporary
    temporary="$(mktemp "$APP_DIR/.env.XXXXXX")"
    awk -v image="$image" '
        /^MEDIOS_IMAGE=/ { print "MEDIOS_IMAGE=" image; next }
        { print }
    ' "$ENV_FILE" > "$temporary"
    chmod 600 "$temporary"
    mv "$temporary" "$ENV_FILE"
}

rollback() {
    echo "ERROR: el despliegue no supero el health check; restaurando $previous_image" >&2
    replace_image "$previous_image"
    docker compose --env-file "$ENV_FILE" up -d --no-build
}

replace_image "$new_image"
docker compose --env-file "$ENV_FILE" config --quiet
docker compose --env-file "$ENV_FILE" up -d --no-build

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

healthy=false
for attempt in {1..12}; do
    if curl --fail --silent --show-error \
        --max-time 5 \
        "http://${MEDIOS_BIND_ADDRESS}:${MEDIOS_PORT}/" >/dev/null; then
        healthy=true
        break
    fi
    echo "Esperando a Medios ($attempt/12)..."
    sleep 5
done

if [[ "$healthy" != true ]]; then
    rollback
    exit 1
fi

echo "Despliegue correcto: $new_image"
