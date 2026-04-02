package com.example.app

import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.content.Context
import android.content.Intent
import android.content.pm.PackageManager
import android.os.Build
import androidx.core.app.NotificationCompat
import androidx.core.app.NotificationManagerCompat
import androidx.work.Constraints
import androidx.work.CoroutineWorker
import androidx.work.ExistingPeriodicWorkPolicy
import androidx.work.ExistingWorkPolicy
import androidx.work.NetworkType
import androidx.work.OneTimeWorkRequestBuilder
import androidx.work.PeriodicWorkRequestBuilder
import androidx.work.WorkManager
import androidx.work.WorkerParameters
import com.example.app.api.ApiClient
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext
import java.util.concurrent.TimeUnit

class NotificationsWorker(
    ctx: Context,
    params: WorkerParameters
) : CoroutineWorker(ctx, params) {

    override suspend fun doWork(): Result {
        val prefs = applicationContext.getSharedPreferences("auth", Context.MODE_PRIVATE)
        val login = prefs.getString("login", null)?.trim().orEmpty()
        if (login.isBlank()) return Result.success()

        return try {
            val response = withContext(Dispatchers.IO) {
                ApiClient.notificationsApi.getNotifications(login = login, take = 20)
            }
            val body = response.body()
            if (!response.isSuccessful || body == null || !body.success) return Result.retry()

            val unread = body.unreadCount
            val latestUnread = body.items
                ?.firstOrNull { !it.isRead && NotificationSettingsManager.isAllowed(applicationContext, it.type) }
            val latestId = latestUnread?.id ?: 0

            val lastNotifiedId = prefs.getInt("last_notified_notification_id", 0)
            if (unread > 0 && latestId > lastNotifiedId) {
                showSystemNotification(
                    unreadCount = unread,
                    title = latestUnread?.title,
                    type = latestUnread?.type,
                    body = latestUnread?.body
                )
                prefs.edit().putInt("last_notified_notification_id", latestId).apply()
            }
            Result.success()
        } catch (_: Exception) {
            Result.retry()
        }
    }

    private fun showSystemNotification(unreadCount: Int, title: String?, type: String?, body: String?) {
        if (Build.VERSION.SDK_INT >= 33) {
            val granted = applicationContext.checkSelfPermission(android.Manifest.permission.POST_NOTIFICATIONS) == PackageManager.PERMISSION_GRANTED
            if (!granted) return
        }

        ensureChannel()

        val intent = Intent(applicationContext, NotificationsActivity::class.java).apply {
            flags = Intent.FLAG_ACTIVITY_NEW_TASK or Intent.FLAG_ACTIVITY_CLEAR_TOP
            putExtra("login", applicationContext.getSharedPreferences("auth", Context.MODE_PRIVATE).getString("login", "") ?: "")
        }

        val piFlags = if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.M) PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE else PendingIntent.FLAG_UPDATE_CURRENT
        val pending = PendingIntent.getActivity(applicationContext, 1001, intent, piFlags)

        val header = when (type?.trim()?.lowercase()) {
            "post" -> "Новая новость"
            "security" -> "Безопасность"
            "test" -> "Уведомление"
            "update" -> "Обновление"
            else -> "Уведомление"
        }

        val contentTitle = title?.trim().takeIf { !it.isNullOrBlank() } ?: header

        val contentText = if (unreadCount <= 1) {
            body?.trim().takeIf { !it.isNullOrBlank() } ?: contentTitle
        } else {
            val short = body?.trim().takeIf { !it.isNullOrBlank() } ?: contentTitle
            "Новых: $unreadCount • $short"
        }

        val n = NotificationCompat.Builder(applicationContext, CHANNEL_ID)
            .setSmallIcon(R.drawable.ic_feed_bell)
            .setContentTitle("СалаватСтекло")
            .setSubText(header)
            .setContentText(contentText)
            .setAutoCancel(true)
            .setContentIntent(pending)
            .setPriority(NotificationCompat.PRIORITY_DEFAULT)
            .build()

        NotificationManagerCompat.from(applicationContext).notify(NOTIF_ID, n)
    }

    private fun ensureChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val manager = applicationContext.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val existing = manager.getNotificationChannel(CHANNEL_ID)
        if (existing != null) return
        val ch = NotificationChannel(CHANNEL_ID, "Уведомления", NotificationManager.IMPORTANCE_DEFAULT)
        manager.createNotificationChannel(ch)
    }

    companion object {
        private const val CHANNEL_ID = "app_notifications"
        private const val NOTIF_ID = 2001
        private const val UNIQUE_PERIODIC = "notifications_periodic"
        private const val UNIQUE_ONCE = "notifications_once"

        fun schedule(context: Context) {
            val constraints = Constraints.Builder()
                .setRequiredNetworkType(NetworkType.CONNECTED)
                .build()

            val once = OneTimeWorkRequestBuilder<NotificationsWorker>()
                .setConstraints(constraints)
                .build()

            WorkManager.getInstance(context).enqueueUniqueWork(
                UNIQUE_ONCE,
                ExistingWorkPolicy.REPLACE,
                once
            )

            val periodic = PeriodicWorkRequestBuilder<NotificationsWorker>(15, TimeUnit.MINUTES)
                .setConstraints(constraints)
                .build()

            WorkManager.getInstance(context).enqueueUniquePeriodicWork(
                UNIQUE_PERIODIC,
                ExistingPeriodicWorkPolicy.UPDATE,
                periodic
            )
        }
    }
}

