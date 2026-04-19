# Steklo Portal Web (MVP)

Web-клиент для ПК и iPhone (Safari), использует существующий backend API.

## Возможности MVP

- Авторизация (`/api/employee/login`) + подтверждение кода нового устройства.
- Список чатов с polling.
- Диалог: отправка текста, фото, видео, APK.
- Bot actions: `relogin_account`, `open_apk`.
- Профиль + выход из аккаунта.

## Запуск локально

1. Скопируйте env:
   - `cp .env.example .env` (или создайте `.env` вручную на Windows)
2. Укажите backend URL:
   - `VITE_API_BASE_URL=http://localhost:5000/`
3. Установите зависимости:
   - `npm install`
4. Запустите dev-сервер:
   - `npm run dev`

## Production build

- `npm run build`
- Артефакты будут в `dist/`.
- Статику можно отдать через любой web-сервер (Nginx, IIS, static hosting).

## Проверка на iPhone Safari

- Откройте web URL с iPhone.
- Проверьте:
  - логин и подтверждение device code;
  - чат polling;
  - отправку фото/видео/APK;
  - воспроизведение видео (`playsInline + controls`);
  - действие кнопки `Скачать APK`.
