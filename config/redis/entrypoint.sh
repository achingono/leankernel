#!/bin/sh
set -eu

if [ -f /run/secrets/redis_password ]; then
  export REDIS_PASSWORD="$(cat /run/secrets/redis_password | tr -d '\r\n')"
  exec redis-server /usr/local/etc/redis/redis.conf --requirepass "$REDIS_PASSWORD"
fi

exec redis-server /usr/local/etc/redis/redis.conf
