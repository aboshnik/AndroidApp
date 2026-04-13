package com.example.app

import android.app.NotificationManager
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import androidx.core.app.RemoteInput
import com.example.app.api.ApiClient
import com.example.app.api.SendMessageRequest
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.launch

class ChatNotificationActionReceiver : BroadcastReceiver() {
    companion object {
        const val ACTION_MARK_READ = "com.example.app.ACTION_MARK_READ"
        const val ACTION_REPLY = "com.example.app.ACTION_REPLY"
        const val EXTRA_THREAD_ID = "extra_thread_id"
        const val EXTRA_NOTIFICATION_ID = "extra_notification_id"
        const val KEY_REPLY_TEXT = "key_reply_text"
    }

    override fun onReceive(context: Context, intent: Intent) {
        val threadId = intent.getIntExtra(EXTRA_THREAD_ID, 0)
        val notificationId = intent.getIntExtra(EXTRA_NOTIFICATION_ID, 0)
        if (threadId <= 0) return
        val login = context.getSharedPreferences("auth", Context.MODE_PRIVATE)
            .getString("employeeId", "")?.trim().orEmpty()
        if (login.isBlank()) return

        when (intent.action) {
            ACTION_MARK_READ -> {
                CoroutineScope(Dispatchers.IO).launch {
                    runCatching {
                        // Calling getMessages updates App_ThreadReads on backend.
                        ApiClient.chatApi.getMessages(threadId = threadId, login = login, take = 1)
                    }
                }
                cancelNotification(context, notificationId)
            }

            ACTION_REPLY -> {
                val reply = RemoteInput.getResultsFromIntent(intent)
                    ?.getCharSequence(KEY_REPLY_TEXT)
                    ?.toString()
                    ?.trim()
                    .orEmpty()
                if (reply.isBlank()) return
                CoroutineScope(Dispatchers.IO).launch {
                    runCatching {
                        ApiClient.chatApi.sendMessage(
                            threadId = threadId,
                            body = SendMessageRequest(login = login, text = reply, metaJson = null)
                        )
                        ApiClient.chatApi.getMessages(threadId = threadId, login = login, take = 1)
                    }
                }
                cancelNotification(context, notificationId)
            }
        }
    }

    private fun cancelNotification(context: Context, notificationId: Int) {
        if (notificationId <= 0) return
        val nm = context.getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.cancel(notificationId)
    }
}

