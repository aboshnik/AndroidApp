

 1) Android-клиент

Файл: `app/build.gradle`

Это главный файл адресации Android-приложения к backend.

Нужно проверить/заменить:

- `API_BASE_URL` -> адрес production API
- `ADMIN_API_KEY` -> production ключ (не dev)

Пример:

```gradle
buildConfigField "String", "API_BASE_URL", "\"ip:port/\""
buildConfigField "String", "ADMIN_API_KEY", "\"YOUR_PROD_ADMIN_KEY\""
```

> Важно: URL должен заканчиваться `/`.



---

## 2) Backend (.NET API + SQL)

Файл: `backend/appsettings.json`

Ключевой production-файл для:

- подключения к БД,
- admin-ключа,
- ключа шифрования чатов.

Поля, которые обязательно заменить:

- `ConnectionStrings.DefaultConnection`
- `Admin.Key`
- `Chat.MessageEncryptionKey`

Пример:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=sql.company.ru,1433;Database=prod_db;User Id=prod_user;Password=STRONG_PASS;Encrypt=True;TrustServerCertificate=False;"
  },
  "Admin": {
    "Key": "STRONG_RANDOM_ADMIN_KEY"
  },
  "Chat": {
    "MessageEncryptionKey": "BASE64_32_BYTE_KEY"
  }
}
```

 Требования к `Chat.MessageEncryptionKey`

- Base64-строка,
- после декодирования **ровно 32 байта** (AES-256-GCM),
- пустое значение отключает шифрование сообщений (не рекомендуется для prod).



## 3) Web (Desktop + iPhone web)








```env
VITE_API_BASE_URL=VITE_API_BASE_URL=http://192.168.0.106:5000/




 Backend

```bash
cd backend
dotnet publish -c Release -o ./publish
```

 Web

```bash
cd web
npm ci
npm run build
```

