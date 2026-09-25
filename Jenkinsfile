// Сборка, тестирование и контейнеризация CBR Rates Gateway.
// Требуемые плагины Jenkins: Pipeline, Docker Pipeline, JUnit, Credentials Binding, Workspace Cleanup.
// Агент с меткой 'docker' должен иметь установленный Docker.

pipeline {
    agent { label 'docker' }

    options {
        timestamps()
        timeout(time: 30, unit: 'MINUTES')
        disableConcurrentBuilds()
        buildDiscarder(logRotator(numToKeepStr: '20'))
    }

    parameters {
        booleanParam(name: 'PUSH_IMAGE', defaultValue: false,
                     description: 'Публиковать образ в registry (для main/master/release публикуется всегда)')
    }

    environment {
        // Адрес Docker registry. Можно переопределить глобальной переменной Jenkins DOCKER_REGISTRY
        // (локальный стенд из jenkins-local/ выставляет localhost:5000).
        REGISTRY             = "${env.DOCKER_REGISTRY ?: 'registry.company.local'}"
        REGISTRY_CREDENTIALS = 'docker-registry-credentials'     // id учётки в Jenkins Credentials
        IMAGE_NAME           = 'integration/cbr-rates-gateway'
        SOLUTION             = 'CbrRatesGateway.sln'
        DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        DOTNET_NOLOGO        = '1'
        // BuildKit нужен Dockerfile'у (RUN --mount=type=cache для кэша NuGet)
        DOCKER_BUILDKIT      = '1'
    }

    stages {
        stage('Checkout') {
            steps {
                checkout scm
                script {
                    def shortSha = sh(script: 'git rev-parse --short=8 HEAD', returnStdout: true).trim()
                    env.IMAGE_TAG = "${env.BUILD_NUMBER}-${shortSha}"
                    currentBuild.displayName = "#${env.BUILD_NUMBER} ${env.IMAGE_TAG}"
                }
            }
        }

        stage('Restore, Build & Test') {
            agent {
                docker {
                    image 'mcr.microsoft.com/dotnet/sdk:8.0'
                    reuseNode true
                    // HOME и кэш NuGet внутри workspace — контейнер запускается не от root.
                    // Папка .nuget не удаляется в cleanWs (см. post), поэтому restore в следующих сборках
                    // берёт пакеты из кэша, а не качает их заново.
                    args '-e HOME=/tmp -e NUGET_PACKAGES=$WORKSPACE/.nuget/packages'
                }
            }
            stages {
                stage('Restore') {
                    steps {
                        sh 'dotnet restore $SOLUTION'
                    }
                }
                stage('Build') {
                    steps {
                        sh 'dotnet build $SOLUTION -c Release --no-restore'
                    }
                }
                stage('Unit tests') {
                    steps {
                        sh '''
                            dotnet test $SOLUTION -c Release --no-build \
                              --logger "junit;LogFilePath=$WORKSPACE/TestResults/{assembly}.junit.xml" \
                              --collect:"XPlat Code Coverage" \
                              --results-directory $WORKSPACE/TestResults
                        '''
                    }
                    post {
                        always {
                            junit allowEmptyResults: false, testResults: 'TestResults/*.junit.xml'
                            archiveArtifacts artifacts: 'TestResults/**/coverage.cobertura.xml', allowEmptyArchive: true
                        }
                    }
                }
            }
        }

        stage('Docker build') {
            // Тесты уже прошли в стадии 'Unit tests', поэтому внутри образа их не повторяем (RUN_TESTS=false)
            // Сбои сети при скачивании базовых образов (TLS handshake timeout и т.п.) — повторяем до 3 раз
            options { retry(3) }
            steps {
                sh '''
                    docker build \
                      --pull \
                      --build-arg VERSION=$IMAGE_TAG \
                      --build-arg RUN_TESTS=false \
                      --label org.opencontainers.image.revision=$(git rev-parse HEAD) \
                      -t $REGISTRY/$IMAGE_NAME:$IMAGE_TAG \
                      .
                '''
            }
        }

        stage('Docker push') {
            when {
                anyOf {
                    branch 'main'
                    branch 'master'
                    branch pattern: 'release/.*', comparator: 'REGEXP'
                    expression { return params.PUSH_IMAGE }
                }
            }
            steps {
                withCredentials([usernamePassword(credentialsId: env.REGISTRY_CREDENTIALS,
                                                  usernameVariable: 'REGISTRY_USER',
                                                  passwordVariable: 'REGISTRY_PASSWORD')]) {
                    sh '''
                        echo "$REGISTRY_PASSWORD" | docker login $REGISTRY -u "$REGISTRY_USER" --password-stdin
                        docker push $REGISTRY/$IMAGE_NAME:$IMAGE_TAG
                    '''
                    script {
                        if (['main', 'master'].contains(env.BRANCH_NAME)) {
                            sh '''
                                docker tag  $REGISTRY/$IMAGE_NAME:$IMAGE_TAG $REGISTRY/$IMAGE_NAME:latest
                                docker push $REGISTRY/$IMAGE_NAME:latest
                            '''
                        }
                    }
                }
            }
        }
    }

    post {
        always {
            sh '''
                docker rmi $REGISTRY/$IMAGE_NAME:$IMAGE_TAG $REGISTRY/$IMAGE_NAME:latest 2>/dev/null || true
                docker logout $REGISTRY 2>/dev/null || true
            '''
            // Чистим workspace, но оставляем кэш NuGet — иначе каждый restore качает все пакеты заново
            cleanWs(deleteDirs: true, patterns: [
                [pattern: '.nuget', type: 'EXCLUDE'],
                [pattern: '.nuget/**', type: 'EXCLUDE']
            ])
        }
        success {
            echo "Образ собран: ${REGISTRY}/${IMAGE_NAME}:${IMAGE_TAG}"
        }
    }
}
