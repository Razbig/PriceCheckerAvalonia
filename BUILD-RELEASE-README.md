Build / Release scripts

Добавлены два скрипта для создания артефактов релиза и генерации JSON-метаданных:

- build-release.ps1 — PowerShell-скрипт для Windows / PowerShell Core
- build-release.sh — Bash-скрипт для Linux / macOS

Пример использования (PowerShell):
- Откройте PowerShell в корне репозитория
- Запустите: .\build-release.ps1 -Version "1.2.3" -OutputDir "artifacts" -Notes "Release notes"

Пример использования (bash):
- ./build-release.sh 1.2.3 "Release notes"

Что делают скрипты:
- dotnet publish для указанных RID'ов (по умолчанию win-x64 и linux-x64)
- Упаковывают публикацию в zip (Windows) или tar.gz (Linux)
- Вычисляют SHA256 и генерируют файл metadata-{version}-{rid}.json с полями: version, url, sha256, publishedAt, notes, minClientVersion
- Если установлен gpg — создают detached ASCII подпись *.sig

Важно:
- В JSON по умолчанию ставится url https://updates.example.com/{artifact}. Замените на ваш CDN/сервер при публикации.
- Скрипты не загружают файлы на сервер — добавьте шаг upload (scp, aws s3 cp, az storage blob upload и т.д.) в pipeline.
