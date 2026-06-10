#!/bin/bash
set -e

BACPAC_PATH="$(cd "$(dirname "$0")" && pwd)/Diplom/MyDatabase_backup.bacpac"
SA_PASSWORD="PlywoodStrong@2024"
DB_NAME="PlywoodFactoryDB"
SQLPACKAGE_DIR="$(cd "$(dirname "$0")" && pwd)/sqlpackage"

echo "==> Запуск контейнера с БД..."
docker compose up -d db

echo "==> Ожидание готовности SQL Server (до 60 секунд)..."
for i in $(seq 1 12); do
  if docker compose exec db /opt/mssql-tools18/bin/sqlcmd \
      -S localhost -U sa -P "$SA_PASSWORD" -C -Q "SELECT 1" > /dev/null 2>&1; then
    echo "    SQL Server готов."
    break
  fi
  echo "    Попытка $i/12..."
  sleep 5
done

echo "==> Пересоздание базы данных..."
docker compose exec db /opt/mssql-tools18/bin/sqlcmd \
  -S localhost -U sa -P "$SA_PASSWORD" -C -Q "
IF EXISTS (SELECT name FROM sys.databases WHERE name = '$DB_NAME')
BEGIN
  ALTER DATABASE [$DB_NAME] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
  DROP DATABASE [$DB_NAME];
END
CREATE DATABASE [$DB_NAME];
"

echo "==> Загрузка sqlpackage (если не установлен)..."
if [ ! -f "$SQLPACKAGE_DIR/sqlpackage" ]; then
  curl -L https://aka.ms/sqlpackage-linux -o /tmp/sqlpackage.zip
  unzip -o /tmp/sqlpackage.zip -d "$SQLPACKAGE_DIR"
  rm /tmp/sqlpackage.zip
fi
chmod +x "$SQLPACKAGE_DIR/sqlpackage"

echo "==> Импорт $BACPAC_PATH ..."
"$SQLPACKAGE_DIR/sqlpackage" \
  /Action:Import \
  /SourceFile:"$BACPAC_PATH" \
  /TargetServerName:"localhost,1433" \
  /TargetDatabaseName:"$DB_NAME" \
  /TargetUser:sa \
  /TargetPassword:"$SA_PASSWORD" \
  /TargetTrustServerCertificate:true

echo "==> Запуск веб-приложения..."
docker compose up -d web

echo ""
echo "Готово! Приложение доступно на порту 7777."
