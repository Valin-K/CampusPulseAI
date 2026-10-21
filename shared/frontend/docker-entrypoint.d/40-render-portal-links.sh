#!/bin/sh
set -eu

: "${PORTAL_WEB_URL:=http://localhost:8080/}"
: "${ACADEMICRECORDS_WEB_URL:=http://localhost:8081/}"
: "${COURSEMANAGEMENT_WEB_URL:=http://localhost:8082/}"
: "${CAMPUSHOUSING_WEB_URL:=http://localhost:8083/}"
: "${FINAID_WEB_URL:=http://localhost:8084/}"
: "${EVENTSCHEDULING_WEB_URL:=http://localhost:8085/}"
export PORTAL_WEB_URL ACADEMICRECORDS_WEB_URL COURSEMANAGEMENT_WEB_URL
export CAMPUSHOUSING_WEB_URL FINAID_WEB_URL EVENTSCHEDULING_WEB_URL

envsubst '${PORTAL_WEB_URL} ${ACADEMICRECORDS_WEB_URL} ${COURSEMANAGEMENT_WEB_URL} ${CAMPUSHOUSING_WEB_URL} ${FINAID_WEB_URL} ${EVENTSCHEDULING_WEB_URL}' \
  < /usr/share/nginx/html/campus-config.json.template \
  > /usr/share/nginx/html/campus-config.json

# Only the directory portal has an HTML template; Blazor serves its published index.
if [ -f /usr/share/nginx/html/index.html.template ]; then
  envsubst '${ACADEMICRECORDS_WEB_URL} ${COURSEMANAGEMENT_WEB_URL} ${CAMPUSHOUSING_WEB_URL} ${FINAID_WEB_URL} ${EVENTSCHEDULING_WEB_URL}' \
	< /usr/share/nginx/html/index.html.template \
	> /usr/share/nginx/html/index.html
fi
