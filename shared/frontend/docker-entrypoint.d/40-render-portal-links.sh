#!/bin/sh
set -eu

# Renders index.html.template into index.html, substituting only the five
# service-link variables below (an explicit list, so nothing else in the
# file, e.g. unrelated '$' characters, is touched). Runs automatically on
# container start via nginx's docker-entrypoint.d convention, the same
# mechanism that already renders the *.conf.template files for the other
# frontends.
envsubst '${ACADEMICRECORDS_WEB_URL} ${COURSEMANAGEMENT_WEB_URL} ${CAMPUSHOUSING_WEB_URL} ${FINAID_WEB_URL} ${EVENTSCHEDULING_WEB_URL}' \
  < /usr/share/nginx/html/index.html.template \
  > /usr/share/nginx/html/index.html
