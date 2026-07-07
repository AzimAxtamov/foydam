#!/bin/sh
# Бэкап Postgres раз в сутки с ротацией. Запускается в контейнере backup
# (образ postgres:16 — ради pg_dump). Снапшоты — наш главный актив.
set -eu

: "${POSTGRES_HOST:=postgres}"
: "${POSTGRES_DB:=sellertools}"
: "${POSTGRES_USER:=sellertools}"
: "${BACKUP_DIR:=/backups}"
: "${BACKUP_KEEP_DAYS:=14}"
: "${BACKUP_INTERVAL_SECONDS:=86400}"   # раз в сутки
: "${BACKUP_ONESHOT:=0}"                # 1 → один бэкап и выход (для деплоя)

mkdir -p "$BACKUP_DIR"
echo "[backup] старт: host=$POSTGRES_HOST db=$POSTGRES_DB dir=$BACKUP_DIR keep=${BACKUP_KEEP_DAYS}д interval=${BACKUP_INTERVAL_SECONDS}с"

do_backup() {
  ts=$(date +%Y-%m-%d_%H%M%S)
  file="$BACKUP_DIR/sellertools-$ts.sql.gz"
  echo "[backup] $(date -Iseconds) → $file"
  # PGPASSWORD берётся из env. -Fp | gzip: обычный SQL, сжатый.
  if pg_dump -h "$POSTGRES_HOST" -U "$POSTGRES_USER" "$POSTGRES_DB" | gzip > "$file.tmp"; then
    mv "$file.tmp" "$file"
    echo "[backup] ок: $(du -h "$file" | cut -f1)"
  else
    echo "[backup] ОШИБКА pg_dump" >&2
    rm -f "$file.tmp"
  fi
  # Ротация: удаляем дампы старше N дней.
  find "$BACKUP_DIR" -name 'sellertools-*.sql.gz' -mtime "+$BACKUP_KEEP_DAYS" -print -delete || true
}

# Первый бэкап — сразу после старта (чтобы не ждать сутки на свежем деплое).
do_backup

# Oneshot: один дамп и выход (используется деплоем перед обновлением).
if [ "$BACKUP_ONESHOT" = "1" ]; then
  echo "[backup] oneshot — выходим"
  exit 0
fi

while true; do
  sleep "$BACKUP_INTERVAL_SECONDS"
  do_backup
done
