package com.example.app

import android.app.NotificationManager
import android.content.Context
import android.os.Build
import androidx.core.app.NotificationCompat
import com.example.app.api.ApiClient
import com.example.app.api.RegisterPushTokenRequest
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.util.UUID

class MyFirebaseMessagingService : FirebaseMessagingService() {

    override fun onNewToken(token: String) {
        super.onNewToken(token)
        registerTokenAsync(token)
    }

    override fun onMessageReceived(message: RemoteMessage) {
        super.onMessageReceived(message)
        val title = message.notification?.title
            ?: message.data["title"]
            ?: "Обновление"
        val body = message.notification?.body
            ?: message.data["body"]
            ?: message.data["message"]
            ?: "Доступна новая информация"

        showSystemNotification(title, body)
    }

    private fun showSystemNotification(title: String, body: String) {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channelId = "updates"

        val notification = NotificationCompat.Builder(this, channelId)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(title)
            .setContentText(body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()

        nm.notify((System.currentTimeMillis() % Int.MAX_VALUE).toInt(), notification)
    }

    private fun registerTokenAsync(token: String) {
        val prefs = getSharedPreferences("auth", Context.MODE_PRIVATE)
        val login = prefs.getString("login", "")?.trim().orEmpty().ifBlank { null }

        val deviceId = prefs.getString("device_id", null)
            ?: UUID.randomUUID().toString().also { newId ->
                prefs.edit().putString("device_id", newId).apply()
            }
        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()

        CoroutineScope(Dispatchers.IO).launch {
            runCatching {
                ApiClient.pushTokenApi.register(
                    RegisterPushTokenRequest(
                        login = login,
                        token = token,
                        deviceId = deviceId,
                        deviceName = deviceName
                    )
                )
            }
        }
    }
}

