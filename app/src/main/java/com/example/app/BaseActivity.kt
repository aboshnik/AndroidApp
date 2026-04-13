package com.example.app

import android.content.Intent
import android.os.Handler
import android.os.Looper
import android.view.MotionEvent
import android.view.View
import android.widget.TextView
import com.example.app.api.ApiClient
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.launch
import kotlinx.coroutines.withContext

open class BaseActivity : AppCompatActivity() {
    protected val scopeJob = Job()
    protected val scope = CoroutineScope(Dispatchers.Main + scopeJob)
    private val uiHandler = Handler(Looper.getMainLooper())
    private var unreadBadgeRunnable: Runnable? = null
    private var dismissalCheckInFlight = false
    private var swipeStartX = 0f
    private var swipeStartY = 0f
    private var swipeStartAtMs = 0L

    protected open fun swipeTabIndex(): Int? = null

    protected fun canShowUi(): Boolean = !(isFinishing || isDestroyed)

    protected fun safeToast(text: String, long: Boolean = false) {
        if (!canShowUi()) return
        Toast.makeText(this, text, if (long) Toast.LENGTH_LONG else Toast.LENGTH_SHORT).show()
    }

    protected fun safeShowDialog(builder: AlertDialog.Builder): AlertDialog? {
        if (!canShowUi()) return null
        return try {
            builder.show()
        } catch (_: android.view.WindowManager.BadTokenException) {
            null
        }
    }

    override fun onStart() {
        super.onStart()
        enforceEmploymentStatus()
    }

    protected fun startChatsUnreadBadgeAutoRefresh(
        employeeId: String,
        badgeViewId: Int = R.id.navChatsBadge,
        intervalMs: Long = 2500L
    ) {
        stopChatsUnreadBadgeAutoRefresh()
        if (employeeId.isBlank()) return
        val run = object : Runnable {
            override fun run() {
                refreshChatsUnreadBadge(employeeId, badgeViewId)
                uiHandler.postDelayed(this, intervalMs)
            }
        }
        unreadBadgeRunnable = run
        run.run()
    }

    protected fun stopChatsUnreadBadgeAutoRefresh() {
        unreadBadgeRunnable?.let { uiHandler.removeCallbacks(it) }
        unreadBadgeRunnable = null
    }

    private fun refreshChatsUnreadBadge(employeeId: String, badgeViewId: Int) {
        val badge = findViewById<TextView?>(badgeViewId) ?: return
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) {
                    ApiClient.chatApi.getThreads(login = employeeId)
                }
                val body = resp.body()
                val unread = if (resp.isSuccessful && body?.success == true) {
                    body.threads.orEmpty().sumOf { it.unreadCount.coerceAtLeast(0) }
                } else {
                    if (isDismissedMessage(body?.message)) {
                        SessionManager.forceLogoutToAuth(this@BaseActivity, "Профиль заблокирован: сотрудник уволен")
                        return@launch
                    }
                    0
                }
                if (!canShowUi()) return@launch
                if (unread <= 0) {
                    badge.visibility = View.GONE
                } else {
                    badge.visibility = View.VISIBLE
                    badge.text = if (unread > 99) "99+" else unread.toString()
                }
            } catch (_: Exception) {
                if (!canShowUi()) return@launch
                badge.visibility = View.GONE
            }
        }
    }

    private fun enforceEmploymentStatus() {
        if (dismissalCheckInFlight) return
        if (this is MainActivity) return
        if (!SessionManager.isActive(this)) return
        val login = getSharedPreferences("auth", MODE_PRIVATE).getString("login", "")?.trim().orEmpty()
        if (login.isBlank()) return

        dismissalCheckInFlight = true
        scope.launch {
            try {
                val resp = withContext(Dispatchers.IO) { ApiClient.employeeApi.getProfile(login = login) }
                val body = resp.body()
                if (!canShowUi()) return@launch
                if (isDismissedMessage(body?.message)) {
                    SessionManager.forceLogoutToAuth(this@BaseActivity, "Профиль заблокирован: сотрудник уволен")
                }
            } catch (_: Exception) {
                // ignore network errors here; next start/refresh will retry
            } finally {
                dismissalCheckInFlight = false
            }
        }
    }

    private fun isDismissedMessage(message: String?): Boolean {
        val m = message?.trim()?.lowercase().orEmpty()
        if (m.isEmpty()) return false
        return m.contains("сотрудник уволен") || m.contains("профиль заблокирован")
    }

    override fun dispatchTouchEvent(ev: MotionEvent): Boolean {
        val currentTab = swipeTabIndex()
        if (currentTab != null) {
            when (ev.actionMasked) {
                MotionEvent.ACTION_DOWN -> {
                    swipeStartX = ev.x
                    swipeStartY = ev.y
                    swipeStartAtMs = ev.eventTime
                }
                MotionEvent.ACTION_UP -> {
                    val dx = ev.x - swipeStartX
                    val dy = ev.y - swipeStartY
                    val dt = (ev.eventTime - swipeStartAtMs).coerceAtLeast(1L)
                    val minDx = 90f
                    val maxDy = 120f
                    val minVelocityPxPerSec = 220f
                    val velocity = (kotlin.math.abs(dx) * 1000f) / dt.toFloat()
                    if (kotlin.math.abs(dx) >= minDx &&
                        kotlin.math.abs(dy) <= maxDy &&
                        velocity >= minVelocityPxPerSec) {
                        val target = if (dx < 0f) currentTab + 1 else currentTab - 1
                        openTabBySwipe(target)
                        return true
                    }
                }
            }
        }
        return super.dispatchTouchEvent(ev)
    }

    private fun openTabBySwipe(index: Int) {
        val i = when (index) {
            0 -> Intent(this, HomeActivity::class.java)
            1 -> Intent(this, com.example.app.chats.ChatsActivity::class.java)
            2 -> Intent(this, SettingsActivity::class.java)
            3 -> Intent(this, ProfileActivity::class.java)
            else -> null
        } ?: return
        val current = swipeTabIndex() ?: return
        if (index == current) return
        startActivity(i)
        overridePendingTransition(android.R.anim.fade_in, android.R.anim.fade_out)
    }

    override fun onDestroy() {
        stopChatsUnreadBadgeAutoRefresh()
        super.onDestroy()
        scopeJob.cancel()
    }
}
