# Локальный Jenkins для проверки Jenkinsfile. НЕ для продакшена (работает от root, admin/admin).
FROM jenkins/jenkins:lts-jdk21

USER root

# Docker CLI: Jenkins управляет Docker Desktop через примонтированный docker.sock
COPY --from=docker:27-cli /usr/local/bin/docker /usr/local/bin/docker

# Репозиторий примонтирован с Windows — git иначе ругается на «dubious ownership»
RUN git config --system --add safe.directory '*'

# Плагины, которые использует Jenkinsfile, + JCasC и Job DSL для автонастройки
RUN jenkins-plugin-cli --plugins \
      workflow-aggregator \
      pipeline-stage-view \
      git \
      docker-workflow \
      junit \
      credentials-binding \
      ws-cleanup \
      timestamper \
      configuration-as-code \
      job-dsl
