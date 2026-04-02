package com.example.app

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.Application
import android.content.Context
import android.os.Build
import com.example.app.api.ApiClient
import com.example.app.api.RegisterPushTokenRequest
import com.google.firebase.messaging.FirebaseMessaging
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.util.UUID

class App : Application() {
    override fun onCreate() {
        super.onCreate()
        ensureNotificationChannels()
        registerFcmTokenOnStartup()
    }

    private fun ensureNotificationChannels() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager

        val updates = NotificationChannel(
            "updates",
            "Обновления",
            NotificationManager.IMPORTANCE_HIGH
        ).apply {
            description = "Уведомления об обновлениях и важных событиях"
        }

        nm.createNotificationChannel(updates)
    }

    private fun registerFcmTokenOnStartup() {
        val prefs = getSharedPreferences("auth", Context.MODE_PRIVATE)
        val deviceId = prefs.getString("device_id", null)
            ?: UUID.randomUUID().toString().also { newId ->
                prefs.edit().putString("device_id", newId).apply()
            }
        val deviceName = "${Build.MANUFACTURER} ${Build.MODEL}".trim()

        FirebaseMessaging.getInstance().token.addOnSuccessListener { token ->
            val login = prefs.getString("login", "")?.trim().orEmpty().ifBlank { null }
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
}

