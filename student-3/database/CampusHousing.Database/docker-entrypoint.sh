#!/bin/sh
set -e

mkdir -p /app/data
chown -R "$APP_UID:$APP_UID" /app/data
exec setpriv --reuid="$APP_UID" --regid="$APP_UID" --clear-groups /bin/sh -c 'exec dotnet /app/CampusHousing.Database.dll'
