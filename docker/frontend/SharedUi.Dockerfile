FROM nginx:alpine
WORKDIR /usr/share/nginx/html
COPY shared/frontend/index.html.template ./index.html.template
COPY shared/frontend/assets ./assets
COPY shared/frontend/css ./css
COPY shared/frontend/js ./js
COPY shared/frontend/portal-nginx.conf.template /etc/nginx/templates/default.conf.template

# Default values match the local docker-compose port mapping, so no
# environment overrides are needed for local use; a cloud deployment
# overrides these to each frontend's real Container App URL.
ENV ACADEMICRECORDS_WEB_URL=http://localhost:8081
ENV COURSEMANAGEMENT_WEB_URL=http://localhost:8082
ENV CAMPUSHOUSING_WEB_URL=http://localhost:8083
ENV FINAID_WEB_URL=http://localhost:8084
ENV EVENTSCHEDULING_WEB_URL=http://localhost:8085

COPY shared/frontend/docker-entrypoint.d/40-render-portal-links.sh /docker-entrypoint.d/40-render-portal-links.sh
RUN chmod +x /docker-entrypoint.d/40-render-portal-links.sh

EXPOSE 80
