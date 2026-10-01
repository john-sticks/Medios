#!/usr/bin/env bash
set -Eeuo pipefail

readonly APP_DIR=/opt/medios
readonly SOURCE_DIR="$APP_DIR/source"
readonly ENV_FILE="$APP_DIR/.env"
readonly IMAGE=johnsticks/medios:latest
readonly ROLLBACK_IMAGE=johnsticks/medios:rollback
readonly FAILED_IMAGE_FILE="$APP_DIR/.failed-image-id"

cd "$SOURCE_DIR"

git fetch origin main --quiet
git merge --ff-only origin/main --quiet

current_container_image="$(docker inspect medios --format '{{.Image}}' 2>/dev/null || true)"
docker pull "$IMAGE" >/dev/null
candidate_image="$(docker image inspect "$IMAGE" --format '{{.Id}}')"

if [[ -f "$FAILED_IMAGE_FILE" ]] && [[ "$(<"$FAILED_IMAGE_FILE")" == "$candidate_image" ]]; then
    echo "La imagen $candidate_image ya fallo el health check; se espera una version nueva."
    exit 0
fi

if [[ -n "$current_container_image" ]] && [[ "$current_container_image" == "$candidate_image" ]]; then
    echo "Medios ya ejecuta $candidate_image."
    exit 0
fi

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

if [[ -n "$current_container_image" ]]; then
    docker tag "$current_container_image" "$ROLLBACK_IMAGE"
fi

replace_image "$IMAGE"
docker compose --env-file "$ENV_FILE" config --quiet
docker compose --env-file "$ENV_FILE" up -d --no-build >/dev/null

set -a
# shellcheck disable=SC1090
source "$ENV_FILE"
set +a

for attempt in {1..12}; do
    if curl --fail --silent --show-error --max-time 5 \
        "http://${MEDIOS_BIND_ADDRESS}:${MEDIOS_PORT}/" >/dev/null; then
        rm -f "$FAILED_IMAGE_FILE"
        echo "Despliegue correcto: $candidate_image"
        exit 0
    fi
    sleep 5
done

printf '%s' "$candidate_image" > "$FAILED_IMAGE_FILE"

if [[ -n "$current_container_image" ]]; then
    echo "Health check fallido; restaurando $current_container_image" >&2
    replace_image "$ROLLBACK_IMAGE"
    docker compose --env-file "$ENV_FILE" up -d --no-build >/dev/null
fi

exit 1
