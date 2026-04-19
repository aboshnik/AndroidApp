package com.example.app

import android.app.PendingIntent
import android.app.NotificationManager
import android.content.Context
import android.content.Intent
import android.os.Build
import androidx.core.app.NotificationManagerCompat
import androidx.core.app.RemoteInput
import androidx.core.app.NotificationCompat
import com.example.app.api.ApiClient
import com.example.app.api.RegisterPushTokenRequest
import com.example.app.chats.ChatActivity
import com.google.firebase.messaging.FirebaseMessagingService
import com.google.firebase.messaging.RemoteMessage
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch
import java.util.UUID

class MyFirebaseMessagingService : FirebaseMessagingService() {
    companion object {
        private const val PREFS_NOTIF = "push_notifications"
        private const val KEY_GENERAL_IDS = "general_ids"

        fun clearTrackedGeneralNotifications(context: Context) {
            val prefs = context.getSharedPreferences(PREFS_NOTIF, Context.MODE_PRIVATE)
            val idsRaw = prefs.getString(KEY_GENERAL_IDS, "").orEmpty()
            if (idsRaw.isNotBlank()) {
                val nm = NotificationManagerCompat.from(context)
                idsRaw.split(",")
                    .mapNotNull { it.trim().toIntOrNull() }
                    .forEach { id -> nm.cancel(id) }
            }
            prefs.edit().remove(KEY_GENERAL_IDS).apply()
        }

        fun clearChatNotification(context: Context, threadId: Int) {
            if (threadId <= 0) return
            NotificationManagerCompat.from(context).cancel(threadId)
        }
    }

    override fun onNewToken(token: String) {
        super.onNewToken(token)
        registerTokenAsync(token)
    }

    override fun onMessageReceived(message: RemoteMessage) {
        super.onMessageReceived(message)
        val typeEarly = message.data["type"]?.trim().orEmpty()
        if (typeEarly.equals("chat", ignoreCase = true)) {
            val threadId = message.data["threadId"]?.toIntOrNull() ?: 0
            if (threadId > 0) {
                sendBroadcast(
                    Intent(ChatEvents.ACTION_REFRESH_THREAD_LIST).setPackage(packageName)
                )
            }
        }
        if (!UserSettings.areNotificationsEnabled(this)) return
        val title = message.notification?.title
            ?: message.data["title"]
            ?: "Обновление"
        val body = message.notification?.body
            ?: message.data["body"]
            ?: message.data["message"]
            ?: "Доступна новая информация"
        val type = message.data["type"]?.trim().orEmpty()
        if (type.equals("chat", ignoreCase = true)) {
            val threadId = message.data["threadId"]?.toIntOrNull() ?: 0
            val threadTitle = message.data["threadTitle"]?.trim().orEmpty().ifBlank { title }
            if (threadId > 0) {
                showChatNotification(threadId = threadId, threadTitle = threadTitle, body = body)
                return
            }
        }
        showSystemNotification(title, body)
    }

    private fun showSystemNotification(title: String, body: String) {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channelId = "updates"
        val notificationId = (System.currentTimeMillis() % Int.MAX_VALUE).toInt()

        val notification = NotificationCompat.Builder(this, channelId)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(title)
            .setContentText(body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .build()

        nm.notify(notificationId, notification)
        rememberGeneralNotificationId(notificationId)
    }

    private fun showChatNotification(threadId: Int, threadTitle: String, body: String) {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val channelId = "updates"
        val notificationId = threadId

        val openIntent = Intent(this, ChatActivity::class.java).apply {
            putExtra(ChatActivity.EXTRA_THREAD_ID, threadId)
            putExtra(ChatActivity.EXTRA_THREAD_TITLE, threadTitle)
            putExtra(ChatActivity.EXTRA_THREAD_TYPE, "user")
            putExtra(ChatActivity.EXTRA_THREAD_IS_TECH_ADMIN, false)
            putExtra(ChatActivity.EXTRA_THREAD_BOT_ID, "")
            putExtra(ChatActivity.EXTRA_THREAD_IS_OFFICIAL_BOT, false)
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
        }
        val openPi = PendingIntent.getActivity(
            this,
            notificationId,
            openIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val markReadIntent = Intent(this, ChatNotificationActionReceiver::class.java).apply {
            action = ChatNotificationActionReceiver.ACTION_MARK_READ
            putExtra(ChatNotificationActionReceiver.EXTRA_THREAD_ID, threadId)
            putExtra(ChatNotificationActionReceiver.EXTRA_NOTIFICATION_ID, notificationId)
        }
        val markReadPi = PendingIntent.getBroadcast(
            this,
            notificationId * 10 + 1,
            markReadIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        )

        val replyIntent = Intent(this, ChatNotificationActionReceiver::class.java).apply {
            action = ChatNotificationActionReceiver.ACTION_REPLY
            putExtra(ChatNotificationActionReceiver.EXTRA_THREAD_ID, threadId)
            putExtra(ChatNotificationActionReceiver.EXTRA_NOTIFICATION_ID, notificationId)
        }
        val replyPi = PendingIntent.getBroadcast(
            this,
            notificationId * 10 + 2,
            replyIntent,
            PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_MUTABLE
        )
        val remoteInput = RemoteInput.Builder(ChatNotificationActionReceiver.KEY_REPLY_TEXT)
            .setLabel("Ответить")
            .build()

        val notification = NotificationCompat.Builder(this, channelId)
            .setSmallIcon(R.mipmap.ic_launcher)
            .setContentTitle(threadTitle)
            .setContentText(body)
            .setStyle(NotificationCompat.BigTextStyle().bigText(body))
            .setContentIntent(openPi)
            .setAutoCancel(true)
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .addAction(
                NotificationCompat.Action.Builder(
                    android.R.drawable.ic_menu_send,
                    "Ответить",
                    replyPi
                ).addRemoteInput(remoteInput).build()
            )
            .addAction(
                NotificationCompat.Action.Builder(
                    android.R.drawable.ic_menu_view,
                    "Пометить прочитанным",
                    markReadPi
                ).build()
            )
            .build()

        nm.notify(notificationId, notification)
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

    private fun rememberGeneralNotificationId(id: Int) {
        if (id <= 0) return
        val prefs = getSharedPreferences(PREFS_NOTIF, Context.MODE_PRIVATE)
        val existing = prefs.getString(KEY_GENERAL_IDS, "").orEmpty()
            .split(",")
            .mapNotNull { it.trim().toIntOrNull() }
            .toMutableList()
        existing.add(id)
        val compact = existing.takeLast(30).joinToString(",")
        prefs.edit().putString(KEY_GENERAL_IDS, compact).apply()
    }
}

